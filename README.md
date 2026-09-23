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

Duas formas de subir, e elas servem a propósitos diferentes.

**Para desenvolver** — o SPA fora do compose, com recarga imediata a cada alteração:

```bash
docker compose up -d                 # PostgreSQL + API
cd frontend && npm ci && npm run dev # SPA em http://localhost:5173
```

**Para só usar o sistema** — tudo em contêiner, sem Node na máquina:

```bash
docker compose --profile web up -d --build   # banco, API e SPA em http://localhost:3000
```

No perfil `web` o SPA é compilado e servido por nginx, que também repassa `/api` para a API: tudo na mesma origem, sem CORS e com o cookie de refresh como cookie de primeira parte. É o desenho mais parecido com produção, onde um gateway fica na frente dos dois.

O que ele **não** faz é recarga automática: o bundle é estático e cada alteração de código exige `--build` de novo. Para mexer no frontend, `npm run dev` continua sendo o caminho.

| Onde | Endereço |
| --- | --- |
| SPA, `npm run dev` | http://localhost:5173 |
| SPA, perfil `web` | http://localhost:3000 |
| API | http://localhost:8080 |
| Swagger | http://localhost:8080/swagger (ou /swagger pela porta 3000) |
| Health check (liveness) | http://localhost:8080/health |
| Readiness | http://localhost:8080/ready |

As duas formas de servir o SPA usam portas diferentes de propósito. Compartilhar a porta parecia mais simples e é uma armadilha: com um `npm run dev` rodando, o Docker Desktop no Windows **não falha** ao publicar uma porta já ocupada — os dois ficam escutando, o dev server atende, e o contêiner parece no ar servindo conteúdo que não é o dele.

A API aplica as migrations e popula o seed na subida, apenas em ambiente de desenvolvimento. Os usuários de exemplo criados pelo seed e a lista completa de comandos — testes, migrations, typecheck — estão em [CLAUDE.md](CLAUDE.md), seção "Comandos".

**Fora de desenvolvimento, instalar é uma etapa explícita.** O mesmo executável da API aceita comandos que fazem a tarefa e terminam, sem abrir porta nenhuma:

```bash
# Schema, dados de referência e primeiro gestor, nesta ordem.
docker compose run --rm   -e OpsDesk__Bootstrap__Email=voce@empresa.com   -e OpsDesk__Bootstrap__Password='uma-senha-provisoria'   api --setup
```

| Comando | O que faz |
| --- | --- |
| `--migrate` | aplica as migrations pendentes |
| `--seed` | insere categorias, políticas de SLA e feriados (sem usuários de exemplo) |
| `--bootstrap-admin` | cria o primeiro gestor a partir de `OpsDesk:Bootstrap` |
| `--setup` | os três acima, na ordem |

Migration continua fora da subida da aplicação de propósito: ninguém deve descobrir uma alteração de schema lendo log de inicialização.

O gestor criado pelo bootstrap nasce com **troca de senha obrigatória** — a senha chegou por variável de ambiente, e variável de ambiente aparece em log de deploy, em `docker inspect` e no histórico do shell. Até a troca acontecer, a API recusa com 403 tudo fora de `/api/auth` e a interface prende a navegação na tela de troca. O bootstrap só age quando **não existe nenhum gestor**: a variável esquecida no orquestrador não ressuscita conta administrativa a cada deploy. Se o e-mail já pertencer a alguém, essa conta é promovida e mantém a senha que já tinha.

`/health` e `/ready` respondem perguntas diferentes. O primeiro diz se o processo está vivo e o banco alcançável — é o que decide **reiniciar** o contêiner. O segundo acrescenta schema na versão da aplicação e dados de referência presentes — é o que decide **mandar tráfego**, e quem esquecer o `--setup` é acusado ali, com o comando que falta no corpo da resposta.

O que ainda falta para uma instalação em branco ser confortável — renovação de feriados, exemplo de compose com segredos, backup dos dois volumes — está na seção 18, em "Primeira execução: instalar em branco".

### Estado do código

**O MVP da versão 1 está completo.** Os nove critérios de sucesso da seção 19 estão atendidos: cadastro e abertura de chamado, atendimento com comentários e mudança de status, atribuição de técnico, visão completa para o gestor, autorização por perfil, histórico de alterações, dashboard, execução local por Docker Compose e este README explicando como rodar.

