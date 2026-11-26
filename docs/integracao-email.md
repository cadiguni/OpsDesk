# Integração com e-mail e caixa de SPAM — versão 2.0

Especificação da abertura de chamados por e-mail e da caixa de SPAM. **Entregue na versão 2.0.** As seções 1 a 8 são a especificação, com as regras que valem. A seção 9 registra onde a implementação se afastou dela e por quê, e a seção 10 o que ficou para depois.

Escopo funcional geral e regras de negócio: [README.md](../README.md). Decisões de stack: [arquitetura.md](arquitetura.md).

---

## 1. Objetivo

Uma caixa de e-mail monitorada, por exemplo `suporte@empresa.com`, passa a ser um canal de abertura de chamados. Toda mensagem recebida vira um chamado, tendo como solicitante a pessoa que enviou a mensagem, identificada pelo nome e pelo endereço do remetente.

Mensagens classificadas como indesejadas não viram chamado: ficam retidas em uma caixa de SPAM, visível para técnicos e administradores, onde podem ser revisadas e promovidas a chamado.

---

## 2. Princípio central: e-mail recebido e chamado são coisas separadas

Toda mensagem recebida é persistida como `InboundEmail`, independentemente do destino que terá. O chamado é uma consequência possível dessa mensagem, não a mensagem em si.

```
Mensagem recebida
      │
      ▼
 InboundEmail  ──── registrado sempre, mesmo se for spam ou duplicata
      │
      ├── duplicata do Message-ID ──────────► descartada, status Duplicate
      ├── resposta a chamado existente ─────► vira comentário no chamado
      ├── classificada como spam ───────────► fica na caixa de SPAM
      └── mensagem legítima nova ───────────► cria chamado
```

**Por que não usar um status "Spam" no próprio chamado:** spam não é um estágio do ciclo de vida de um chamado. Se virasse status, todo relatório, contador de SLA e listagem do sistema passaria a precisar de uma exceção para ignorá-lo, e a máquina de estados da seção 5 do README ganharia um ramo que não tem nada a ver com atendimento. Separando as duas entidades, spam nunca chega perto da base de chamados.

---

## 3. Entidades

### EmailAccount

Caixa monitorada. Mais de uma pode existir, por exemplo uma por departamento.

Campos:

* Id;
* Name;
* Address;
* Protocol;
* DefaultCategoryId;
* DefaultPriority;
* IsActive;
* LastSyncAt;
* CreatedAt;
* UpdatedAt.

Credenciais não ficam nesta tabela. Elas vêm de configuração da aplicação, por *secret* de ambiente ou Azure Key Vault.

### InboundEmail

Mensagem recebida, guardada como veio.

Campos:

* Id;
* EmailAccountId;
* MessageId;
* InReplyTo;
* References;
* FromName;
* FromAddress;
* ToAddress;
* Subject;
* BodyText;
* BodyHtml;
* ReceivedAt;
* Status;
* SpamScore;
* SpamReason;
* TicketId;
* ProcessedAt;
* ProcessingError;
* CreatedAt.

`Status` assume: `Pending`, `Processed`, `Spam`, `Duplicate`, `Failed`.

### SpamRule

Regra de classificação mantida pela equipe.

Campos:

* Id;
* Type;
* Pattern;
* Action;
* CreatedById;
* IsActive;
* CreatedAt.

`Type` assume: `SenderAddress`, `SenderDomain`, `SubjectContains`, `BodyContains`.
`Action` assume: `Allow`, `Block`.

---

## 4. Fluxo de processamento

O processamento é sequencial e cada etapa pode encerrar o fluxo.

### 4.1 Coleta

Um `BackgroundService` consulta cada `EmailAccount` ativa em intervalo configurável, com padrão de um minuto. Cada mensagem lida é gravada como `InboundEmail` com status `Pending` e marcada como lida na caixa apenas depois da gravação bem-sucedida.

A leitura fica atrás de uma abstração `IMailboxReader`. A implementação entregue é a do **Microsoft Graph**, com o mesmo aplicativo do Entra ID que envia (seção 6). IMAP ficou de fora; ver seção 9.

### 4.2 Descarte de duplicatas

Se já existir `InboundEmail` com o mesmo `MessageId` na mesma conta, a nova mensagem recebe status `Duplicate` e o fluxo termina.

**Por quê:** reentrega de servidor de e-mail é comum, e sem essa verificação o mesmo problema vira três chamados.

### 4.3 Descarte de mensagens automáticas

A mensagem é descartada, com status `Spam` e motivo registrado, quando:

* possui cabeçalho `Auto-Submitted` diferente de `no`;
* possui `X-Auto-Response-Suppress` ou `Precedence: bulk`;
* o remetente é um endereço do tipo `no-reply` ou `noreply`;
* o remetente é o próprio endereço da caixa monitorada;
* a mensagem é uma notificação de falha de entrega.

