# Documento Funcional — OpsDesk

## 1. Nome do projeto

**OpsDesk**

Sistema interno para gerenciamento de chamados de TI, inspirado em ferramentas como GLPI, Freshdesk e Freshservice, com foco em abertura, atendimento, acompanhamento, SLA e histórico de chamados internos.

### Documentação

Este documento é a especificação funcional do produto. Os detalhes técnicos vivem em arquivos separados, cada assunto com uma única fonte de verdade:

| Documento | Conteúdo |
| --- | --- |
| `README.md` (este arquivo) | escopo, perfis, regras de negócio, entidades, telas, roadmap |
| [docs/atendimento-e-aparencia.md](docs/atendimento-e-aparencia.md) | enviar e fechar, interface de atendimento, temas e roteiro de validação |
| [docs/arquitetura.md](docs/arquitetura.md) | stack, estrutura do repositório, decisões de arquitetura e justificativas |
| [docs/integracao-email.md](docs/integracao-email.md) | ingestão de chamados por e-mail e caixa de SPAM (versão 2.0) |
| [CLAUDE.md](CLAUDE.md) | instruções para agentes de IA que trabalham no repositório |

### Como executar

Pré-requisitos: **.NET 10 SDK**, **Node 24 LTS** (`frontend/.nvmrc`; a faixa aceita é `^20.19` ou `>=22.12`) e **Docker**.

```bash
docker compose up -d                 # PostgreSQL + API
cd frontend && npm ci && npm run dev # SPA
```

| Onde | Endereço |
| --- | --- |
| SPA | http://localhost:5173 |
| API | http://localhost:8080 |
| Swagger | http://localhost:8080/swagger |
| Health check | http://localhost:8080/health |

A API aplica as migrations e popula o seed na subida, apenas em ambiente de desenvolvimento. Os usuários de exemplo criados pelo seed e a lista completa de comandos — testes, migrations, typecheck — estão em [CLAUDE.md](CLAUDE.md), seção "Comandos".

### Estado do código

**O MVP da versão 1 está completo.** Os nove critérios de sucesso da seção 19 estão atendidos: cadastro e abertura de chamado, atendimento com comentários e mudança de status, atribuição de técnico, visão completa para o gestor, autorização por perfil, histórico de alterações, dashboard, execução local por Docker Compose e este README explicando como rodar.

Fora do escopo da versão 1, conforme o roadmap da seção 18: notificações e as telas de administração de categorias e de usuários ficam para a 1.1; a ingestão de e-mail e a caixa de SPAM ficam para a 2.0. Anexos estavam na 1.1 e foram antecipados — chamado de suporte sem print de tela obriga a conversa a acontecer por e-mail, fora do sistema. Perfis de técnico e gestor são definidos pelo seed ou direto no banco.

---

## 2. Objetivo do sistema

O objetivo do OpsDesk é permitir que usuários internos abram chamados de suporte para a equipe de TI, enquanto técnicos e gestores conseguem acompanhar, priorizar, atender e analisar esses chamados.

O sistema deve permitir:

* abertura de chamados;
* acompanhamento por status;
* classificação por categoria e prioridade;
* atribuição de responsável;
* comentários entre usuário e equipe técnica;
* comentários internos da equipe;
* histórico de alterações;
* controle básico de SLA;
* dashboard gerencial;
* base preparada para futuras integrações, como abertura por e-mail e importação de chamados externos.

---

## 3. Escopo da versão 1

A versão 1 será um MVP funcional, com foco nas principais regras de negócio de um sistema de chamados.

### Dentro do escopo

* autenticação de usuários;
* autorização por perfil;
* cadastro básico de usuários;
* abertura de chamados;
* listagem de chamados;
* detalhe do chamado;
* alteração de status;
* atribuição de técnico responsável;
* comentários;
* comentários internos;
* histórico de alterações;
* categorias;
* prioridades;
* SLA básico;
* dashboard inicial.