Fora do escopo da versão 1, conforme o roadmap da seção 18: notificações e as telas de administração de categorias e de usuários ficam para a 1.1; a ingestão de e-mail e a caixa de SPAM ficam para a 2.0. Anexos estavam na 1.1 e foram antecipados — chamado de suporte sem print de tela obriga a conversa a acontecer por e-mail, fora do sistema. O primeiro gestor sai do comando de bootstrap; promover **os demais** a técnico ou gestor ainda é `UPDATE` no banco, até a tela de administração de usuários da 1.1.

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

* visualizar todos os chamados, atribuídos ou não;
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

### 4.4 Visibilidade dos chamados

A fronteira de visibilidade é entre **equipe e solicitante**, e não entre os três perfis:

| Perfil | Enxerga |
| --- | --- |
| Gestor | todos os chamados |
| Técnico | todos os chamados |
| Usuário | apenas os próprios, e neles nunca o que é interno |

Técnico já viu apenas os chamados sem responsável e os atribuíos a ele. A restrição custava mais do que protegia: quem encaminhava um chamado para um colega o perdia de vista no mesmo instante, ninguém conseguia reclassificar nem comentar no chamado que o colega havia assumido, e cobrir uma ausência exigia passar pelo gestor. E não protegia dado de ninguém — é a mesma equipe, com o mesmo acesso a comentário e anexo internos.

Ver tudo não é o mesmo que trabalhar sobre tudo: a fila pessoal continua disponível na listagem, agora como filtro ("Meus chamados") em vez de imposição do backend.

O que **não** mudou, e é o que importa: o solicitante continua enxergando só os próprios chamados, sem comentário interno e sem anexo interno. O filtro segue aplicado na query, antes da projeção — invariante 1 do [CLAUDE.md](CLAUDE.md).

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

### 6.1 Reclassificação: trocar prioridade e categoria

Prioridade e categoria de abertura são um palpite de quem abriu o chamado. Corrigir esse palpite é o trabalho da triagem, e **técnico e gestor podem trocar os dois** a qualquer momento enquanto o chamado não estiver encerrado.

O solicitante não reclassifica, nem o próprio chamado: prioridade definida por quem abre transformaria a fila de atendimento numa negociação com quem escreve o pedido.

**Trocar a prioridade recalcula os prazos de SLA**, e a conta recomeça da **abertura** do chamado, não do instante da reclassificação. Contar da reclassificação daria mais folga a um chamado promovido a crítico do que a um aberto como crítico, e promover viraria uma forma de ganhar prazo.

Dois marcos já cumpridos não são reescritos:

* o prazo de **resposta** só é recalculado enquanto não houve primeira resposta. Mexer depois transformaria retroativamente um atendimento pontual em atrasado, ou o contrário;
* o prazo de **resolução** só é recalculado enquanto o chamado não foi resolvido.

O tempo que o chamado passou em "Aguardando usuário" é reaplicado sobre o prazo novo: a pausa acumulada não se perde porque alguém trocou a prioridade depois.

Trocar a **categoria** não mexe em prazo nenhum — categoria roteia e agrupa relatório, não define compromisso de tempo.

Chamado **fechado ou cancelado não é reclassificado**: mudar a prioridade de um atendimento concluído alteraria indicador de coisa que já acabou.

As duas mudanças vão para o histórico, com valor anterior e novo.

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

Encaminhar não é privilégio de gestão: quem recebeu o chamado errado precisa poder passá-lo adiante sem depender do gestor. E, como a equipe enxerga a fila inteira (seção 4.4), encaminhar não tira o chamado da vista de quem encaminhou — dá para acompanhar o que foi passado adiante.

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

**Cotas.** O limite por arquivo não limita nada sozinho: sem teto de quantidade, mil envios de dez megabytes são dez gigabytes de um único usuário autenticado. Por isso há três limites:

| Limite | Valor | Resposta |
| --- | --- | --- |
| Tamanho por arquivo | 10 MB | 413 |
| Anexos pendentes por usuário | 20 | 429 |
| Anexos por chamado | 50 | 400 na abertura ou no comentário |
| Envios por minuto, por usuário | 30 | 429 |