**Por quê:** sem essa barreira, uma resposta automática de férias combinada com uma notificação do próprio sistema produz um laço de mensagens entre as duas caixas.

### 4.4 Vínculo com chamado existente

A mensagem é tratada como resposta, e não como chamado novo, quando:

1. os cabeçalhos `In-Reply-To` ou `References` apontam para o `Message-ID` de uma mensagem já associada a um chamado; ou
2. o assunto contém o código do chamado no formato `[OPS-000123]`.

A checagem por cabeçalho vem primeiro, por ser confiável; o assunto é o plano B, para clientes de e-mail que reescrevem a linha.

**O vínculo respeita a visibilidade.** Só vale para quem enxerga o chamado: o solicitante dele ou alguém da equipe. Quem põe `[OPS-000123]` no assunto sem ser nenhum dos dois não comenta no chamado alheio. A mensagem vira chamado novo, dele. Para isso o processamento age **em nome do remetente**, com o mesmo serviço de comentário do portal: histórico, notificação e regra de reabertura são os mesmos, sem caminho paralelo.

Neste caso:

* o corpo da mensagem vira um comentário **público** no chamado, com autoria do solicitante;
* se o chamado estava em "Aguardando usuário", ele volta para "Em atendimento" e o SLA é retomado conforme a seção 8.2 do README;
* se o chamado estava "Resolvido" ou "Fechado", ele é **reaberto** para "Em atendimento", mantendo o mesmo código e todo o histórico;
* o evento é registrado no histórico do chamado.

Reabertura por resposta de e-mail tem um limite, o mesmo da reabertura pelo portal (README, seção 5.9): mensagens que chegam depois da janela de reabertura — sete dias do fechamento, em `Tickets:ReopenWindowDays` — criam um chamado novo, referenciando o anterior no corpo. Sem esse corte, uma thread antiga ressuscita indefinidamente um chamado já encerrado.

### 4.5 Identificação do solicitante

O sistema procura um `User` ativo cujo e-mail seja igual ao endereço do remetente, em comparação sem distinção de maiúsculas.

* **Encontrou**: esse usuário é o solicitante.
* **Não encontrou**: um `User` é criado automaticamente com o nome e o endereço do remetente, perfil `Usuário`, `PasswordHash` nulo e `IsActive = true`.

O usuário criado assim consegue ser solicitante e receber respostas por e-mail, mas não consegue autenticar no portal enquanto não definir uma senha pelo fluxo de primeiro acesso. Quando isso acontece, ele reencontra pelo portal todos os chamados que abriu por e-mail, porque são o mesmo registro.

**Por quê:** guardar apenas o endereço solto no chamado obrigaria todo o sistema — autorização, listagem, dashboard — a lidar com dois tipos de solicitante. Criar o usuário de verdade mantém uma única regra de "meus chamados" e uma única relação no banco.

Se o nome do remetente vier vazio, usa-se a parte do endereço anterior ao `@`.

Remetente cuja conta existe mas está **desativada** não abre chamado: a mensagem vai para a caixa de SPAM com o motivo "Remetente com conta desativada". Desativar uma conta não pode ter como contorno mandar e-mail.

### 4.6 Classificação de spam

A classificação é por regras, avaliadas nesta ordem:

1. `SpamRule` com ação `Allow` que case com remetente ou domínio: mensagem liberada, fluxo segue;
2. `SpamRule` com ação `Block` que case: mensagem marcada como spam;
3. remetente com domínio externo ao da organização, quando a conta estiver configurada para aceitar apenas domínios internos: marcada como spam;
4. falha de autenticação do remetente nos cabeçalhos `Authentication-Results`, em SPF, DKIM ou DMARC: marcada como spam;
5. heurísticas de conteúdo, que somam `SpamScore`: assunto vazio (2), corpo vazio (3), mais de cinco links (2) ou mais de dez (4). A partir de 5 pontos, a mensagem é marcada como spam.

`SpamReason` sempre registra qual regra decidiu, para que a revisão manual seja auditável.

Não há classificação por aprendizado de máquina nesta versão. As regras cobrem o caso real de uma caixa de suporte interna, e o operador tem a caixa de SPAM para corrigir o que escapar.

### 4.7 Criação do chamado

Mensagem legítima que não é resposta vira chamado com:

| Campo | Valor |
| --- | --- |
| `Title` | assunto da mensagem, truncado; assunto vazio vira "Chamado sem assunto" |
| `Description` | corpo em texto puro, com HTML convertido e sanitizado |
| `RequesterId` | usuário identificado ou criado na etapa 4.5 |
| `CategoryId` | `DefaultCategoryId` da conta, com "Outros" como padrão |
| `Priority` | `DefaultPriority` da conta, com "Média" como padrão |
| `Status` | Aberto |
| `Source` | `Email` |
| `ExternalId` | `Message-ID` da mensagem |
| `AssignedTechnicianId` | nulo |

