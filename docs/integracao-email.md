# Integração com e-mail e caixa de SPAM — versão 2.0

Especificação da abertura de chamados por e-mail e da caixa de SPAM. Não faz parte da versão 1, mas as decisões abaixo influenciam o modelo de dados desde o início, em particular `Ticket.Source`, `Ticket.ExternalId` e `User.PasswordHash` anulável.

Escopo funcional geral e regras de negócio: [README.md](../README.md). Decisões de stack: [arquitetura.md](arquitetura.md).

---

## 1. Objetivo

Uma caixa de e-mail monitorada, por exemplo `suporte@empresa.com`, passa a ser um canal de abertura de chamados. Toda mensagem recebida vira um chamado, tendo como solicitante a pessoa que enviou a mensagem, identificada pelo nome e pelo endereço do remetente.

Mensagens classificadas como indesejadas não viram chamado: ficam retidas em uma caixa de SPAM, visível para técnicos e gestores, onde podem ser revisadas e promovidas a chamado.

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

A leitura fica atrás de uma abstração `IMailboxReader`, com duas implementações:

* **IMAP**, via MailKit, usada em desenvolvimento contra o Mailpit e em caixas genéricas;
* **Microsoft Graph**, usada em produção quando a caixa está no Microsoft 365, alinhada ao roadmap de Azure e Entra ID do README.

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

Neste caso:

* o corpo da mensagem vira um comentário **público** no chamado, com autoria do solicitante;
* se o chamado estava em "Aguardando usuário", ele volta para "Em atendimento" e o SLA é retomado conforme a seção 8.2 do README;
* se o chamado estava "Resolvido" ou "Fechado", ele é **reaberto** para "Em atendimento", mantendo o mesmo código e todo o histórico;
* o evento é registrado no histórico do chamado.

Reabertura por resposta de e-mail tem um limite: mensagens que chegam mais de trinta dias após o fechamento criam um chamado novo, referenciando o anterior no corpo. Sem esse corte, uma thread antiga ressuscita indefinidamente um chamado já encerrado.

### 4.5 Identificação do solicitante

O sistema procura um `User` ativo cujo e-mail seja igual ao endereço do remetente, em comparação sem distinção de maiúsculas.

* **Encontrou**: esse usuário é o solicitante.
* **Não encontrou**: um `User` é criado automaticamente com o nome e o endereço do remetente, perfil `Usuário`, `PasswordHash` nulo e `IsActive = true`.

O usuário criado assim consegue ser solicitante e receber respostas por e-mail, mas não consegue autenticar no portal enquanto não definir uma senha pelo fluxo de primeiro acesso. Quando isso acontece, ele reencontra pelo portal todos os chamados que abriu por e-mail, porque são o mesmo registro.

**Por quê:** guardar apenas o endereço solto no chamado obrigaria todo o sistema — autorização, listagem, dashboard — a lidar com dois tipos de solicitante. Criar o usuário de verdade mantém uma única regra de "meus chamados" e uma única relação no banco.

Se o nome do remetente vier vazio, usa-se a parte do endereço anterior ao `@`.

### 4.6 Classificação de spam

A classificação é por regras, avaliadas nesta ordem:

1. `SpamRule` com ação `Allow` que case com remetente ou domínio: mensagem liberada, fluxo segue;
2. `SpamRule` com ação `Block` que case: mensagem marcada como spam;
3. remetente com domínio externo ao da organização, quando a conta estiver configurada para aceitar apenas domínios internos: marcada como spam;
4. falha de autenticação do remetente nos cabeçalhos `Authentication-Results`, em SPF, DKIM ou DMARC: marcada como spam;
5. heurísticas de conteúdo, como assunto vazio, corpo vazio ou excesso de links: somam `SpamScore` e, acima do limite configurado, marcam como spam.

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

Erro durante o processamento grava status `Failed` e a mensagem em `ProcessingError`, sem perder o `InboundEmail`. Há reprocessamento manual pela interface e tentativa automática com espera crescente, até três vezes.

---

## 5. Caixa de SPAM

### 5.1 Acesso

Visível para **técnicos** e **gestores**. O perfil Usuário não tem acesso, nem sabe que a tela existe.

### 5.2 Tela

Uma aba **SPAM** na área de atendimento, ao lado da caixa de entrada de e-mails, listando os `InboundEmail` com status `Spam`.

Cada linha mostra remetente, assunto, data de recebimento, motivo da classificação e pontuação. A visualização mostra o corpo da mensagem em modo somente leitura, com HTML sanitizado e sem carregar imagens ou recursos externos.

Filtros: período, remetente, domínio e motivo da classificação.

### 5.3 Ações

| Ação | Efeito |
| --- | --- |
| **Não é spam** | executa as etapas 4.4 a 4.7 e cria o chamado, ou vincula ao chamado existente |
| **Não é spam e liberar remetente** | o mesmo, e cria uma `SpamRule` do tipo `Allow` para o remetente |
| **Confirmar spam** | mantém a retenção e cria uma `SpamRule` do tipo `Block` para o remetente |
| **Bloquear domínio** | cria uma `SpamRule` do tipo `Block` para o domínio inteiro |
| **Excluir** | remove o `InboundEmail`; disponível apenas para gestores |

Toda ação registra o autor e o instante. A criação de regra é sempre explícita: nada é aprendido em silêncio a partir de um clique.

### 5.4 Retenção

Mensagens em `Spam` são excluídas automaticamente após noventa dias. Mensagens `Duplicate` após trinta. Mensagens `Processed` mantêm apenas os cabeçalhos e a referência ao chamado depois de um ano, já que o conteúdo relevante virou chamado.

---

## 6. Envio de e-mail

A integração de recebimento pressupõe a de envio, entregue junto na versão 2.0 e antecipada em parte na 1.2 do roadmap.

Quando um técnico comenta publicamente em um chamado de origem `Email`, o solicitante recebe a resposta por e-mail, com:

* o código do chamado no assunto, no formato `[OPS-000123]`;
* cabeçalhos `In-Reply-To` e `References` apontando para a mensagem original, para que a resposta do solicitante caia na etapa 4.4;
* uma marcação clara do ponto acima do qual se deve escrever, para facilitar a remoção do texto citado.

Comentários internos nunca são enviados por e-mail. Essa é a mesma regra da seção 15 do README, e vale aqui com a mesma força: a caixa de saída é apenas mais um lugar onde ela pode ser violada.

---

## 7. Fora do escopo desta integração

* anexos de e-mail, que dependem da entrega de anexos prevista na versão 1.1;
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