### Fora do escopo da versão 1

* abertura automática por e-mail;
* caixa de SPAM de e-mails recebidos;
* integração com Freshdesk, Freshservice ou GLPI;
* importação via CSV ou JSON;
* base de conhecimento;
* inventário de ativos;
* chat em tempo real;
* notificações por e-mail;
* integração com Microsoft Entra ID;
* aplicativo mobile;
* fluxo avançado de aprovação.

Essas funcionalidades poderão ser adicionadas em versões futuras.

---

## 4. Perfis de acesso

O sistema terá inicialmente três perfis principais.

---

### 4.1 Usuário

Representa o colaborador que precisa de suporte.

Permissões:

* abrir chamados;
* visualizar seus próprios chamados;
* comentar em seus chamados;
* visualizar respostas dos técnicos;
* acompanhar status;
* anexar arquivos ao chamado e aos comentários;
* solicitar reabertura de chamado futuramente.

Não pode:

* visualizar chamados de outros usuários;
* alterar prioridade após abertura, dependendo da regra definida;
* atribuir técnico;
* alterar SLA;
* acessar dashboard gerencial;
* visualizar comentários internos.

---

### 4.2 Técnico

Representa o profissional responsável por atender chamados.

Permissões:

* visualizar chamados disponíveis para atendimento;
* visualizar chamados atribuídos a ele;
* assumir chamados;
* encaminhar chamado para outro técnico;
* abrir chamado em nome de um usuário;
* alterar status;
* responder chamados;
* adicionar comentários internos;
* alterar categoria;
* alterar prioridade, se permitido;
* marcar chamado como resolvido;
* visualizar histórico do chamado.

Não pode:

* gerenciar usuários;
* alterar regras globais de SLA;
* visualizar dashboards estratégicos completos, dependendo da regra definida.

---

### 4.3 Gestor

Representa o responsável pela operação da equipe de suporte.

Permissões:

* visualizar todos os chamados;
* atribuir chamados a técnicos;
* alterar status, prioridade e categoria;
* gerenciar categorias;
* visualizar dashboard;
* acompanhar chamados vencidos;
* visualizar indicadores por técnico, categoria e prioridade;
* visualizar histórico completo;
* gerenciar usuários futuramente;
* configurar SLA futuramente.

---

## 5. Fluxo de status do chamado

O chamado terá um ciclo de vida simples na versão 1.

### Status disponíveis

* **Aberto**
* **Em triagem**
* **Em atendimento**
* **Aguardando usuário**
* **Resolvido**
* **Fechado**
* **Cancelado**

---

### 5.1 Aberto

Status inicial de todo chamado criado pelo usuário.

Exemplo:

> Usuário abriu um chamado informando que não consegue acessar a VPN.

---

### 5.2 Em triagem

Status usado quando a equipe de TI está analisando o chamado antes de iniciar o atendimento.

Exemplo:

> O gestor ou técnico está verificando categoria, prioridade e responsável.

---

### 5.3 Em atendimento

Status usado quando um técnico assumiu o chamado e está trabalhando nele.

Exemplo:

> Técnico está analisando logs, acessos, permissões ou ambiente do usuário.

---

### 5.4 Aguardando usuário

Status usado quando o atendimento depende de uma resposta, evidência ou ação do solicitante.

Exemplo:

> Técnico pediu um print do erro ou pediu para o usuário testar novamente.

---

### 5.5 Resolvido

Status usado quando o técnico aplicou uma solução.

Exemplo:

> Técnico corrigiu o acesso VPN e marcou o chamado como resolvido.

---

### 5.6 Fechado

Status final do chamado.

Pode acontecer quando:

* usuário confirma a resolução;
* gestor fecha manualmente;
* sistema fecha automaticamente após determinado período, em versão futura.

---

### 5.7 Cancelado

Status usado quando o chamado não deve mais ser atendido.