A cota de pendentes é sobre o que está solto, não sobre o que já foi enviado: vincular libera a cota. O limite de envios por minuto é particionado por **usuário**, e não por IP — por IP, um escritório atrás de NAT dividiria uma cota só.

**Varredura de abandonados.** Anexo pendente há mais de 24 horas é recolhido do banco e do armazenamento por uma tarefa que roda a cada 6 horas (`AttachmentStorage:PendingRetentionHours` e `CleanUpIntervalHours`; intervalo zero desliga). Sem isso o arquivo que a pessoa anexou e desistiu de enviar fica para sempre — e como pendente é invisível para todos menos para quem enviou, ninguém descobre olhando a interface.

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
* alterar prioridade e categoria;
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

### 13.8 Troca de senha

Formulário com senha atual, nova senha e confirmação. Trocar a senha encerra as demais sessões do usuário e reabre a atual, para a pessoa não ser devolvida ao login logo depois de ter provado quem é.

Atende dois casos com a mesma tela. Na troca voluntária é uma tela como as outras. Na **troca obrigatória** — o primeiro acesso do gestor criado pelo bootstrap — ela fica fora do layout do aplicativo, sem menu, e a navegação não sai dali: não há para onde ir, porque a API recusa todo o resto. A única outra saída é sair da conta.

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
* MustChangePassword;
* CreatedAt;
* UpdatedAt.

`MustChangePassword` marca senha provisória. Hoje nasce de um lugar só: o bootstrap do primeiro gestor, cuja senha chega por variável de ambiente. Enquanto for verdadeiro, a API recusa toda rota fora de `/api/auth` com 403, e a troca pelo próprio usuário limpa a marca. É o que mantém a credencial de instalação valendo uma vez, e não para sempre.

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
* chamados atribuídos a ele, pelo filtro "Meus chamados";
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

### Senha provisória

Corta transversalmente os três perfis. Enquanto `User.MustChangePassword` for verdadeiro, a pessoa só pode acessar `/api/auth` — entrar, ver quem é, trocar a senha e sair. Qualquer outra rota responde 403, com um `type` próprio no `ProblemDetails` para a interface distinguir isto de falta de permissão: uma tem saída, a outra é porta fechada.

A regra é aplicada por middleware, e não por atributo em cada rota. O motivo é o mesmo da invariante 1 do [CLAUDE.md](CLAUDE.md): filtro que se declara rota a rota é filtro que a próxima rota esquece.

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

## 18. Roadmap

Cada item traz o que é, por que está na lista, e o que ainda precisa ser decidido antes de escrever código. Item sem decisão pendente é item que pode começar amanhã.

### Entregue na versão 1.0

* login, cadastro e perfis;
* abertura, listagem e detalhe de chamado;
* comentários públicos e internos, com "enviar e fechar";
* máquina de estados e histórico automático;
* atribuição, inclusive encaminhamento entre técnicos;
* abertura em nome de outro usuário, pela equipe;
* reclassificação de prioridade e categoria, com recálculo de SLA;
* SLA em horas úteis, com pausa e retomada;
* categorias, prioridades e dashboard;
* **anexos** em chamados e comentários, antecipados da 1.1;
* **instalação em branco**: comandos de migration, seed e bootstrap do primeiro gestor, com troca de senha obrigatória no primeiro acesso;
* **diagnóstico de instalação**: `/ready` separado do `/health`, e validação de configuração na subida.

### Primeira execução: instalar em branco

Até a 1.0 o projeto subia pronto para uso **em desenvolvimento**, e apenas nele. A API aplicava migration e seed na subida só quando o ambiente era `Development`; em qualquer outro, `ApplyDatabaseStartupTasksAsync` retornava cedo e nada acontecia. Quem clonasse e subisse em produção encontrava banco sem schema, zero categorias e políticas de SLA — abrir chamado respondia **500** —, zero feriados e zero usuários, sem caminho pela interface para o primeiro gestor existir. O ambiente de desenvolvimento escondia tudo isso, o que é justamente o que tornava o problema traiçoeiro: funcionava na máquina de quem escreveu.

