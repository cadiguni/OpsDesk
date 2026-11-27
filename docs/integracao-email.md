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

### 4.7.1 Anexos e imagens da mensagem

Os anexos de arquivo da mensagem entram no chamado: na abertura, quando a mensagem vira chamado novo, ou no comentário, quando é resposta. Isso inclui as imagens **inline**, como o print colado no corpo do e-mail.

* **Caminho:** é o mesmo do portal (invariante 9 do CLAUDE.md). Cada arquivo é gravado como anexo pendente do remetente e vinculado na mesma entrega; o comentário vindo de e-mail é sempre público.
* **Limites:** a mesma lista de tipos, os mesmos 10 MB por arquivo e o mesmo teto por chamado do portal. O que não serve fica de fora sem impedir o chamado. Arquivo grande demais nem é baixado.
* **Quando baixa:** na entrega, e não na leitura da caixa. Duplicata, resposta automática e spam não ocupam armazenamento. Mensagem liberada da caixa de SPAM baixa os anexos nesse momento; se ela já tiver sido apagada da caixa de correio, o chamado sai sem eles. Falha do Graph ao baixar falha o processamento, que volta à fila (4.8) antes de o chamado existir.
* **Marca no texto:** no texto puro que o Graph entrega, cada imagem inline aparece como `[cid:…]`. A marca vira `[imagem: nome.png]`, e o arquivo aparece como miniatura logo abaixo. A marca de imagem que não foi trazida vira só `[imagem]`.
* **Assinatura repetida:** a imagem inline da assinatura volta em toda resposta. Num chamado que já tem uma imagem inline com o mesmo nome e tamanho, ela não entra de novo.
* **Imagem externa:** a imagem que a assinatura carrega de um site, como um GIF hospedado no servidor da empresa, não é anexo. Ela continua como o endereço entre colchetes no texto e não é carregada, pelo mesmo motivo do corpo em texto puro (seção 9): carregar imagem externa avisa o remetente de quem abriu e quando. O nome técnico disso é *pixel de rastreamento*.

**Mensagens antigas.** As mensagens lidas antes desta versão não guardaram o identificador do provedor (`ProviderId`) e entraram sem anexos. O administrador as recupera em **Configurações → E-mail → Anexos de e-mails antigos** (`/api/inbound/attachment-recovery`):

* **Busca:** cada mensagem é procurada na caixa monitorada pelo Message-ID, em qualquer pasta. Os anexos vão para onde a mensagem foi parar:
  * na abertura, quando o chamado nasceu dela;
  * no comentário da resposta, quando foi resposta. O comentário é o do remetente gravado junto com o processamento da mensagem, até dez minutos de distância.
* **Regras:** valem as mesmas desta seção. A marca `[cid:…]` vira o nome do arquivo, a imagem da assinatura não se repete e os limites são os mesmos.
* **Sem evento:** corrigir a exibição não é evento do chamado. O texto é trocado direto no banco, sem passar pelos interceptores: nada entra no histórico, ninguém é avisado e o "atualizado em" não muda.
* **Lotes:** o servidor trabalha em lotes de 20 e a tela os encadeia. Repetir é seguro, porque a mensagem recuperada ganha o `ProviderId` e sai da fila.
* **Sem origem:** mensagem já apagada da caixa de correio não tem de onde vir. Ela conta como não encontrada e continua na fila, para o caso de reaparecer.

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

Envio e recebimento são independentes: dá para receber chamados por e-mail com o envio desligado, e enviar avisos sem caixa monitorada. Os dois usam o mesmo aplicativo do Entra ID (seção 6.1); o envio foi antecipado em parte na 1.2 do roadmap.

O canal de envio é o da 1.2 (README, seção 18): Microsoft Graph, configurado pelo administrador no portal, com fila de saída. O remetente é a caixa monitorada, para que a resposta do solicitante volte para ela e caia na etapa 4.4.

Sem caixa monitorada ativa, o e-mail continua avisando que respostas a ele não são lidas. Com uma caixa ativa, o aviso dá lugar a "responda este e-mail", e o e-mail ganha:

* o código do chamado no assunto, no formato `[OPS-000123]` — o que já valia desde a 1.2;
* a linha "Responda acima desta linha", acima da qual o solicitante escreve. Na volta, o texto abaixo dela é descartado, assim como os blocos de citação que os clientes de e-mail acrescentam ("Em ... escreveu:", "On ... wrote:", o cabeçalho "De:/Enviado:" do Outlook e as linhas com `>`).

Vale para chamado de qualquer origem, não só de `Email`: quem abriu pelo portal também pode responder pelo e-mail que recebeu.

Os cabeçalhos `In-Reply-To` e `References` previstos aqui não são enviados; ver seção 9.

### 6.1 Aplicativo no Entra ID: passo a passo