Prioridade nunca é inferida do conteúdo. Remetente ansioso escreve "URGENTE" no assunto, e deixar isso definir prioridade entrega o controle da fila de atendimento a quem escreve o e-mail. A triagem continua sendo da equipe.

O `InboundEmail` passa a `Processed` com `TicketId` preenchido.

### 4.8 Falhas

Erro durante o processamento grava status `Failed` e a mensagem em `ProcessingError`, sem perder o `InboundEmail`. Há reprocessamento manual pela interface e tentativa automática com espera crescente — 1, 5 e 15 minutos —, até três vezes.

Reprocessar é seguro: se a falha aconteceu depois de o chamado existir, o reprocessamento o encontra pelo `ExternalId` em vez de criar outro.

---

## 5. Caixa de SPAM

### 5.1 Acesso

Visível para **técnicos** e **administradores**. O perfil Usuário não tem acesso, nem sabe que a tela existe.

### 5.2 Tela

Item **E-mails** no menu da equipe, com as abas **SPAM**, **Falhas**, **Processados**, **Duplicados** e **Regras**.

Cada linha mostra remetente, assunto, data de recebimento, motivo da classificação e pontuação. A visualização mostra o corpo da mensagem em texto puro, somente leitura. Como o corpo é guardado só em texto (seção 9), não há HTML para sanitizar nem recurso externo para bloquear.

Filtros: período e busca por remetente ou assunto.

### 5.3 Ações

| Ação | Efeito |
| --- | --- |
| **Não é spam** | executa as etapas 4.4 a 4.7 e cria o chamado, ou vincula ao chamado existente |
| **Não é spam e liberar remetente** | o mesmo, e cria uma `SpamRule` do tipo `Allow` para o remetente |
| **Confirmar spam** | mantém a retenção e cria uma `SpamRule` do tipo `Block` para o remetente |
| **Bloquear domínio** | cria uma `SpamRule` do tipo `Block` para o domínio inteiro |
| **Excluir** | remove o `InboundEmail`; disponível apenas para administradores |

Toda ação registra o autor e o instante. A criação de regra é sempre explícita: nada é aprendido em silêncio a partir de um clique.

### 5.4 Retenção

Mensagens em `Spam` são excluídas automaticamente após noventa dias. Mensagens `Duplicate` após trinta. Mensagens `Processed` mantêm apenas os cabeçalhos e a referência ao chamado depois de um ano, já que o conteúdo relevante virou chamado.

---

## 6. Envio de e-mail

A integração de recebimento pressupõe a de envio, entregue junto na versão 2.0 e antecipada em parte na 1.2 do roadmap.

O canal de envio é o da 1.2 (README, seção 18): Microsoft Graph, configurado pelo administrador no portal, com fila de saída. O remetente é a caixa monitorada, para que a resposta do solicitante volte para ela e caia na etapa 4.4.

Sem caixa monitorada ativa, o e-mail continua avisando que respostas a ele não são lidas. Com uma caixa ativa, o aviso dá lugar a "responda este e-mail", e o e-mail ganha:

* o código do chamado no assunto, no formato `[OPS-000123]` — o que já valia desde a 1.2;
* a linha "Responda acima desta linha", acima da qual o solicitante escreve. Na volta, o texto abaixo dela é descartado, assim como os blocos de citação que os clientes de e-mail acrescentam ("Em ... escreveu:", "On ... wrote:", o cabeçalho "De:/Enviado:" do Outlook e as linhas com `>`).

Vale para chamado de qualquer origem, não só de `Email`: quem abriu pelo portal também pode responder pelo e-mail que recebeu.

Os cabeçalhos `In-Reply-To` e `References` previstos aqui não são enviados; ver seção 9.

### 6.1 Aplicativo no Entra ID

Tarefa do administrador do Microsoft 365, uma vez:

1. registrar um aplicativo no Entra ID, de locatário único, e criar um *client secret*. Anote a validade: a tela de configuração avisa antes de vencer;
2. conceder as permissões **de aplicativo** do Microsoft Graph, com consentimento do administrador:
   * `Mail.Send`, para enviar;
   * `Mail.ReadWrite`, só se houver caixa monitorada — ler e marcar como lida;
3. **restringir o aplicativo à caixa de suporte**, por *Application Access Policy* ou *RBAC for Applications* do Exchange Online. Sem a restrição, a permissão vale para todas as caixas da empresa, e um secret vazado lê e envia e-mail em nome de qualquer pessoa;
4. no OpsDesk, em **Configurações → E-mail**, preencher tenant, client id, secret e remetente, e usar **enviar e-mail de teste**. A caixa monitorada é cadastrada na mesma tela, com categoria e prioridade padrão.