**Entregue: instalação como etapa explícita de deploy.** O executável da API aceita `--migrate`, `--seed`, `--bootstrap-admin` e `--setup`, faz a tarefa e termina, sem abrir porta. Os comandos e o exemplo de uso estão na seção 1, em "Como executar". A decisão entre comando explícito e execução automática na subida ficou com o comando, pelo mesmo argumento que já valia para a migration: ninguém deve descobrir alteração de banco pelo log de inicialização. O preço é conhecido e aceito — quem esquecer de rodar sobe um sistema que parece saudável e recusa o primeiro chamado, e é o que o item de readiness abaixo passa a acusar.

**Entregue: o primeiro gestor, por variável de ambiente.** `OpsDesk__Bootstrap__Email` e `OpsDesk__Bootstrap__Password`, aplicadas uma única vez quando não existir nenhum gestor, com **troca de senha obrigatória no primeiro acesso** (`User.MustChangePassword`). As outras duas opções foram descartadas: o comando de linha dedicado exigiria um passo manual e acesso ao contêiner sem ganho real de segurança sobre isto, e "o primeiro usuário cadastrado vira gestor" é cómodo e perigoso — numa instância exposta antes de ser configurada, quem chegasse primeiro viraria administrador do sistema.

O custo do caminho escolhido é a senha existir em configuração, que é lugar que vaza: log de deploy, `docker inspect`, histórico do shell. A troca obrigatória é o que paga esse custo, e por isso ela não é cosmética — enquanto a marca estiver de pé, a API recusa com 403 tudo fora de `/api/auth`, por middleware e não por atributo de rota, para que endpoint novo nasça coberto.

**Falta: promover os demais.** O bootstrap resolve o primeiro gestor, e só ele. Colocar um segundo técnico na equipe continua sendo `UPDATE` no banco até a tela de administração de usuários, na 1.1.

**Feriados não se renovam sozinhos fora de desenvolvimento.** O seed cobre o ano corrente e os dois seguintes, e essa janela só "anda" porque roda a cada subida. Em produção, com seed executado uma vez, em algum ano o cálculo de horas úteis passa a contar feriado como expediente — em silêncio, e o sintoma aparece como prazo estranho, não como erro.

*A decidir:* renovar por tarefa agendada, reexecutar o seed em cada deploy, ou criar tela de administração de feriados (que também resolve feriado municipal, que nenhuma tabela nacional tem).

**Entregue: validação de configuração na subida.** `Jwt:SigningKey`, connection string e `AttachmentStorage` já falhavam cedo. Entraram os casos que subiam calados: raiz de anexos sem permissão de escrita (um arquivo de sonda na subida, em vez do `Permission denied` no primeiro upload) e fuso de expediente inválido — `BusinessHoursOptions` declarava `ValidateOnStart` sem validador registrado, ou seja, não validava nada; agora fuso inexistente, expediente invertido e lista de dias úteis vazia derrubam a subida com mensagem.

`Cors:AllowedOrigins` vazio ficou como **aviso**, e não como erro, contra o que este roadmap previa. O motivo apareceu ao implementar: no perfil `web` o nginx põe SPA e API na mesma origem, e nessa topologia a lista vazia é a configuração correta — derrubar a subida quebraria justamente o desenho mais próximo de produção. O que virou erro foi origem malformada: o middleware compara origem como texto, então `http://localhost:3000/` com barra no fim nunca casa, e o navegador reporta só um bloqueio genérico enquanto o log da API não registra nada.

**Entregue: readiness separado de liveness.** O `/health` responde se o processo está vivo e o banco alcançável, e é o que decide reiniciar o contêiner. O `/ready` acrescenta schema na versão da aplicação e dados de referência presentes, e é o que decide mandar tráfego; o corpo da resposta traz o comando que falta. Os testes de readiness ficam **fora** do `/health` de propósito: reiniciar o contêiner não aplica migration nem roda seed, e incluí-los daria um laço de reinício sem diagnóstico.

**Compose e segredos para quem não é desenvolvedor.** O compose atual traz chave JWT de desenvolvimento embutida, e está escrito nele que é só para isso. Falta um exemplo de subida séria: segredo injetado por `docker secret` ou cofre, senha de banco fora do arquivo, volumes nomeados para banco e anexos, e um `.env.example` do backend documentando cada variável obrigatória.