Feito uma vez, por quem administra o Microsoft 365. O OpsDesk não entra na caixa com usuário e senha: ele age por um **aplicativo registrado no Entra ID**, com permissões do Microsoft Graph. A caixa em si não precisa de configuração nenhuma.

O que cada parte precisa:

| Para | Permissão **de aplicativo** do Microsoft Graph |
| --- | --- |
| receber chamados (caixa monitorada) | `Mail.ReadWrite` — ler e marcar como lida |
| enviar avisos ao solicitante | `Mail.Send` |
| entrar com a conta Microsoft | nenhuma de aplicativo; as delegadas da seção 6.2 |

Dá para começar só pelo recebimento e ligar o envio depois.

#### 1. A caixa de suporte

No **Microsoft 365 admin center** → **Equipes e grupos** → **Caixas de correio compartilhadas** → **Adicionar**. Uma caixa compartilhada não consome licença e não tem senha de login. Quem da equipe quiser abri-la no Outlook entra como membro; o OpsDesk não precisa disso.

Use **o mesmo endereço** para a caixa monitorada e para o remetente do envio. O solicitante responde ao aviso, a resposta cai na caixa monitorada e volta para o chamado. Remetente diferente da caixa monitorada só funciona se o remetente também for cadastrado como caixa monitorada: o envio não define `Reply-To`.

#### 2. Registrar o aplicativo

**Entra admin center** → **Aplicativos** → **Registros de aplicativo** → **Novo registro**:

* tipos de conta: **somente neste diretório organizacional** (locatário único);
* sem URI de redirecionamento por enquanto (só o login Microsoft precisa, seção 6.2).

Na **Visão geral** do aplicativo estão dois dos três valores que o OpsDesk pede:

* **ID do aplicativo (cliente)** → campo **Client id**;
* **ID do diretório (locatário)** → campo **Tenant**. O domínio `.onmicrosoft.com` também serve; `common` e `organizations` não.

#### 3. Criar o client secret

**Certificados e segredos** → **Novo segredo do cliente**. Copie a coluna **Valor** — não o "ID do segredo" — na hora: ela não aparece de novo. Anote a validade e informe-a no OpsDesk, que avisa antes de vencer. Quando o secret vence, param leitura, envio e login Microsoft juntos.

#### 4. Conceder as permissões

**Permissões de API** → **Adicionar uma permissão** → **Microsoft Graph** → **Permissões de aplicativo** (não "Permissões delegadas"):

* `Mail.ReadWrite`, para receber;
* `Mail.Send`, quando for enviar.

Depois, **Conceder consentimento do administrador**. A coluna "Status" precisa ficar verde.

**Permissão delegada no lugar da de aplicativo é o erro mais comum.** O sintoma é o Graph responder 403 "Access is denied" com tenant, client id e secret corretos: o aplicativo autentica, mas não tem permissão sobre a caixa.

#### 5. Configurar o OpsDesk

**Configurações → E-mail**:

1. em **Aplicativo e envio**, preencha tenant, client id, client secret e validade, e salve. Para só receber, deixe **Enviar e-mails aos solicitantes** desligado;
2. em **Caixas monitoradas**, cadastre a caixa com categoria e prioridade padrão. A leitura acontece a cada minuto;
3. para enviar: preencha o endereço remetente, ligue o envio, salve e use **Enviar e-mail de teste**.

Com isso a integração funciona. O passo seguinte não é obrigatório, mas é recomendado.

#### 6. Restringir o aplicativo à caixa de suporte

Permissão de aplicativo concedida no Entra ID vale para **todas as caixas da organização**. Quem obtiver o secret lê a caixa de qualquer pessoa (`Mail.ReadWrite`) ou envia em nome dela (`Mail.Send`). A restrição a uma caixa é feita no Exchange Online, pelo **RBAC for Applications**, e só existe por PowerShell — não há tela para ela. Precisa do perfil de administrador do Exchange.

Não é preciso instalar nada: o **Cloud Shell** do portal do Azure já traz o módulo do Exchange Online.

**a. O identificador do aplicativo empresarial.** **Entra admin center** → **Aplicativos** → **Aplicativos empresariais** → o aplicativo → **Visão geral** → **ID do objeto**. Tem que ser o desta tela: o "ID do objeto" de *Registros de aplicativo* é outro número, e com ele o comando falha.

**b. Abrir o Cloud Shell.** No **portal do Azure**, ícone **`>_`** na barra superior → **PowerShell**. Se perguntar sobre armazenamento, **nenhuma conta de armazenamento necessária** basta.

**c. Criar o escopo e as atribuições**, trocando os valores entre `< >`:

```powershell
Connect-ExchangeOnline -Device
# abra o link exibido, digite o código e entre com uma conta de administrador do Exchange

New-ServicePrincipal -AppId "<client id>" -ObjectId "<ID do objeto do aplicativo empresarial>" -DisplayName "OpsDesk"

New-ManagementScope -Name "OpsDesk-suporte" -RecipientRestrictionFilter "PrimarySmtpAddress -eq '<caixa de suporte>'"

New-ManagementRoleAssignment -App "<client id>" -Role "Application Mail.ReadWrite" -CustomResourceScope "OpsDesk-suporte"
New-ManagementRoleAssignment -App "<client id>" -Role "Application Mail.Send" -CustomResourceScope "OpsDesk-suporte"   # se for enviar
```

Para mais de uma caixa monitorada, o filtro do escopo aceita `-or`, ou um grupo: `MemberOfGroup -eq '<DN do grupo>'`.

**d. Conferir:**

```powershell
Test-ServicePrincipalAuthorization -Identity "<client id>" -Resource "<caixa de suporte>"
```

`InScope` deve vir `True` para a caixa de suporte, e `False` para qualquer outra caixa testada.

**e. Remover as permissões amplas do Entra ID.** **Este passo é o que faz a restrição valer.** As permissões do Entra ID e as do Exchange se somam: enquanto `Mail.ReadWrite` e `Mail.Send` de aplicativo continuarem concedidas em **Permissões de API**, elas seguem liberando todas as caixas, com escopo ou sem. Em cada uma, **⋯ → Revogar consentimento do administrador** e depois **⋯ → Remover permissão**. As delegadas do login Microsoft (seção 6.2) ficam.

**f. Testar de novo** no OpsDesk. A troca de permissões leva de minutos a cerca de uma hora para propagar; um 403 logo depois costuma ser só isso.

#### Conta de envio separada

Ferramentas como Freshdesk e Spiceworks costumam enviar por uma conta própria, com usuário e senha em SMTP. O OpsDesk não faz isso: envia e lê só pelo Microsoft Graph, com o aplicativo acima. A Microsoft está desligando a autenticação básica (usuário e senha) do SMTP e do IMAP no Exchange Online, então o aplicativo com permissão restrita é o caminho que continua funcionando.

Uma caixa só para enviar funciona: basta usá-la como remetente e incluí-la no escopo do passo 6. Mas, como o envio não define `Reply-To`, a resposta do solicitante vai para essa caixa, e só volta ao chamado se ela também for cadastrada como caixa monitorada. Na maioria dos casos, a mesma caixa para tudo é mais simples.

### 6.2 O mesmo aplicativo para o login com a conta Microsoft

Opcional. Para o botão "Entrar com a conta Microsoft" (README, seção 13.1), no mesmo aplicativo:

1. em **Autenticação**, adicionar a plataforma **Web** com o URI de redirecionamento que a tela **Configurações → Geral** mostra — `https://<endereço do portal>/api/auth/microsoft/callback`. Em desenvolvimento, `http://localhost:8080/api/auth/microsoft/callback`;
2. em **Permissões de API**, as permissões **delegadas** `openid`, `profile` e `email` do Microsoft Graph, com consentimento do administrador para a organização — sem ele, cada pessoa veria uma tela de consentimento no primeiro acesso;
3. o tenant na tela de e-mail precisa ser o ID ou o domínio da empresa. Com `common` ou `organizations` o login não liga;
4. em **Configurações → Geral**, ligar **Permitir entrar com a conta Microsoft**.

O envio de e-mail não precisa estar ligado: o login usa só tenant, client id e secret. O secret é o mesmo — quando vencer, param login, leitura e envio juntos.

Comentários internos nunca são enviados por e-mail. Essa é a mesma regra da seção 15 do README, e vale aqui com a mesma força: a caixa de saída é apenas mais um lugar onde ela pode ser violada.

---

## 7. Fora do escopo desta integração

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
| recebimento pressupõe o envio | independentes | a leitura usa só tenant, client id e secret, sem exigir o envio ligado nem remetente. Receber chamado por e-mail costuma vir antes de avisar por e-mail, e a primeira versão parava a leitura quando o envio estava desligado |
| `BodyText` e `BodyHtml` | só `BodyText` | o Graph entrega o corpo já convertido em texto quando pedido. Sem HTML guardado, não há sanitização a errar nem imagem externa a bloquear. Os cabeçalhos vão para `HeadersJson`, e `Attempts`, `NextAttemptAt`, `ReviewedById` e `ReviewedAt` sustentam as tentativas e a revisão |
| `In-Reply-To` e `References` na resposta | código no assunto e linha de corte | o `sendMail` do Graph só aceita cabeçalho personalizado (`x-`), não esses. O vínculo da etapa 4.4 funciona pelos cabeçalhos entre mensagens recebidas do mesmo fio e, para a resposta ao e-mail do OpsDesk, pelo código no assunto |

---

## 10. Para depois

* **IMAP**, se aparecer caixa fora do Microsoft 365. É outra implementação de `IMailboxReader`;
* **assinatura de webhook do Graph** no lugar da consulta a cada minuto, se o atraso incomodar.