Exemplo:

> Usuário abriu chamado duplicado ou informou que não precisa mais do atendimento.

---

### 5.8 Transições permitidas

O ciclo de vida não é um campo livre: cada status tem um conjunto fechado de destinos, e a regra vive em um lugar só (`TicketStatusMachine`, no domínio).

| De | Para |
| --- | --- |
| Aberto | Em triagem, Em atendimento, Fechado, Cancelado |
| Em triagem | Em atendimento, Aguardando usuário, Fechado, Cancelado |
| Em atendimento | Em triagem, Aguardando usuário, Resolvido, Fechado, Cancelado |
| Aguardando usuário | Em atendimento, Resolvido, Fechado, Cancelado |
| Resolvido | Fechado, Em atendimento |
| Fechado | — |
| Cancelado | — |

Observações:

* **Aberto não vai direto para Resolvido.** Resolver sem passar por atendimento deixaria o chamado sem responsável e sem marco de resposta, e o indicador de SLA não teria o que medir.
* **Fechamento direto pela equipe:** técnicos e gestores podem fechar qualquer chamado não terminal ao qual tenham acesso, inclusive com “Enviar e fechar”. O fechamento preenche `ResolvedAt` se ainda não houver resolução e encerra eventual pausa de SLA. O solicitante só fecha chamados já resolvidos.
* **Resolvido volta para Em atendimento** quando a solução não resolveu. É o caminho de retrabalho antes do fechamento.
* **Fechado e Cancelado são terminais na versão 1.** A reabertura de chamado fechado está no roadmap e abrirá essa transição quando existir.

Quem pode mover o chamado:

| Perfil | Transições |
| --- | --- |
| Gestor | qualquer uma da tabela acima |
| Técnico | qualquer uma da tabela acima |
| Usuário | cancelar o próprio chamado; fechar um chamado já resolvido, confirmando a solução |

O perfil autoriza a *transição*; o filtro de visibilidade é que decide *em qual chamado*. São verificações independentes, e as duas acontecem.

---

## 6. Prioridades

O sistema terá quatro prioridades iniciais.

### Baixa

Problema sem impacto relevante na operação.

Exemplos:

* dúvida simples;
* solicitação de orientação;
* ajuste sem urgência.

### Média

Problema que impacta parcialmente o usuário, mas possui contorno.

Exemplos:

* dificuldade em acessar uma ferramenta secundária;
* erro que não impede totalmente o trabalho.

### Alta

Problema que impacta diretamente o trabalho do usuário ou de uma área.

Exemplos:

* usuário sem acesso a sistema essencial;
* falha recorrente em aplicação importante.

### Crítica

Problema com impacto amplo ou impeditivo.

Exemplos:

* sistema indisponível;
* vários usuários impactados;
* falha em ambiente produtivo;
* incidente de segurança.

---

## 7. Categorias iniciais

As categorias iniciais serão:

* Acesso;
* Hardware;
* Software;
* Rede;
* VPN;
* E-mail;
* Impressora;
* Sistemas internos;
* Banco de dados;
* Servidores;
* Cloud/Azure;
* Outros.

Cada chamado deverá estar associado a uma categoria.

Na versão 1, a categoria poderá ser selecionada pelo usuário na abertura do chamado.

Em versões futuras, a categoria poderá ser sugerida automaticamente com base no texto do chamado.

---

## 8. Regras de SLA

A versão 1 terá um SLA simples baseado na prioridade do chamado.

### SLA inicial sugerido

| Prioridade | Prazo de resposta | Prazo de resolução |
| ---------- | ----------------: | -----------------: |
| Baixa      |    24 horas úteis |       5 dias úteis |
| Média      |     8 horas úteis |       3 dias úteis |
| Alta       |     4 horas úteis |         1 dia útil |
| Crítica    |       1 hora útil |      4 horas úteis |