**Imagem do frontend com endereço de API definido no build.** `VITE_API_URL` é embutido em tempo de compilação, então a mesma imagem não serve a dois domínios. Hoje isso não incomoda porque o nginx do perfil `web` põe SPA e API na mesma origem; incomodaria numa topologia com domínios separados.

*A decidir, só quando essa topologia existir:* gerar a configuração em tempo de execução (um `config.js` escrito pelo entrypoint do contêiner) ou manter a regra de mesma origem.

**Dados de demonstração opcionais.** Quem sobe o sistema para avaliar cai numa lista vazia, e lista vazia não mostra SLA, dashboard nem conversa. Uma flag de seed de demonstração — alguns chamados em status diferentes, um com anexo, um vencido, um aguardando solicitante — resolve, e precisa ser separada do seed de referência para nunca escapar para produção.

**Backup e restauração de banco e anexos juntos.** São dois volumes, e o estado consistente exige os dois. Restaurar só o banco dá anexo que a interface lista e o download não encontra (404 com aviso no log); restaurar só os arquivos dá lixo em disco que nenhuma tela mostra. Falta documentar a ordem e um procedimento de restauração testado — backup que ninguém restaurou não é backup.

### Versão 1.1 — Melhorias operacionais

**Tela de administração de usuários.** Hoje não há como promover alguém a técnico ou gestor sem `UPDATE` no banco, e nem como desativar quem saiu da empresa. Enquanto isso, toda mudança de equipe depende de acesso ao PostgreSQL.

*A decidir:* quem pode promover (só gestor, presumivelmente); se desativar usuário com chamados abertos exige reatribuir antes; se o próprio gestor pode se rebaixar (provavelmente não — sistema sem gestor não tem volta pela interface).

**Tela de administração de categorias.** Criar, renomear e desativar. A entidade e o `IsActive` já existem; falta a tela e os endpoints.

*A decidir:* o que acontece com chamados de uma categoria desativada — a intenção é que continuem válidos e a categoria só saia dos seletores, que é o motivo de `IsActive` existir em vez de `DELETE`.

**Testes de frontend.** Não existe nenhum: são 333 testes no backend e zero no cliente, e o job de CI roda typecheck, lint e build. Não é caso de cobrir tudo; é caso de cobrir o que dói — marcação visual de comentário e anexo internos, filtros da lista sobrevivendo à URL, e as ações respeitando `allowedNextStatuses`.

*A decidir:* Vitest com Testing Library para componente, e se vale um teste de ponta a ponta com Playwright ou se isso fica para depois.

**Reabrir chamado fechado.** `Fechado` e `Cancelado` são terminais na versão 1. É o que o usuário pede na primeira semana: "o problema voltou".

*A decidir:* reabertura cria chamado novo vinculado ao antigo, ou reabre o mesmo? Reabrir o mesmo é mais simples e polui o indicador de tempo de resolução; chamado novo mantém os indicadores honestos e exige um campo de vínculo. Também falta decidir se o SLA reinicia.

**Paginação e filtros melhores.** Filtro por responsável e por período de abertura, busca que também olhe a descrição, e ordenação por última atualização. A busca atual cobre código e título, com `lower(coluna) LIKE`.

*A decidir:* busca em descrição com `LIKE` degrada com volume. Se entrar, provavelmente vale `tsvector` com índice GIN — o que é uma migration e uma decisão de arquitetura, não um ajuste de query.

**Melhoria visual do dashboard.** Recorte por período, comparação com o mês anterior, e cumprimento de SLA como percentual, que hoje não existe como indicador.

### Versão 1.2 — Notificações

É a maior lacuna funcional do produto: hoje ninguém descobre que um chamado mudou a não ser abrindo a tela. O técnico não sabe que foi atribuído, e o solicitante não sabe que a resposta chegou.

**E-mail de novo comentário público e de mudança de status.** Começar por aí.

*A decidir:* serviço de envio (Azure Communication Services, SendGrid, SMTP corporativo); fila ou envio no mesmo request — envio síncrono amarra a resposta da API à disponibilidade do provedor, e a fila pede infraestrutura; e como evitar tempestade de e-mail em chamado muito ativo (agrupar por janela de tempo).