### 6.2 O mesmo aplicativo para o login com a conta Microsoft

Opcional. Para o botão "Entrar com a conta Microsoft" (README, seção 13.1), no mesmo aplicativo:

1. em **Autenticação**, adicionar a plataforma **Web** com o URI de redirecionamento que a tela **Configurações → Geral** mostra — `https://<endereço do portal>/api/auth/microsoft/callback`. Em desenvolvimento, `http://localhost:8080/api/auth/microsoft/callback`;
2. em **Permissões de API**, as permissões **delegadas** `openid`, `profile` e `email` do Microsoft Graph, com consentimento do administrador para a organização — sem ele, cada pessoa veria uma tela de consentimento no primeiro acesso;
3. o tenant na tela de e-mail precisa ser o ID ou o domínio da empresa. Com `common` ou `organizations` o login não liga;
4. em **Configurações → Geral**, ligar **Permitir entrar com a conta Microsoft**.

O envio de e-mail não precisa estar ligado: o login usa só tenant, client id e secret. O secret é o mesmo — quando vencer, param os dois.

Comentários internos nunca são enviados por e-mail. Essa é a mesma regra da seção 15 do README, e vale aqui com a mesma força: a caixa de saída é apenas mais um lugar onde ela pode ser violada.

---

## 7. Fora do escopo desta integração

* anexos de e-mail. A entrega de anexos existe desde a 1.0, mas trazer os da mensagem ficou para depois (seção 10);
* criptografia de mensagem, S/MIME ou PGP;
* classificação por aprendizado de máquina;
* múltiplos idiomas na detecção de conteúdo;
* mensagens com mais de um destinatário virando chamados distintos;
* encaminhamento de chamado para caixas externas.

---

## 8. Critérios de aceite

A integração está concluída quando:

* uma mensagem enviada à caixa monitorada cria um chamado com o remetente como solicitante;
* um remetente desconhecido passa a existir como usuário e reencontra seus chamados ao acessar o portal;
* a resposta do solicitante à notificação do chamado vira comentário no chamado certo, e não um chamado novo;
* uma resposta automática de férias não cria chamado;
* a mesma mensagem reentregue pelo servidor não cria dois chamados;
* uma mensagem retida em SPAM pode ser promovida a chamado em um clique;
* confirmar spam impede que o mesmo remetente gere chamado de novo;
* nenhum comentário interno é enviado por e-mail.

Todos têm teste de integração, em `InboundEmailTests`.

---

## 9. Onde a implementação se afastou da especificação

| Especificação | Implementação | Motivo |
| --- | --- | --- |
| leitura por IMAP (MailKit) e por Graph | só Graph | a empresa usa Microsoft 365, e o mesmo aplicativo do Entra ID já envia. A Microsoft está desligando a autenticação básica no IMAP do Exchange Online, e IMAP com OAuth dá o mesmo trabalho de registro sem ganho. Os testes usam uma caixa falsa atrás de `IMailboxReader`, o que dispensou o Mailpit |
| `EmailAccount.Protocol` | não existe | com um protocolo só, o campo não decide nada. Entram `InternalDomainsOnly` (a regra 3 da seção 4.6, com os domínios das configurações gerais) e `LastError`, que a tela mostra |
| credencial da caixa em configuração da aplicação | a mesma credencial do envio, cifrada no banco | é o mesmo aplicativo do Entra ID, configurado pelo administrador na tela de e-mail. A regra de credencial só de escrita e cifrada vale igual (invariante 12 do CLAUDE.md) |
| `BodyText` e `BodyHtml` | só `BodyText` | o Graph entrega o corpo já convertido em texto quando pedido. Sem HTML guardado, não há sanitização a errar nem imagem externa a bloquear. Os cabeçalhos vão para `HeadersJson`, e `Attempts`, `NextAttemptAt`, `ReviewedById` e `ReviewedAt` sustentam as tentativas e a revisão |
| `In-Reply-To` e `References` na resposta | código no assunto e linha de corte | o `sendMail` do Graph só aceita cabeçalho personalizado (`x-`), não esses. O vínculo da etapa 4.4 funciona pelos cabeçalhos entre mensagens recebidas do mesmo fio e, para a resposta ao e-mail do OpsDesk, pelo código no assunto |

---

## 10. Para depois

* **anexos da mensagem.** Entrariam pelo caminho de anexo pendente e vinculado, ligados ao comentário ou à abertura que a mensagem virar, com os mesmos limites de tipo e tamanho;
* **IMAP**, se aparecer caixa fora do Microsoft 365. É outra implementação de `IMailboxReader`;
* **assinatura de webhook do Graph** no lugar da consulta a cada minuto, se o atraso incomodar.