O prazo é calculado uma única vez, na criação do chamado, e gravado em `SlaResponseDueAt` e `SlaResolutionDueAt`. Não existe processo em segundo plano recalculando prazos: um chamado está vencido quando `now() > prazo` e o chamado ainda não atingiu o marco correspondente.

Os prazos são sempre armazenados em **horas úteis**, inclusive os expressos em dias na tabela acima. Um dia útil é o expediente inteiro da seção 8.1, ou seja dez horas — 08:00 às 18:00. A conversão é única e fica no seed das políticas:

| Prioridade | Resposta | Resolução |
| ---------- | -------: | --------: |
| Baixa      |     24 h |      50 h |
| Média      |      8 h |      30 h |
| Alta       |      4 h |      10 h |
| Crítica    |      1 h |       4 h |

---

### 8.1 Definição de horas úteis

Horas úteis são contadas dentro do expediente configurado, ignorando fins de semana e feriados.

Expediente padrão da versão 1:

* segunda a sexta-feira;
* das 08:00 às 18:00;
* fuso horário `America/Sao_Paulo`;
* feriados nacionais cadastrados em tabela.

O expediente é fixo na versão 1 e não é configurável pela interface.

---

### 8.2 Pausa do SLA

O relógio do SLA de **resolução** é pausado enquanto o chamado está no status **Aguardando usuário**.

Regras:

* ao entrar em "Aguardando usuário", o instante da pausa é registrado;
* ao sair de "Aguardando usuário", o tempo pausado é acumulado no chamado e `SlaResolutionDueAt` é recalculado somando o período pausado convertido em horas úteis;
* o SLA de **resposta** não é pausado, porque ele mede o tempo até o primeiro retorno da equipe;
* os status "Resolvido", "Fechado" e "Cancelado" encerram a contagem definitivamente.

Justificativa: sem essa pausa, o indicador de chamados vencidos passa a medir a demora do solicitante, e não o desempenho da equipe de TI.

---

### 8.3 Marcos que encerram cada prazo

* **SLA de resposta**: encerrado no primeiro comentário público de um técnico ou gestor no chamado. O instante é gravado em `FirstRespondedAt`.
* **SLA de resolução**: encerrado quando o chamado entra em "Resolvido". O instante é gravado em `ResolvedAt`. No fechamento direto pela equipe, a resolução é registrada junto com o fechamento; se já existia uma data de resolução, ela é preservada.

Chamados cancelados não entram nos indicadores de SLA.

---

## 9. Regras de criação de chamado

Para abrir um chamado, o usuário deverá informar:

* título;
* descrição;
* categoria;
* prioridade.

Campos obrigatórios:

* título;
* descrição;
* categoria;
* prioridade.

Campos automáticos:

* código do chamado;
* status inicial;
* data de criação;
* prazo de SLA;
* origem do chamado.

### 9.1 Solicitante

Por padrão, o solicitante é quem está autenticado, e o corpo do pedido não precisa informá-lo.

**Técnico e gestor podem abrir chamado em nome de outro usuário.** É o atendimento por telefone ou presencial: quem registra é a equipe, mas o problema é de quem pediu, e o chamado precisa aparecer na lista dessa pessoa para que as respostas cheguem a ela. O solicitante recebe 403 se tentar — abrir chamado no nome de um colega não é atribuição dele.

O campo `requesterId` aceita apenas usuário ativo; inexistente ou inativo é recusado com 400. Informar o próprio identificador é abertura comum, não abertura em nome de terceiro.

Quem registrou o chamado fica no `TicketHistory`, na entrada de criação. O campo solicitante diz **de quem é o problema**; o histórico diz **quem digitou**. São coisas diferentes e as duas ficam guardadas.

O chamado nasce sem responsável, como qualquer outro: registrar em nome de alguém não é assumir o atendimento.

Na versão 1, a origem será sempre:

* Portal.

Em versões futuras, poderá ser:

* Portal;
* E-mail;
* CSV;
* Freshdesk;
* Freshservice;
* GLPI;
* API externa.

---

## 10. Regras de atribuição

Um chamado pode ou não possuir técnico responsável.

Ao ser criado, o chamado ficará sem responsável.

Um chamado poderá ser atribuído de quatro formas:

1. técnico assume manualmente;
2. técnico encaminha para outro técnico;
3. gestor atribui a um técnico;
4. regra automática, em versão futura.

Encaminhar não é privilégio de gestão: quem recebeu o chamado errado precisa poder passá-lo adiante sem depender do gestor. O que limita o técnico não é a regra de atribuição e sim o filtro de visibilidade — ele só alcança chamado sem responsável ou atribuído a ele, e o chamado de outra pessoa simplesmente não existe para ele (404).

Consequência direta disso: ao encaminhar para um colega, o técnico perde o chamado de vista no mesmo instante. A API responde **204** nesse caso, em vez de 200 com o chamado — devolver o corpo exigiria reler o chamado ignorando o filtro de visibilidade. A interface leva a pessoa de volta para a lista.

Quando um técnico assumir o chamado:

* o campo responsável será atualizado;
* o status poderá mudar para “Em atendimento”;
* um registro será adicionado ao histórico.

---

## 11. Comentários

O sistema terá dois tipos de comentários.

### Comentário público

Visível para:

* usuário solicitante;
* técnico responsável;
* gestor.

Usado para comunicação normal entre usuário e equipe de TI.

Exemplo:

> Olá, poderia enviar um print do erro apresentado?

---

### Comentário interno

Visível apenas para:

* técnicos;
* gestores.

Usado para observações internas da equipe.

Exemplo:

> Verificar se esse usuário está no grupo correto do AD antes de responder.

O usuário solicitante não poderá visualizar comentários internos.

---

## 11.1 Anexos

Chamado e comentário aceitam arquivo: print da tela, log, planilha, PDF — o que hoje sairia por e-mail.

Regras:

* até **10 MB por arquivo**, e a lista de tipos aceitos é fechada: imagem, PDF, texto, CSV, zip, documento e planilha do Office, e mensagem de e-mail. O que não está na lista é recusado, e não aceito por omissão;
* o conteúdo **não** fica no banco. Vai para o armazenamento configurado — disco com volume no ambiente local —, e o banco guarda metadados e a chave;
* **o anexo herda a visibilidade do comentário.** Arquivo enviado numa nota interna é tão restrito quanto o texto dela: não aparece na listagem do solicitante e o download direto pelo identificador responde 404. A regra da seção 11 vale para o arquivo, não só para o texto;
* anexo da abertura é público, como a descrição;
* anexo de chamado que o usuário não pode ver não é baixável, pela mesma verificação que esconde o chamado.

O envio acontece em dois tempos, e a ordem é o que sustenta a regra acima:

1. o arquivo sobe assim que é escolhido e nasce **pendente** — sem chamado, visível apenas para quem o enviou;
2. a abertura do chamado, ou o envio do comentário, é o que o vincula e define a quem ele passa a ser visível.

Sem isso haveria uma janela entre "o arquivo já está no servidor" e "o comentário interno foi enviado" em que o print estaria legível para o solicitante. Anexo que nunca chega a ser vinculado continua pendente e invisível para todos os outros.

Na interface, o arquivo entra por botão, arrastar-e-soltar ou **Ctrl+V** — colar um print recém-tirado é o caminho mais comum e não exige salvar arquivo nenhum.

---

## 12. Histórico do chamado

Toda alteração importante deverá gerar histórico.

Eventos que devem ser registrados:

* criação do chamado;
* alteração de status;
* alteração de prioridade;
* alteração de categoria;
* atribuição de técnico;
* remoção de técnico;
* adição de comentário;
* resolução do chamado;
* fechamento do chamado;
* cancelamento do chamado.