**Atenção, e não é detalhe:** a invariante 2 vale em canal novo. Nota interna e anexo interno **nunca** entram no corpo de um e-mail, e o destinatário de cada mensagem precisa ser derivado da mesma regra de visibilidade que a API usa — não de uma lista montada à mão no serviço de notificação. Todo endpoint novo que exponha comentário pede teste do caso negativo; o mesmo vale para todo canal novo.

**Notificações internas na interface.** Sino com contador, lidas e não lidas.

**Alertas de SLA.** Aviso antes do vencimento, não depois. Depende de tarefa agendada, então vem depois do canal de e-mail existir.

*A decidir:* quem recebe — responsável, gestor, ou os dois; e com quanta antecedência, em horas úteis.

### Versão 2.0 — Integrações

Ingestão de chamado por e-mail, caixa de SPAM, importação CSV e JSON, e integração com Freshdesk e Freshservice. Especificação completa da ingestão de e-mail e da caixa de SPAM: [docs/integracao-email.md](docs/integracao-email.md).

O desenho de anexo pendente e vinculado já foi feito pensando nisso: anexo de e-mail entra pelo mesmo caminho, vinculado ao comentário que a mensagem virar.

### Infraestrutura, quando sair do ambiente local

**Anexos em Azure Blob Storage.** O armazenamento em disco atual **não sobrevive a mais de uma réplica**: cada réplica tem seu próprio sistema de arquivos, e o anexo enviado numa dá 404 na outra. O sistema de arquivos de contêiner também é efêmero — um deploy leva os anexos embora. Ou seja, isto deixa de ser melhoria e passa a ser pré-requisito no momento em que a API escalar horizontalmente.

O caminho está preparado: é uma implementação nova de `IAttachmentStorage` (decisão 4.15 de [docs/arquitetura.md](docs/arquitetura.md)), sem tocar em domínio, serviço, schema ou filtro de visibilidade. A chave de armazenamento já é um caminho hierárquico, que é o que o Blob espera.

*Decisões e cuidados:*

* **Não guardar anexo no banco.** Binário em coluna infla backup e restore, prende conexão do pool durante o streaming, e custa por volta de uma ordem de grandeza mais por GB que blob — contado de novo no backup e na réplica.
* **Managed Identity**, nunca connection string com chave de conta: chave em configuração é credencial permanente que vaza em log e em dump de variável de ambiente.
* **Como os bytes chegam ao navegador.** Hoje passam pela API, que aplica o filtro de visibilidade antes de abrir o stream. A alternativa é devolver URL SAS e deixar o navegador buscar direto — mas **SAS é portador**: quem tem o link acessa sem autenticação até expirar. Se adotar: gerar depois da verificação de visibilidade, no mesmo endpoint de download e nunca junto da listagem, com *user delegation SAS* e expiração de minutos. A recomendação é continuar transmitindo pela API e migrar quando o egresso justificar.
* **Azure Files** montado como volume é a saída sem mexer em código, com latência pior e custo maior. Serve para adiar, não para resolver.
* **Defender for Storage** com varredura de malware, *soft delete* de 7 a 30 dias, e regra de ciclo de vida para camada fria. O sistema aceita zip e documento do Office de qualquer pessoa da empresa: o anexo é o vetor de entrada mais óbvio que existe.
* A varredura de pendentes roda no processo da API. Com várias réplicas, todas varrem — é idempotente e o lote é pequeno, mas se incomodar, `CleanUpIntervalHours = 0` desliga e a limpeza migra para um job com dono único.

**Forwarded headers.** O rate limiting particiona por IP de origem nas rotas anónimas, lido de `RemoteIpAddress`. Atrás de Front Door ou Application Gateway, todo pedido chega com o IP do proxy e a partição vira uma só. Antes de pôr um proxy na frente, configurar `UseForwardedHeaders` — e só então confiar no cabeçalho, porque confiar nele sem o middleware deixa o cliente escolher a própria partição.

**Migration como etapa de deploy.** A API só aplica migration automaticamente em Development. Em qualquer outro ambiente o schema sobe como passo explícito, antes da aplicação nova subir — junto com o seed de dados de referência e o bootstrap do primeiro gestor, descritos em "Primeira execução" acima.

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