Cada item de histórico deve conter:

* chamado;
* usuário responsável pela ação;
* tipo da ação;
* valor anterior;
* valor novo;
* data e hora.

Exemplo:

> Status alterado de “Aberto” para “Em atendimento” por Lucas em 22/06/2026 às 14:32.

---

## 13. Telas da versão 1

### 13.1 Login

Tela para autenticação do usuário.

Campos:

* e-mail;
* senha.

Ações:

* entrar;
* acessar tela de cadastro, se permitido.

---

### 13.2 Cadastro de usuário

Tela para criação de usuário.

Campos:

* nome;
* e-mail;
* senha;
* confirmação de senha.

Na versão 1, todo usuário cadastrado pelo portal será criado com perfil “Usuário”.

Perfis técnicos e gestores poderão ser ajustados diretamente no banco, painel administrativo ou seed inicial.

---

### 13.3 Home do usuário

Tela inicial para usuários comuns.

Deve exibir:

* botão para abrir chamado;
* lista dos meus chamados;
* filtros por status;
* filtros por prioridade;
* data de criação;
* status atual.

---

### 13.4 Abertura de chamado

Tela para criação de um novo chamado.

Campos:

* título;
* descrição;
* categoria;
* prioridade;
* solicitante — só para técnico e gestor, e só para abrir em nome de outra pessoa. O padrão é “eu mesmo”;
* anexos — opcional.

Ações:

* criar chamado;
* cancelar.

Após criar, o usuário deve ser redirecionado para a tela de detalhe do chamado.

---

### 13.5 Detalhe do chamado

Tela central do sistema.

Deve exibir:

* código do chamado;
* título;
* descrição;
* status;
* prioridade;
* categoria;
* solicitante;
* técnico responsável;
* data de criação;
* prazo de SLA;
* comentários;
* anexos, junto da mensagem a que pertencem;
* histórico.

Ações para usuário:

* adicionar comentário;
* visualizar andamento.

Ações para técnico:

* assumir chamado;
* encaminhar para outro técnico;
* alterar status;
* adicionar comentário público;
* adicionar comentário interno;
* marcar como resolvido.

Ações para gestor:

* atribuir técnico;
* alterar prioridade;
* alterar categoria;
* alterar status;
* visualizar histórico completo.

---

### 13.6 Painel do técnico

Tela para técnicos acompanharem chamados.

Deve exibir:

* chamados sem responsável;
* chamados atribuídos ao técnico logado;
* chamados em atendimento;
* chamados aguardando usuário;
* chamados próximos do vencimento;
* chamados vencidos.

Filtros:

* status;
* prioridade;
* categoria;
* responsável;
* data de criação.

---

### 13.7 Dashboard do gestor

Tela com visão geral da operação.

Indicadores iniciais:

* total de chamados abertos;
* total de chamados em atendimento;
* total de chamados aguardando usuário;
* total de chamados resolvidos;
* total de chamados vencidos;
* chamados por prioridade;
* chamados por categoria;
* chamados por status;
* chamados por técnico.

---

## 14. Entidades principais

### User

Representa um usuário do sistema.

Campos:

* Id;
* Name;
* Email;
* PasswordHash;
* Role;
* IsActive;
* CreatedAt;
* UpdatedAt.

`PasswordHash` é anulável. A partir da versão 2.0, solicitantes identificados apenas por e-mail são criados automaticamente sem senha e não conseguem autenticar até definirem uma. Ver [docs/integracao-email.md](docs/integracao-email.md).

---

### Ticket

Representa um chamado.

Campos:

* Id;
* Code;
* Title;
* Description;
* Status;
* Priority;
* RequesterId;
* AssignedTechnicianId;
* CategoryId;
* Source;
* ExternalId;
* SlaResponseDueAt;
* SlaResolutionDueAt;
* FirstRespondedAt;
* SlaPausedAt;
* SlaPausedBusinessMinutes;
* CreatedAt;
* UpdatedAt;
* ResolvedAt;
* ClosedAt.

Observações:

* `Code` é gerado por *sequence* do PostgreSQL, no formato `OPS-000123`;
* `Source` identifica a origem do chamado e vale `Portal` na versão 1;
* `ExternalId` guarda o identificador da origem externa, como o `Message-ID` do e-mail que gerou o chamado;
* `FirstRespondedAt` fecha o SLA de resposta;
* `SlaPausedAt` e `SlaPausedBusinessMinutes` sustentam a pausa descrita na seção 8.2.

---

### Category

Representa uma categoria de chamado.

Campos:

* Id;
* Name;
* Description;
* IsActive;
* CreatedAt;
* UpdatedAt.

---

### TicketComment

Representa um comentário em um chamado.

Campos:

* Id;
* TicketId;
* AuthorId;
* Content;
* IsInternal;
* CreatedAt.

---

### TicketHistory

Representa um evento de histórico do chamado.

Campos:

* Id;
* TicketId;
* ChangedById;
* Action;
* PreviousValue;
* NewValue;
* CreatedAt.

---

### SlaPolicy

Representa a política de SLA por prioridade.

Campos:

* Id;
* Priority;
* ResponseHours;
* ResolutionHours;
* IsActive;
* CreatedAt;
* UpdatedAt.

---

### Holiday

Feriado excluído da contagem de horas úteis. A data é local ao fuso do expediente, por isso é uma data e não um instante.

Campos:

* Id;
* Date;
* Name.

O seed popula os feriados nacionais do ano corrente e dos dois seguintes, calculando as datas móveis a partir da Páscoa. Carnaval e Corpus Christi entram na lista: são ponto facultativo federal, mas a equipe de suporte não está de plantão, e SLA contando hora útil em dia sem ninguém atendendo é indicador mentiroso.

---

### RefreshToken

Refresh token revogável, ligado a um usuário. Só o hash é armazenado; o valor em claro vive no cookie `httpOnly` do navegador e é rotacionado a cada uso.

Campos:

* Id;
* UserId;
* TokenHash;
* ExpiresAt;
* CreatedAt;
* RevokedAt;
* ReplacedByTokenHash.

`ReplacedByTokenHash` mantém a cadeia de rotação auditável: dá para reconstruir qual token substituiu qual.

---

### Entidades da versão 2.0

A ingestão de e-mail acrescenta `EmailAccount`, `InboundEmail` e `SpamRule`. Elas estão especificadas em [docs/integracao-email.md](docs/integracao-email.md) e não fazem parte da versão 1.

---

## 15. Regras de autorização

### Usuário

Pode acessar:

* seus próprios chamados;
* criação de chamados;
* comentários públicos dos seus chamados.

Não pode acessar:

* chamados de outros usuários;
* comentários internos;
* dashboard de gestor;
* painel técnico.

---

### Técnico

Pode acessar:

* painel técnico;
* chamados sem responsável;
* chamados atribuídos a ele;
* comentários internos;
* histórico dos chamados que pode atender.

Não pode acessar:

* configurações globais do sistema;
* gerenciamento completo de usuários, na versão 1.

---

### Gestor

Pode acessar:

* todos os chamados;
* dashboard;
* painel técnico;
* atribuição de responsáveis;
* alteração de prioridade;
* alteração de categoria;
* visão geral da operação.

---

## 16. Requisitos não funcionais

### Segurança

* senhas devem ser armazenadas com hash;
* autenticação via JWT;
* rotas protegidas por perfil;
* usuário comum não pode acessar dados de outros usuários;
* comentários internos não podem ser retornados para usuários comuns.

### Performance

* listagem de chamados deve ter paginação;
* filtros devem ser aplicados no backend;
* dashboard deve usar consultas agregadas.

### Auditoria

* alterações relevantes devem ser registradas no histórico;
* histórico não deve ser editável pelo usuário.

### Usabilidade

* status e prioridade devem ser visualmente claros;
* usuário deve conseguir abrir chamado com poucos campos;
* técnico deve conseguir identificar rapidamente chamados críticos e vencidos.

---

## 17. Tecnologias

A stack abaixo está definida para a versão 1. Justificativas, alternativas descartadas e estrutura de pastas estão em [docs/arquitetura.md](docs/arquitetura.md).

### Backend

* .NET 10 (LTS);
* ASP.NET Core Web API;
* Entity Framework Core + Npgsql;
* PostgreSQL;
* autenticação JWT com refresh token;
* hash de senha com `PasswordHasher<T>`, do ASP.NET Core Identity;
* FluentValidation;
* Swagger/OpenAPI;
* Serilog;
* xUnit, `WebApplicationFactory` e Testcontainers.

Descartados de forma deliberada:

* **MediatR** e **AutoMapper**, por terem passado a licença comercial e não resolverem um problema que este projeto tenha;
* **repositório genérico** sobre o EF Core, porque o `DbContext` já é Unit of Work e a camada extra só esconde a query.

### Frontend

* React com TypeScript;
* Vite;
* React Router;
* TanStack Query;
* React Hook Form e Zod;
* shadcn/ui com Tailwind CSS;
* TanStack Table, para as listagens com paginação e filtros aplicados no backend;
* Recharts, para o dashboard;
* Axios, com tipos gerados a partir do OpenAPI da API.

### Infraestrutura local

* Docker e Docker Compose;
* PostgreSQL em container;
* backend em container;
* Mailpit em container, a partir da versão 2.0, para testar a ingestão de e-mail;
* frontend em container futuramente.

### Infraestrutura futura

* Azure Container Apps ou App Service;
* Azure Database for PostgreSQL;
* Azure Blob Storage;
* Azure Front Door;
* Application Insights;
* GitHub Actions.

---

## 18. Roadmap inicial

### Versão 1.0 — MVP

* login;
* anexos em chamados e comentários (antecipado da 1.1);
* cadastro básico;
* perfis;
* abertura de chamado;
* listagem de chamados;
* detalhe do chamado;
* comentários;
* alteração de status;
* atribuição de técnico;
* categorias;
* prioridades;
* SLA básico;
* dashboard simples.

### Versão 1.1 — Melhorias operacionais

* paginação avançada;
* filtros melhores;
* tela de administração de categorias;
* tela de administração de usuários;
* seed inicial de usuários e categorias;
* melhoria visual do dashboard.

### Versão 1.2 — Notificações

* notificações internas;
* alertas de SLA;
* e-mail para mudança de status;
* e-mail para novo comentário.

### Versão 2.0 — Integrações

* abertura de chamado por e-mail, com o remetente virando solicitante;
* criação automática de solicitante a partir do endereço remetente;
* vínculo de respostas de e-mail ao chamado original, como comentário;
* caixa de SPAM, com classificação, revisão manual e promoção a chamado;
* importação CSV;
* importação JSON;
* integração com Freshdesk;
* integração com Freshservice;
* identificação de chamados externos por origem e ID externo.

Especificação completa da ingestão de e-mail e da caixa de SPAM: [docs/integracao-email.md](docs/integracao-email.md).

---

## 19. Critérios de sucesso da versão 1

A versão 1 será considerada concluída quando:

* um usuário conseguir se cadastrar e abrir chamado;
* um técnico conseguir visualizar e assumir chamado;
* um técnico conseguir responder e alterar status;
* um gestor conseguir visualizar todos os chamados;
* o sistema impedir acesso indevido por perfil;
* o chamado possuir histórico de alterações;
* o dashboard mostrar indicadores básicos;
* o projeto rodar localmente com Docker Compose;
* o README explicar como executar o sistema.
