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
| [docs/instalacao.md](docs/instalacao.md) | instalação em produção: HTTPS, atualização, backup, Azure Blob, réplicas |
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
# Schema, dados de referência e primeiro administrador, nesta ordem.
docker compose run --rm   -e OpsDesk__Bootstrap__Email=voce@empresa.com   -e OpsDesk__Bootstrap__Password='uma-senha-provisoria'   api --setup
```

| Comando | O que faz |
| --- | --- |
| `--migrate` | aplica as migrations pendentes |
| `--seed` | insere categorias (só em banco sem nenhuma), políticas de SLA e feriados (sem usuários de exemplo) |
| `--bootstrap-admin` | cria o primeiro administrador a partir de `OpsDesk:Bootstrap` |
| `--setup` | os três acima, na ordem |
| `--seed-demo` | dados de demonstração: cinco pessoas e oito chamados. Recusa banco que já tem chamado |

Migration continua fora da subida da aplicação de propósito: ninguém deve descobrir uma alteração de schema lendo log de inicialização.

O administrador criado pelo bootstrap nasce com **troca de senha obrigatória** — a senha chegou por variável de ambiente, e variável de ambiente aparece em log de deploy, em `docker inspect` e no histórico do shell. Até a troca acontecer, a API recusa com 403 tudo fora de `/api/auth` e a interface prende a navegação na tela de troca. O bootstrap só age quando **não existe nenhum administrador**: a variável esquecida no orquestrador não ressuscita conta administrativa a cada deploy. Se o e-mail já pertencer a alguém, essa conta é promovida e mantém a senha que já tinha.

`/health` e `/ready` respondem perguntas diferentes. O primeiro diz se o processo está vivo e o banco alcançável — é o que decide **reiniciar** o contêiner. O segundo acrescenta schema na versão da aplicação e dados de referência presentes — é o que decide **mandar tráfego**, e quem esquecer o `--setup` é acusado ali, com o comando que falta no corpo da resposta.

**Para produção**, o caminho é a pasta [deploy/](deploy): compose com Caddy na borda e HTTPS, segredos gerados num `.env` fora do repositório, e scripts de backup e restauração. O passo a passo está em [docs/instalacao.md](docs/instalacao.md).

### Estado do código

**O roadmap até a versão 2.0 está entregue.** Os nove critérios de sucesso da seção 19 estão atendidos desde a versão 1, e depois dela vieram:

* **1.1, operação:** administração de usuários, com cadastro, convite, redefinição de senha e trilha de auditoria; administração de categorias e de feriados; configurações gerais; reabertura de chamado; busca na descrição; filtros por responsável, período e categoria desativada; dashboard por período, com cumprimento de SLA e reabertura por técnico; testes de frontend e de ponta a ponta;
* **1.2, notificações:** e-mail ao solicitante pelo Microsoft Graph, configurado no portal, com fila de saída; notificações no portal; alertas de SLA;
* **2.0, integrações:** abertura de chamado por e-mail, caixa de SPAM, e importação de CSV, JSON e das exportações do Freshdesk e do Freshservice;
* **produção:** compose com HTTPS, backup e restauração testados, dados de demonstração, anexos no Azure Blob e forwarded headers.

O que continua aberto, com a decisão pendente de cada item, está no fim da seção 18.

---

## 2. Objetivo do sistema

O objetivo do OpsDesk é permitir que usuários internos abram chamados de suporte para a equipe de TI, enquanto técnicos e administradores conseguem acompanhar, priorizar, atender e analisar esses chamados.

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

Essas funcionalidades poderão ser adicionadas em versões futuras. Várias já foram — e-mail nos dois sentidos, caixa de SPAM, importação por arquivo e das exportações do Freshdesk e do Freshservice. A seção 18 conta o que entrou em cada versão.

---

## 4. Perfis de acesso

O sistema tem três perfis: Usuário, Técnico e Administrador. O perfil decide o que a pessoa pode fazer no portal.

O **cargo** (Tech Lead, Analista, Estagiário...) é outra coisa: só informativo, cadastrado pelo administrador (seção 13.16) e independente do perfil. Um analista pode ser Técnico e outro Administrador, sem que o catálogo precise de "Analista administrador".

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
* reabrir o próprio chamado resolvido ou fechado, respondendo a ele (seção 5.9);
* responder pelo e-mail de aviso, quando houver caixa monitorada (versão 2.0);
* definir a própria senha por link, em "Esqueci minha senha" ou pelo convite (seção 13.13).

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
* visualizar histórico do chamado;
* revisar os e-mails recebidos e a caixa de SPAM (seção 13.15).

Não pode:

* gerenciar usuários;
* alterar regras globais de SLA;
* visualizar dashboards estratégicos completos, dependendo da regra definida.

---

### 4.3 Administrador

Representa o responsável pela operação da equipe de suporte e pela administração do portal. Até a versão 2.0 este perfil se chamava **Gestor**; o valor gravado no banco e levado no token continua `Manager`.

Permissões:

* visualizar todos os chamados;
* atribuir chamados a técnicos;
* alterar status, prioridade e categoria;
* gerenciar categorias e cargos;
* visualizar dashboard;
* acompanhar chamados vencidos;
* visualizar indicadores por técnico, categoria e prioridade;
* visualizar histórico completo;
* gerenciar usuários: cadastrar, promover, rebaixar, desativar, reativar, excluir conta sem histórico e redefinir senha (seção 13.9);
* administrar feriados (seção 13.11) e as configurações gerais e de e-mail (seção 13.12);
* importar chamados de outra ferramenta (seção 13.14);
* excluir mensagem da caixa de SPAM;
* alterar a tabela de SLA, futuramente.

---

### 4.4 Visibilidade dos chamados

A fronteira de visibilidade é entre **equipe e solicitante**, e não entre os três perfis:

| Perfil | Enxerga |
| --- | --- |
| Administrador | todos os chamados |
| Técnico | todos os chamados |
| Usuário | apenas os próprios, e neles nunca o que é interno |

Técnico já viu apenas os chamados sem responsável e os atribuíos a ele. A restrição custava mais do que protegia: quem encaminhava um chamado para um colega o perdia de vista no mesmo instante, ninguém conseguia reclassificar nem comentar no chamado que o colega havia assumido, e cobrir uma ausência exigia passar pelo administrador. E não protegia dado de ninguém — é a mesma equipe, com o mesmo acesso a comentário e anexo internos.

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

> O administrador ou técnico está verificando categoria, prioridade e responsável.

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
* administrador fecha manualmente;
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
| Fechado | Em atendimento, dentro da janela de reabertura (seção 5.9) |
| Cancelado | — |

Observações:

* **Aberto não vai direto para Resolvido.** Resolver sem passar por atendimento deixaria o chamado sem responsável e sem marco de resposta, e o indicador de SLA não teria o que medir.
* **Fechamento direto pela equipe:** técnicos e administradores podem fechar qualquer chamado não terminal ao qual tenham acesso, inclusive com “Enviar e fechar”. O fechamento preenche `ResolvedAt` se ainda não houver resolução e encerra eventual pausa de SLA. O solicitante só fecha chamados já resolvidos.
* **Resolvido volta para Em atendimento** quando a solução não resolveu. É o caminho de retrabalho antes do fechamento.
* **Fechado volta para Em atendimento** por reabertura, dentro de sete dias do fechamento (seção 5.9). **Cancelado é terminal:** quem cancelou desistiu, e "o problema voltou" não se aplica.

Quem pode mover o chamado:

| Perfil | Transições |
| --- | --- |
| Administrador | qualquer uma da tabela acima |
| Técnico | qualquer uma da tabela acima |
| Usuário | cancelar o próprio chamado; fechar um chamado já resolvido, confirmando a solução. A reabertura do solicitante não passa por aqui: ela acontece quando ele responde (seção 5.9) |

O perfil autoriza a *transição*; o filtro de visibilidade é que decide *em qual chamado*. São verificações independentes, e as duas acontecem.

### 5.9 Reabertura

Reabrir devolve **o mesmo chamado** para "Em atendimento": mesmo código, mesma conversa, mesmo histórico. É o que o solicitante espera quando diz "o problema voltou", e é o que a versão 2.0 faz quando ele responde o e-mail de um chamado já encerrado.

Quem reabre, e como:

* **o solicitante reabre respondendo.** Um comentário dele em chamado "Resolvido" ou "Fechado" reabre o chamado junto com a resposta. Não há botão à parte: a resposta é o motivo da reabertura, e reabrir sem dizer por quê só acrescenta ruído à fila. "Enviar e fechar" em chamado resolvido é o gesto contrário, confirmar a solução, e não reabre nada. A interface avisa, acima do campo de resposta, que enviar vai reabrir;
* **a equipe reabre pela ação "Reabrir chamado"**, que é a mudança de status para "Em atendimento". Comentário da equipe não reabre: em chamado resolvido ele pode ser só um detalhe da solução, e em chamado fechado é recusado até alguém reabrir — assim a decisão fica explícita no histórico.

A janela:

* **chamado fechado reabre por até sete dias** contados do fechamento (`Tickets:ReopenWindowDays`). Depois disso, a resposta é recusada com a orientação de abrir um chamado novo. Sem a janela, um "obrigado!" enviado meses depois ressuscitaria um chamado antigo — e na 2.0 qualquer resposta a uma thread velha de e-mail faria o mesmo;
* **chamado resolvido reabre sempre**, porque ainda não foi dado como encerrado;
* **chamado cancelado nunca reabre.**

O SLA **continua de onde parou**. O tempo útil entre a resolução e a reabertura entra como pausa, como a espera pelo solicitante (seção 8.2): soma-se ao prazo de resolução e ao `SlaPausedBusinessMinutes`. Se faltavam três horas úteis quando o chamado foi resolvido, voltam três horas. Um chamado que já estava vencido ao ser resolvido volta vencido, porque aquele atraso foi real. As datas `ResolvedAt` e `ClosedAt` são limpas; as originais continuam no histórico, que registra a reabertura como evento próprio.

As duas alternativas foram descartadas. Recomeçar o prazo do zero daria prazo cheio a cada reabertura, e o compromisso assumido na abertura deixaria de valer. Não descontar nada — o comportamento anterior da volta de "Resolvido" — fazia o chamado voltar vencido pelo tempo em que a equipe não tinha como agir.

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

Prioridade e categoria de abertura são um palpite de quem abriu o chamado. Corrigir esse palpite é o trabalho da triagem, e **técnico e administrador podem trocar os dois** a qualquer momento enquanto o chamado não estiver encerrado.

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

Esta lista é o catálogo **inicial**: o seed a cria só quando não existe categoria nenhuma. Depois disso o catálogo é do administrador, pela tela da seção 13.10 — o seed não recria categoria renomeada nem reativa categoria desativada.

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
* feriados nacionais, renovados sozinhos, e os que o administrador cadastrar (seção 13.11).

O expediente é fixo na versão 1 e não é configurável pela interface.

---

### 8.2 Pausa do SLA

O relógio do SLA de **resolução** é pausado enquanto o chamado está no status **Aguardando usuário**.

Regras:

* ao entrar em "Aguardando usuário", o instante da pausa é registrado;
* ao sair de "Aguardando usuário", o tempo pausado é acumulado no chamado e `SlaResolutionDueAt` é recalculado somando o período pausado convertido em horas úteis;
* o SLA de **resposta** não é pausado, porque ele mede o tempo até o primeiro retorno da equipe;
* os status "Resolvido", "Fechado" e "Cancelado" encerram a contagem. Só o cancelamento é definitivo: na reabertura, o tempo parado desde a resolução também conta como pausa (seção 5.9).

Justificativa: sem essa pausa, o indicador de chamados vencidos passa a medir a demora do solicitante, e não o desempenho da equipe de TI.

---

### 8.3 Marcos que encerram cada prazo

* **SLA de resposta**: encerrado no primeiro comentário público de um técnico ou administrador no chamado. O instante é gravado em `FirstRespondedAt`.
* **SLA de resolução**: encerrado quando o chamado entra em "Resolvido". O instante é gravado em `ResolvedAt`. No fechamento direto pela equipe, a resolução é registrada junto com o fechamento; se já existia uma data de resolução, ela é preservada.

Chamados cancelados não entram nos indicadores de SLA.

**Chamados importados também não** — os de origem CSV, API externa, Freshdesk e Freshservice (seção 13.14). Eles foram atendidos antes de o prazo do OpsDesk existir: medi-los encheria o dashboard de "vencidos" que ninguém poderia ter evitado, e o alerta de SLA dispararia sobre chamado de anos atrás. Ficam fora do contador de vencidos, do filtro de vencidos, do dashboard por período e dos alertas, e o detalhe do chamado diz por quê. A regra mora num lugar só, `TicketSla.MeasuredSources`, com o espelho `slaMeasuredSources` na interface.

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

**Técnico e administrador podem abrir chamado em nome de outro usuário.** É o atendimento por telefone ou presencial: quem registra é a equipe, mas o problema é de quem pediu, e o chamado precisa aparecer na lista dessa pessoa para que as respostas cheguem a ela. O solicitante recebe 403 se tentar — abrir chamado no nome de um colega não é atribuição dele.

O campo `requesterId` aceita apenas usuário ativo; inexistente ou inativo é recusado com 400. Informar o próprio identificador é abertura comum, não abertura em nome de terceiro.

Quem registrou o chamado fica no `TicketHistory`, na entrada de criação. O campo solicitante diz **de quem é o problema**; o histórico diz **quem digitou**. São coisas diferentes e as duas ficam guardadas.

O chamado nasce sem responsável, como qualquer outro: registrar em nome de alguém não é assumir o atendimento.

A origem do chamado é uma destas:

| Origem | De onde vem |
| --- | --- |
| Portal | aberto na interface |
| E-mail | mensagem à caixa monitorada ([docs/integracao-email.md](docs/integracao-email.md)) |
| CSV | importação de planilha (seção 13.14) |
| API externa | importação de arquivo JSON (seção 13.14) |
| Freshdesk, Freshservice | importação da exportação dessas ferramentas (seção 13.14) |
| GLPI | reservada; ainda sem importação |

Só Portal e E-mail entram nos indicadores de SLA (seção 8.3).

---

## 10. Regras de atribuição

Um chamado pode ou não possuir técnico responsável.

Ao ser criado, o chamado ficará sem responsável.

Um chamado poderá ser atribuído de quatro formas:

1. técnico assume manualmente;
2. técnico encaminha para outro técnico;
3. administrador atribui a um técnico;
4. regra automática, em versão futura.

Encaminhar não é privilégio de gestão: quem recebeu o chamado errado precisa poder passá-lo adiante sem depender do administrador. E, como a equipe enxerga a fila inteira (seção 4.4), encaminhar não tira o chamado da vista de quem encaminhou — dá para acompanhar o que foi passado adiante.

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
* administrador.

Usado para comunicação normal entre usuário e equipe de TI.

Exemplo:

> Olá, poderia enviar um print do erro apresentado?

---

### Comentário interno

Visível apenas para:

* técnicos;
* administradores.

Usado para observações internas da equipe.

Exemplo:

> Verificar se esse usuário está no grupo correto do AD antes de responder.

O usuário solicitante não poderá visualizar comentários internos.

---

## 11.1 Anexos

Chamado e comentário aceitam arquivo: print da tela, log, planilha, PDF — o que hoje sairia por e-mail.

Regras:

* até **10 MB por arquivo**, e a lista de tipos aceitos é fechada: imagem, PDF, texto, CSV, zip, documento e planilha do Office, e mensagem de e-mail. O que não está na lista é recusado, e não aceito por omissão;
* o conteúdo **não** fica no banco. Vai para o armazenamento configurado — disco com volume, ou Azure Blob Storage quando a API roda em mais de uma réplica ([docs/instalacao.md](docs/instalacao.md), seção 7) —, e o banco guarda metadados e a chave;
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
* cancelamento do chamado;
* reabertura do chamado.

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

## 13. Telas

### 13.1 Login

Tela para autenticação do usuário.

Campos:

* e-mail;
* senha.

Ações:

* entrar;
* **entrar com a conta Microsoft**, quando o administrador ligou e o aplicativo do Entra ID está configurado (seção 13.12). A pessoa não cria senha: no primeiro acesso a conta nasce como solicitante, e quem já tinha conta com o mesmo e-mail — com senha, criada por e-mail recebido ou pela importação — passa a entrar por ela, com o mesmo perfil e os mesmos chamados. Conta desativada não entra;
* acessar tela de cadastro, quando o autocadastro estiver ligado (seção 13.12);
* "Esqueci minha senha", quando o envio de e-mail estiver configurado (seção 13.13).

A tela pergunta à API o que oferecer (`GET /api/auth/options`, anônimo), em vez de mostrar link que levaria a uma recusa.

O login Microsoft que não dá certo volta para esta tela com o motivo — cancelado, demorou demais, conta desativada, e-mail já ligado a outra conta Microsoft. O detalhe técnico fica no log da API. O fluxo e as decisões estão em [docs/arquitetura.md](docs/arquitetura.md), decisão 4.25.

---

### 13.2 Cadastro de usuário

Tela para criação de usuário.

Campos:

* nome;
* e-mail;
* senha;
* confirmação de senha.

Na versão 1, todo usuário cadastrado pelo portal será criado com perfil “Usuário”.

Perfis técnicos e administradores são atribuídos pelo administrador na administração de usuários (seção 13.9). O primeiro administrador vem do bootstrap.

O cadastro segue as configurações gerais (seção 13.12): com o autocadastro desligado, a API recusa com 403 e a tela de login não mostra o link; com uma lista de domínios, e-mail de outro domínio é recusado com 400.

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
* solicitante — só para técnico e administrador, e só para abrir em nome de outra pessoa. O padrão é “eu mesmo”;
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
* origem;
* prazo de SLA — ou, em chamado importado, o aviso de que ele fica fora dos indicadores (seção 8.3);
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

Ações para administrador:

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
* data de criação — período em datas de calendário, inclusivas nas duas pontas, contadas em horário de São Paulo: um chamado aberto às 23:30 do dia 9 é do dia 9.

Ordenação: mais recentes, mais antigos, prazo mais próximo, prioridade, e **atualizados recentemente**. Nesta última, comentário conta como atualização — é a resposta do solicitante que a equipe mais precisa ver subir na fila.

**Mudança de status em lote.** A equipe marca chamados na lista, um a um ou todos da página, e aplica o mesmo status a todos. Serve, por exemplo, para fechar de uma vez os resolvidos da semana.

* **Confirmação:** toda mudança em lote pede uma segunda confirmação.
* **Mesmas regras do individual:** cada chamado passa pelo mesmo caminho da mudança feita na tela dele. Isso inclui a máquina de estados, o perfil, a janela de reabertura, o SLA, o histórico e o aviso ao solicitante.
* **Sem tudo ou nada:** o chamado que não pode mudar não impede os outros. Um cancelado, por exemplo, não fecha. Ele volta com o motivo e continua selecionado.
* **Seleção presa à vista:** a seleção vale para a página e os filtros em que foi feita, e trocar qualquer um deles a descarta.
* **Teto:** no máximo 100 chamados por vez, o tamanho máximo da página.
* **Quem usa:** só a equipe. O solicitante não vê a seleção.

---

### 13.7 Dashboard do administrador

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

**Período.** Abaixo da foto da fila, o mesmo painel responde "como fomos?": últimos 7, 30 ou 90 dias — a escolha fica na URL —, comparados com o período anterior de mesmo tamanho. Indicadores:

* chamados abertos e resolvidos;
* **cumprimento do prazo de resposta**, sobre os chamados abertos no período. Prazo ainda correndo não conta a favor nem contra;
* **cumprimento do prazo de resolução**, sobre os resolvidos no período, já descontadas as pausas;
* **taxa de reabertura**: reaberturas registradas no histórico durante o período, sobre as resoluções. É o indicador de solução que não resolveu;
* os mesmos três por técnico.

Percentual sem base aparece como "—", e não como 0%: "ninguém cumpriu" e "não havia o que medir" são coisas diferentes. Tudo vem de consultas agregadas, como o resto do dashboard, e o período tem no máximo um ano.

---

### 13.8 Troca de senha

Formulário com senha atual, nova senha e confirmação. Trocar a senha encerra as demais sessões do usuário e reabre a atual, para a pessoa não ser devolvida ao login logo depois de ter provado quem é.

Atende dois casos com a mesma tela. Na troca voluntária é uma tela como as outras. Na **troca obrigatória** — o primeiro acesso do administrador criado pelo bootstrap — ela fica fora do layout do aplicativo, sem menu, e a navegação não sai dali: não há para onde ir, porque a API recusa todo o resto. A única outra saída é sair da conta.

### 13.9 Administração de usuários

Só administrador. Lista todos os usuários, ativos e desativados, com busca por nome ou e-mail e filtros por perfil e situação, paginada no servidor. Em cada linha: perfil, cargo, situação, marca de senha provisória e quantos chamados em aberto a pessoa tem sob responsabilidade.

Ações:

* **cadastrar** uma pessoa, com nome, e-mail, perfil e, opcionalmente, cargo;
* **trocar o nome** exibido, inclusive o da própria conta — é assim que o administrador do bootstrap deixa de se chamar pelo nome genérico. O e-mail, que identifica a conta, não muda;
* **trocar o cargo**, inclusive o da própria conta — cargo não dá permissão, então a trava da própria conta não se aplica a ele;
* **redefinir a senha** de alguém;
* **trocar o perfil** entre Usuário, Técnico e Administrador;
* **desativar** e **reativar** a conta;
* **excluir** a conta desativada que não deixou rastro — o cadastro feito por engano, o convite que ninguém aceitou. Quem já participou de algo não é excluído: a pessoa é autora de chamados, comentários e registros, e o histórico precisa continuar apontando para ela. Para ela, desativar é o fim da linha;
* **ver o histórico** da conta.

**Cadastro e redefinição entregam o acesso de um de dois jeitos**, conforme o envio de e-mail esteja configurado:

* **com e-mail:** a pessoa recebe um link para definir a própria senha, válido por três dias. O administrador nunca vê nem escolhe a senha de ninguém. Na redefinição, a senha antiga deixa de valer na hora, e não só quando o link for usado — redefinir é o que se faz quando se desconfia da senha;
* **sem e-mail:** a tela mostra uma senha provisória **uma única vez**, para o administrador repassar, e a conta nasce com troca obrigatória no primeiro acesso, a mesma trava do bootstrap.

Redefinir derruba as sessões abertas da pessoa.

Regras:

* **o administrador não altera o próprio perfil nem desativa a própria conta.** Quem age é sempre um administrador ativo; se ele não pode se tirar do posto, o sistema nunca fica sem administrador — situação que não teria volta pela interface, e que o bootstrap também não resolve, porque só age quando não existe administrador nenhum;
* **desativar, ou rebaixar a Usuário, quem é responsável por chamado em aberto é recusado** (409), com a lista dos chamados no corpo da resposta. O administrador reatribui antes. Aberto aqui é qualquer status não terminal — Resolvido conta, porque o solicitante ainda pode devolvê-lo. Chamado que a pessoa *abriu* não bloqueia nada: continua na fila e continua sendo atendido;
* **só se exclui conta já desativada, e sem histórico** (409 nos dois casos). Desativar primeiro faz a exclusão passar pelas mesmas travas e ser um segundo passo deliberado. "Sem histórico" quer dizer que nada fora da própria conta aponta para ela: chamado aberto ou atendido, comentário, registro no histórico de chamado, anexo, notificação causada, alteração de configuração ou de outra conta. Vão junto só as sessões, os links de senha, as notificações recebidas e a trilha da própria conta; o registro da exclusão fica no log da aplicação;
* **rebaixar e desativar derrubam as sessões** da pessoa (todos os refresh tokens são revogados). O token de acesso já emitido vale até expirar, no máximo `Jwt:AccessTokenMinutes` (15 minutos por padrão); a administração de usuários confere o perfil de quem age no banco, para que essa janela não sirva para promover ninguém. **Promover não derruba nada**: a renovação seguinte já traz o perfil novo.

**Trilha de auditoria.** Cadastro, troca de perfil, troca de cargo, desativação, reativação, troca de senha, redefinição pelo administrador e senha definida por link ficam registrados em `UserAuditEntry`, com quem fez, quando, e valor anterior e novo quando houver. A tela mostra o histórico de cada conta. Como o `TicketHistory`, a tabela é preenchida por interceptor do EF Core e não aceita alteração. Senha nunca entra no registro, só o fato de ela ter mudado.

### 13.10 Administração de categorias

Só administrador. Lista todas as categorias, ativas e desativadas, com busca por nome e filtro por situação, e quantos chamados cada uma tem, no total e em aberto.

Ações: **criar**, **editar** nome e descrição, **desativar** e **reativar**. Não existe exclusão.

Regras:

* **categoria desativada sai dos seletores** — abertura e reclassificação a recusam —, e **os chamados que já estão nela continuam válidos**, exibindo o nome dela normalmente. É para isso que `IsActive` existe em vez de `DELETE`. No detalhe do chamado, o seletor de categoria mostra a atual marcada "(desativada)": dá para tirar o chamado dela, não para colocar outro;
* **o nome é único sem diferenciar maiúsculas**, contando as desativadas. "VPN" e "vpn" no mesmo catálogo dividiriam o dashboard, que agrupa por nome;
* **renomear não reescreve o histórico**: o histórico guarda o identificador da categoria, não o nome;
* **a última categoria ativa não pode ser desativada** (409). Sem nenhuma, ninguém abre chamado e o `/ready` reprova a instalação.

### 13.11 Feriados

Só administrador. Os feriados de um ano, com navegação entre anos, ligados e desligados.

* **Os nacionais vêm do sistema e se renovam sozinhos.** Uma tarefa diária garante o ano corrente e os dois seguintes (`Holidays:RenewalIntervalHours`; zero desliga). Antes, em produção, a janela só andava com o seed, e em algum ano o cálculo de horas úteis passaria a contar feriado como expediente — em silêncio;
* **nacional se desliga, não se apaga**, para a empresa que trabalha no dia. A renovação respeita a escolha: não religa o que o administrador desligou;
* **o administrador cadastra os que a tabela nacional não tem**: municipal, estadual, recesso de fim de ano. Data já ocupada é recusada (409). Esses ele pode apagar;
* **mudar um feriado vale para os chamados abertos dali em diante.** Prazo de chamado já aberto não é recalculado: é o compromisso assumido na abertura.

### 13.12 Configurações

Só administrador, em duas abas.

**Geral:**

* **nome da organização**, que aparece no login, no menu e nos e-mails de convite e de senha;
* **autocadastro**, ligado ou desligado. Desligado, só o administrador cria contas;
* **domínios de e-mail aceitos no autocadastro**, como `empresa.com.br`. A comparação é exata: `empresa.com.br` não aceita `outra-empresa.com.br` nem `sub.empresa.com.br`. Lista vazia aceita qualquer domínio. A mesma lista serve à caixa monitorada configurada para aceitar só domínios internos;
* **entrar com a conta Microsoft**, ligado ou desligado. Usa o aplicativo do Entra ID da aba E-mail — tenant, client id e secret —, que precisa ter o tenant pelo ID ou pelo domínio, nunca `common` ou `organizations`. A tela mostra o URI de redirecionamento a registrar no aplicativo e avisa enquanto faltar algo. É independente do autocadastro: com o autocadastro desligado, as pessoas do tenant continuam entrando pela Microsoft, e só a conta com senha deixa de ser criada pelo portal.

**E-mail:** envio pelo Microsoft Graph e caixas monitoradas — ver seção 18, versão 1.2, e [docs/integracao-email.md](docs/integracao-email.md).

Toda mudança de configuração é auditada: quem, o quê e quando, nunca o valor de um secret.

### 13.13 Definir senha: esqueci minha senha e convite

Duas portas para a mesma tela, `/redefinir-senha`, aberta por um link enviado por e-mail:

* **Esqueci minha senha**, a partir do login. A resposta é sempre a mesma, exista ou não a conta: o formulário não pode servir para descobrir quem tem cadastro. O link vale **uma hora**;
* **convite**, enviado quando o administrador cadastra uma pessoa ou redefine a senha dela (seção 13.9). Vale **três dias**.

Regras:

* o link é de uso único, e pedir outro invalida o anterior;
* só o hash do token fica no banco, como no refresh token;
* definir a senha derruba as outras sessões e limpa a marca de senha provisória;
* o corpo do e-mail com o link é apagado da fila de saída depois do envio. A fila guarda o que foi enviado para diagnóstico, e um link de senha guardado ali seria uma credencial esquecida no banco;
* é também por aqui que quem foi cadastrado sem senha — pela caixa de e-mail ou pela importação — passa a entrar no portal, e reencontra os próprios chamados.

Sem envio de e-mail configurado, nada disso aparece: a tela de login esconde o link e o administrador recebe senha provisória no lugar do convite.

### 13.14 Importação de chamados

Só administrador. Traz o histórico de outra ferramenta, em dois passos: **conferir** e **importar**.

| Origem | Arquivo | Colunas reconhecidas |
| --- | --- | --- |
| Modelo do OpsDesk | CSV ou JSON (lista de objetos) | `titulo` e `solicitante_email` obrigatórias; `descricao`, `solicitante_nome`, `categoria`, `prioridade`, `status`, `responsavel_email`, `criado_em`, `resolvido_em`, `fechado_em`, `codigo_externo` |
| Freshdesk | exportação de tickets em CSV | `Ticket ID`, `Subject`, `Description`, `Status`, `Priority`, `Type`, `Agent`, `Created time`, `Resolved time`, `Closed time`, e nome e e-mail do contato |
| Freshservice | exportação de tickets em CSV | `Ticket Id`, `Subject`, `Description`, `Status`, `Priority`, `Category`, `Agent`, `Created Date`, `Resolved Date`, `Closed Date`, e-mail do solicitante |

A tela oferece o modelo do OpsDesk para baixar. A integração com o Freshdesk e o Freshservice é pelo **arquivo de exportação** das duas ferramentas, e não pela API delas: importação é uma migração feita uma vez, e o arquivo dispensa guardar credencial de outro sistema.

**Conferir** lê o arquivo inteiro e não grava nada. Diz quantas linhas entrariam, quantas já foram importadas antes, quantos solicitantes seriam criados, e quais linhas seriam puladas e por quê, com o número da linha no arquivo. Só depois aparece o botão de importar.

Regras:

* **CSV com vírgula ou ponto e vírgula**, descoberto pelo cabeçalho: o Excel em português salva com ponto e vírgula. Campo entre aspas, com quebra de linha dentro, é lido normalmente. O arquivo precisa estar em UTF-8 ("CSV UTF-8" no Excel);
* **importar de novo não duplica.** O identificador da origem vai para `ExternalId`, e linha já importada é pulada;
* **solicitante desconhecido vira usuário sem senha**, como na caixa de e-mail. Ele reencontra os chamados quando definir uma senha (seção 13.13);
* **categoria pelo nome**, sem diferenciar maiúsculas; sem correspondência, "Outros";
* **responsável pelo e-mail, ou pelo nome quando ele é único na equipe.** Nome que dois técnicos compartilham não é adivinhado: o chamado entra sem responsável;
* **status e prioridade** em português ou nos nomes da ferramenta de origem ("Pending" vira Aguardando usuário, "Urgent" vira Crítica). Vazio é Aberto e Média; valor desconhecido pula a linha;
* **datas sem fuso são hora de São Paulo**, nos formatos `2026-09-30 14:00` e `30/09/2026 14:00`; com fuso explícito, vale o do arquivo;
* **importar não notifica ninguém** — nem e-mail, nem portal. A abertura entra no histórico, em nome do administrador que importou;
* **chamado importado fica fora dos indicadores de SLA** (seção 8.3);
* até 10 MB e 5.000 linhas por arquivo.

**Conciliação com o Spiceworks.** É outra ferramenta, para outro problema: chamados que o OpsDesk **já tem**, abertos pela caixa de e-mail a partir de mensagens antigas. Um chamado desses nasce com a data em que a mensagem foi lida, aberto e com prazo de SLA contado dali. Quando a caixa traz meses de mensagens de uma vez, ele traz também meses de chamados "de hoje" e abertos. A exportação JSON do Spiceworks Cloud Help Desk (`ticket_export.json`) sabe a data real e se o chamado foi fechado. O elo entre as duas ferramentas é o Message-ID do e-mail de abertura: `email_message_id` lá, `ExternalId` aqui.

Os passos são os mesmos da importação: conferir e depois aplicar. Para cada chamado casado:

* **datas:** abertura, primeira resposta e última atualização passam a ser as do Spiceworks, e os prazos de SLA são recalculados a partir da abertura real. O chamado continua com origem E-mail, medido pelo SLA (seção 8.3), agora com as datas verdadeiras;
* **fechado lá, fechado aqui:** o chamado que está em andamento no OpsDesk e fechado no Spiceworks fica fechado, com a data de lá. O que já estava resolvido ou fechado aqui só recebe a data. O cancelado não muda;
* **aberto lá:** continua como está. Se aqui o chamado já terminou, a conferência aponta para ser revisto à mão, assim como o chamado aberto lá que não tem correspondente aqui;
* **sem aviso e sem linha no histórico:** é correção de origem, não atendimento. Fechar pelo caminho normal mandaria um aviso a cada solicitante de chamados atendidos meses atrás. A gravação passa ao largo dos interceptores, como a recuperação de anexos (docs/integracao-email.md, 4.7.1);
* **aplicar de novo não muda nada;**
* fica de fora o chamado aberto no Spiceworks sem e-mail, que não tem Message-ID para casar. O arquivo pode ter até 25 MB.

### 13.15 E-mails recebidos e caixa de SPAM

Técnico e administrador. As mensagens recebidas pela caixa monitorada, em abas: **SPAM**, **Falhas**, **Processados**, **Duplicados** e **Regras**. Ações na mensagem retida: não é spam (vira chamado ou resposta), não é spam e liberar o remetente, confirmar e bloquear o remetente, bloquear o domínio. Falha pode ser reprocessada. Só o administrador exclui. Especificação completa em [docs/integracao-email.md](docs/integracao-email.md).

---

### 13.16 Administração de cargos

Só administrador, em `/cargos`. Lista os cargos, ativos e desativados, com quantas pessoas têm cada um.

Ações: **criar**, **renomear**, **desativar** e **reativar**. Não existe exclusão.

Regras:

* **cargo é só informativo.** Não entra em autorização nenhuma: o que a pessoa pode fazer é o perfil (seção 4);
* **cargo desativado sai do seletor**, e quem já o tem continua com ele. Atribuí-lo de novo é recusado;
* **o nome é único sem diferenciar maiúsculas**, contando os desativados, como nas categorias;
* **a trilha da conta guarda o nome do cargo da época**, e não o identificador: renomear depois não reescreve o passado.

O catálogo inicial — Tech Lead, Analista, Assistente, Estagiário e Aprendiz — vem da migration `AddJobTitles`, e não do seed, porque o deploy de produção roda só `--migrate`. Depois disso o catálogo é do administrador, e nenhuma etapa o recria.

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
* JobTitleId — cargo, anulável e só informativo;
* IsActive;
* MustChangePassword;
* EntraObjectId;
* CreatedAt;
* UpdatedAt.

`EntraObjectId` é o identificador da pessoa no Entra ID (claim `oid`), gravado no primeiro login com a conta Microsoft. Nos logins seguintes é ele, e não o e-mail, que reconhece a pessoa. Único quando preenchido. O vínculo entra na trilha de auditoria da conta.

`MustChangePassword` marca senha provisória. Nasce do bootstrap do primeiro administrador, cuja senha chega por variável de ambiente, e da senha provisória que o administrador recebe ao cadastrar ou redefinir alguém sem e-mail configurado (seção 13.9). Enquanto for verdadeiro, a API recusa toda rota fora de `/api/auth` com 403, e a troca pelo próprio usuário limpa a marca. É o que mantém uma credencial que passou pela mão de outra pessoa valendo uma vez, e não para sempre.

`PasswordHash` é anulável. Conta sem senha não autentica **com senha** até a pessoa definir uma por link (seção 13.13) — pela conta Microsoft, entra normalmente. Nasce assim o solicitante que chegou pela caixa de e-mail ou pela importação, a pessoa convidada pelo administrador e a conta cuja senha o administrador redefiniu por e-mail.

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
* `Source` identifica a origem do chamado (seção 9.1) e decide se ele entra nos indicadores de SLA (seção 8.3);
* `ExternalId` guarda o identificador da origem externa — o `Message-ID` do e-mail que gerou o chamado, ou o código do chamado na ferramenta de onde foi importado. É único por origem, e é o que impede a mesma mensagem ou a mesma linha de virar dois chamados;
* `FirstRespondedAt` fecha o SLA de resposta;
* `SlaPausedAt` e `SlaPausedBusinessMinutes` sustentam a pausa descrita na seção 8.2.

---

### JobTitle

Cargo da pessoa (seção 13.16). Só informativo.

Campos:

* Id;
* Name — único sem diferenciar maiúsculas;
* IsActive;
* CreatedAt;
* UpdatedAt.

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
* Name;
* IsActive;
* IsCustom.

`IsCustom` separa o feriado cadastrado pelo administrador do nacional, que vem do sistema; `IsActive` desliga um feriado sem apagá-lo (seção 13.11).

O seed popula os feriados nacionais do ano corrente e dos dois seguintes, calculando as datas móveis a partir da Páscoa, e uma tarefa diária mantém essa janela andando. Carnaval e Corpus Christi entram na lista: são ponto facultativo federal, mas a equipe de suporte não está de plantão, e SLA contando hora útil em dia sem ninguém atendendo é indicador mentiroso.

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

### UserAuditEntry

Trilha de auditoria de conta (seção 13.9). Append-only, preenchida por interceptor, como o `TicketHistory`.

Campos:

* Id;
* UserId;
* Action — `Created`, `RoleChanged`, `JobTitleChanged`, `Deactivated`, `Reactivated`, `PasswordChanged`, `PasswordReset`, `PasswordSetByLink` ou `MicrosoftAccountLinked`;
* PreviousValue;
* NewValue;
* ChangedById;
* CreatedAt.

---

### PasswordResetToken

Link de definição de senha (seção 13.13). Só o hash fica no banco.

Campos:

* Id;
* UserId;
* TokenHash;
* ExpiresAt;
* UsedAt;
* CreatedAt.

---

### SystemSettings

Configurações gerais (seção 13.12). Uma linha só.

Campos:

* Id;
* OrganizationName;
* AllowSelfRegistration;
* AllowedEmailDomains;
* UpdatedById;
* CreatedAt;
* UpdatedAt.

---

### Entidades de apoio

Existem para sustentar uma funcionalidade e não aparecem como tela própria:

| Entidade | Para quê |
| --- | --- |
| `TicketAttachment` | metadados do anexo; o conteúdo fica no armazenamento (seção 11.1) |
| `Notification` | aviso no portal, por pessoa (seção 18, versão 1.2) |
| `OutboundEmail` | fila de saída de e-mail |
| `EmailSettings` | configuração do envio pelo Graph, com o secret cifrado |
| `SettingsAuditEntry` | quem mudou qual configuração, e quando |
| `SlaAlert` | alerta de SLA já dado, com o prazo na chave |

---

### Entidades da versão 2.0

A ingestão de e-mail acrescenta `EmailAccount`, `InboundEmail` e `SpamRule`, especificadas em [docs/integracao-email.md](docs/integracao-email.md). A seção 9 dele registra os campos que mudaram na implementação.

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
* dashboard de administrador;
* painel técnico.

---

### Técnico

Pode acessar:

* painel técnico;
* chamados sem responsável;
* chamados atribuídos a ele, pelo filtro "Meus chamados";
* comentários internos;
* histórico dos chamados que pode atender;
* e-mails recebidos e caixa de SPAM, sem excluir mensagem.

Não pode acessar:

* configurações globais do sistema;
* administração de usuários, categorias e feriados;
* importação de chamados.

---

### Administrador

Pode acessar:

* todos os chamados;
* dashboard;
* painel técnico;
* atribuição de responsáveis;
* alteração de prioridade;
* alteração de categoria;
* visão geral da operação;
* administração de usuários: cadastro, perfil, desativação e redefinição de senha, exceto na própria conta (seção 13.9);
* administração de categorias: criar, editar, desativar e reativar (seção 13.10);
* administração de feriados (seção 13.11);
* configurações gerais e de e-mail (seção 13.12);
* importação de chamados (seção 13.14);
* exclusão de mensagem da caixa de SPAM.

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

* listagem de chamados deve ter paginação: 25 por página por padrão, com escolha de 50 ou 100, que é o máximo aceito pelo servidor. A navegação mostra o total, permite pular para a primeira, a última ou uma página digitada, e a página e o tamanho ficam na URL junto com os filtros;
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
* frontend em container, no perfil `web`, servido por nginx;
* Playwright para os testes de ponta a ponta, contra o perfil `web`.

### Produção

* compose próprio em `deploy/`, com Caddy na borda (HTTPS por CA interna ou Let's Encrypt);
* anexos em volume ou no Azure Blob Storage, com Managed Identity;
* GitHub Actions para build e testes, inclusive os de ponta a ponta.

### Infraestrutura futura

* Azure Container Apps ou App Service;
* Azure Database for PostgreSQL;
* Azure Front Door;
* Application Insights.

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
* **instalação em branco**: comandos de migration, seed e bootstrap do primeiro administrador, com troca de senha obrigatória no primeiro acesso;
* **diagnóstico de instalação**: `/ready` separado do `/health`, e validação de configuração na subida.

### Primeira execução: instalar em branco

Até a 1.0 o projeto subia pronto para uso **em desenvolvimento**, e apenas nele. A API aplicava migration e seed na subida só quando o ambiente era `Development`; em qualquer outro, `ApplyDatabaseStartupTasksAsync` retornava cedo e nada acontecia. Quem clonasse e subisse em produção encontrava banco sem schema, zero categorias e políticas de SLA — abrir chamado respondia **500** —, zero feriados e zero usuários, sem caminho pela interface para o primeiro administrador existir. O ambiente de desenvolvimento escondia tudo isso, o que é justamente o que tornava o problema traiçoeiro: funcionava na máquina de quem escreveu.

**Entregue: instalação como etapa explícita de deploy.** O executável da API aceita `--migrate`, `--seed`, `--bootstrap-admin` e `--setup`, faz a tarefa e termina, sem abrir porta. Os comandos e o exemplo de uso estão na seção 1, em "Como executar". A decisão entre comando explícito e execução automática na subida ficou com o comando, pelo mesmo argumento que já valia para a migration: ninguém deve descobrir alteração de banco pelo log de inicialização. O preço é conhecido e aceito — quem esquecer de rodar sobe um sistema que parece saudável e recusa o primeiro chamado, e é o que o item de readiness abaixo passa a acusar.

**Entregue: o primeiro administrador, por variável de ambiente.** `OpsDesk__Bootstrap__Email` e `OpsDesk__Bootstrap__Password`, aplicadas uma única vez quando não existir nenhum administrador, com **troca de senha obrigatória no primeiro acesso** (`User.MustChangePassword`). As outras duas opções foram descartadas: o comando de linha dedicado exigiria um passo manual e acesso ao contêiner sem ganho real de segurança sobre isto, e "o primeiro usuário cadastrado vira administrador" é cómodo e perigoso — numa instância exposta antes de ser configurada, quem chegasse primeiro viraria administrador do sistema.

O custo do caminho escolhido é a senha existir em configuração, que é lugar que vaza: log de deploy, `docker inspect`, histórico do shell. A troca obrigatória é o que paga esse custo, e por isso ela não é cosmética — enquanto a marca estiver de pé, a API recusa com 403 tudo fora de `/api/auth`, por middleware e não por atributo de rota, para que endpoint novo nasça coberto.

**Entregue: promover os demais.** O bootstrap resolve o primeiro administrador; os seguintes, e os técnicos, saem da tela de administração de usuários (seção 13.9), antecipada da 1.1.

**Entregue: feriados que se renovam, e tela para administrá-los.** O seed cobria o ano corrente e os dois seguintes, e essa janela só "andava" porque rodava a cada subida em desenvolvimento. Em produção, em algum ano o cálculo de horas úteis passaria a contar feriado como expediente — em silêncio. Decidido: as duas coisas. Uma tarefa diária mantém a janela, e a tela da seção 13.11 resolve o que nenhuma tabela nacional tem, o feriado municipal e o recesso.

**Entregue: validação de configuração na subida.** `Jwt:SigningKey`, connection string e `AttachmentStorage` já falhavam cedo. Entraram os casos que subiam calados: raiz de anexos sem permissão de escrita (um arquivo de sonda na subida, em vez do `Permission denied` no primeiro upload) e fuso de expediente inválido — `BusinessHoursOptions` declarava `ValidateOnStart` sem validador registrado, ou seja, não validava nada; agora fuso inexistente, expediente invertido e lista de dias úteis vazia derrubam a subida com mensagem.

`Cors:AllowedOrigins` vazio ficou como **aviso**, e não como erro, contra o que este roadmap previa. O motivo apareceu ao implementar: no perfil `web` o nginx põe SPA e API na mesma origem, e nessa topologia a lista vazia é a configuração correta — derrubar a subida quebraria justamente o desenho mais próximo de produção. O que virou erro foi origem malformada: o middleware compara origem como texto, então `http://localhost:3000/` com barra no fim nunca casa, e o navegador reporta só um bloqueio genérico enquanto o log da API não registra nada.

**Entregue: readiness separado de liveness.** O `/health` responde se o processo está vivo e o banco alcançável, e é o que decide reiniciar o contêiner. O `/ready` acrescenta schema na versão da aplicação e dados de referência presentes, e é o que decide mandar tráfego; o corpo da resposta traz o comando que falta. Os testes de readiness ficam **fora** do `/health` de propósito: reiniciar o contêiner não aplica migration nem roda seed, e incluí-los daria um laço de reinício sem diagnóstico.

**Entregue: compose de produção.** Pasta `deploy/`, com Caddy na borda (HTTPS por CA interna ou Let's Encrypt, HSTS e CSP), banco e API sem porta publicada, segredos num `.env` fora do repositório gerado com valores aleatórios por `new-env.sh`, volumes nomeados e um `.env.example` comentado. Guia em [docs/instalacao.md](docs/instalacao.md). O `docker secret` ficou de fora: no compose sem Swarm ele é um arquivo montado, sem ganho real sobre o `.env` com permissão restrita, e o cofre de verdade é assunto do orquestrador de quem for hospedar.

**Imagem do frontend com endereço de API definido no build.** `VITE_API_URL` é embutido em tempo de compilação, então a mesma imagem não serve a dois domínios. Hoje isso não incomoda porque o nginx do perfil `web` põe SPA e API na mesma origem; incomodaria numa topologia com domínios separados.

*A decidir, só quando essa topologia existir:* gerar a configuração em tempo de execução (um `config.js` escrito pelo entrypoint do contêiner) ou manter a regra de mesma origem.

**Entregue: dados de demonstração.** Comando próprio, `--seed-demo`, separado do seed de referência: cinco pessoas e oito chamados em todos os status, com conversa, nota interna, anexo, um vencido e um aguardando o solicitante. Recusa rodar em banco que já tem chamado, o que o impede de escapar para uma instalação em uso.

**Entregue: backup e restauração juntos.** `deploy/backup.sh` grava banco, anexos e chaves do Data Protection numa pasta só — as chaves entraram porque, sem elas, o secret do e-mail guardado no banco não se lê mais. `deploy/restore.sh` confere tudo antes de apagar qualquer coisa, para a API e restaura na ordem. O procedimento foi testado de ponta a ponta: backup, alteração, restauração e conferência de chamados, anexo e secret.

### Versão 1.1 — Melhorias operacionais

**Entregue antes do prazo: tela de administração de usuários.** Seção 13.9. As três decisões que estavam pendentes ficaram assim: só administrador administra; desativar ou rebaixar quem tem chamado em aberto exige reatribuir antes; e o administrador não mexe na própria conta.

**Entregue: trilha de auditoria de usuário.** Decidido pela tabela própria, `UserAuditEntry`, preenchida por interceptor no molde do `TicketHistory` (seção 13.9). Junto vieram cadastro pelo administrador, convite por e-mail, "esqueci minha senha" e as configurações gerais de autocadastro (seções 13.12 e 13.13).

**Entregue antes do prazo: tela de administração de categorias.** Seção 13.10. A decisão pendente ficou como a intenção previa: chamado de categoria desativada continua válido, e a categoria só sai dos seletores. Implementar revelou uma armadilha no seed, que comparava por nome e por isso recriava a categoria renomeada a cada subida; ele agora só semeia em banco sem categoria nenhuma.

**Entregue: filtrar por categoria desativada.** O filtro da lista pede `/api/categories?includeInactive=true`, que só a equipe pode usar, e mostra as desativadas marcadas. Os seletores de abertura e reclassificação continuam só com as ativas.

**Entregue: testes de frontend.** Vitest com Testing Library, rodando no CI (`npm test`). Cobrem o que dói, não tudo: marcação de comentário e anexo internos, filtros da lista reconstruídos da URL, as ações respeitando `allowedNextStatuses`, o aviso de reabertura e a conversão de período para o horário de São Paulo. A marca de anexo interno ganhou texto para leitor de tela no caminho — antes era só cor e ícone.

**Entregue: teste de ponta a ponta.** Playwright, em `e2e/`, contra o perfil `web` do compose: login, abertura, resposta, nota interna fora da vista do solicitante, chamado alheio inacessível pelo endereço, o dashboard e o menu de cada perfil. Decidido: roda no CI, num job que sobe a pilha inteira.

**Entregue: reabrir chamado.** Seção 5.9. Decidido: reabre o mesmo chamado; o solicitante reabre respondendo, a equipe pela ação de status; chamado fechado reabre por sete dias; o SLA continua de onde parou. A decisão corrigiu também a volta de "Resolvido" para "Em atendimento", que já existia e fazia o chamado voltar vencido pelo tempo parado.

**Entregue: indicador de reabertura.** Decidido contar pelo histórico, numa consulta agregada, sem contador no chamado: o dado já existia e não pode divergir de si mesmo. Aparece no dashboard por período, no total e por técnico (seção 13.7).

**Entregue: filtros por responsável e período, e ordenação por última atualização.** Seção 13.6. O período viaja como instante com o deslocamento de São Paulo, e a API o converte para UTC antes da consulta — o Npgsql recusa parâmetro com deslocamento diferente de zero, e sem a conversão o filtro respondia 500.

**Entregue: busca na descrição.** A busca cobre código, título e descrição. Decidido por índice de trigramas (`pg_trgm`) com GIN sobre `lower(coluna)`, e não por `tsvector`: o trigrama acelera o mesmo `LIKE` de substring que a busca já fazia, e o resultado não muda de significado. Com `tsvector`, "impress" deixaria de achar "impressora", e código de erro e nome de máquina passariam pelo dicionário do português. Há teste conferindo pelo plano de execução que o índice é usado.

**Entregue: dashboard por período.** Recorte de 7, 30 ou 90 dias, comparação com o período anterior, cumprimento de SLA em percentual e desempenho por técnico (seção 13.7).

### Versão 1.2 — Notificações

Era a maior lacuna funcional do produto: ninguém descobria que um chamado mudou a não ser abrindo a tela. O técnico não sabia que foi atribuído, e o solicitante não sabia que a resposta chegou.

**Entregue: e-mail e notificação no portal ao solicitante, e notificação no portal ao responsável.**

Quem recebe o quê:

| Evento | Solicitante (e-mail e portal) | Responsável (portal) |
| --- | --- | --- |
| Resposta pública da equipe | sim | sim, se foi outra pessoa |
| Nota interna | **nunca** | sim, se foi outra pessoa |
| Resposta do solicitante | — | sim ("respondeu") |
| Mudança de status | sim | sim, se foi outra pessoa |
| Reabertura | — (foi ele quem reabriu) | sim ("reabriu") |
| Atribuição | — | quem recebe e quem perde o chamado |
| Prazo de SLA perto de vencer ou vencido | — | sim (ver alertas de SLA, abaixo) |

O solicitante recebe no portal os mesmos avisos do e-mail, e no portal eles chegam mesmo com o envio de e-mail desligado ou parado no spam. Quando resposta e mudança de status vêm juntas, como em "enviar e fechar", o aviso do portal é o da mudança de status — é o que muda a vida dele; a resposta ele lê ao abrir.

* **o administrador não recebe nada por ser administrador**: recebe no portal quando o chamado está no nome dele, pela mesma regra de qualquer responsável;
* **ninguém é avisado da própria ação**;
* **uma gravação, um aviso por pessoa.** "Enviar e fechar" é um e-mail com a resposta e o novo status, não dois;
* **o e-mail avisa que respostas a ele não são lidas.** Até a versão 2.0 ninguém lê a caixa de suporte, e uma resposta por e-mail se perderia sem que a pessoa soubesse. O aviso sai quando a ingestão existir;
* **abertura de chamado não gera aviso** — nem para o solicitante, que acabou de abri-lo, nem para a equipe, que o vê na fila.

**Como funciona por dentro.** Um interceptor do EF Core, irmão do que grava o histórico, olha o que mudou em cada gravação — comentário novo, status, responsável — e grava as notificações e os e-mails na mesma transação. Aviso que depende de cada serviço lembrar de chamar alguma coisa funciona até o primeiro endpoint novo; aqui o gatilho é o dado. A regra de quem recebe o quê mora numa função pura, `NotificationPlanner`, e cada destinatário passa pela mesma regra de visibilidade da API: responsável que deixou de ser da equipe não recebe nota interna, e a notificação some do sino de quem deixou de enxergar o chamado.

**Decidido: envio pelo Microsoft Graph, configurado no próprio portal.** A empresa usa Microsoft 365. O envio é pela API `sendMail` do Graph, com um aplicativo registrado no Entra ID, e não por SMTP: a Microsoft anunciou a desativação da autenticação básica no SMTP do Exchange Online, e SMTP com OAuth dá o mesmo trabalho de registro sem vantagem. O remetente é a caixa de suporte — a mesma que a versão 2.0 vai monitorar —, para que "responder" no e-mail volte para ela.

A configuração fica numa tela **Configurações → E-mail**, só para administrador, e não em variável de ambiente. O motivo decisivo é que o *client secret* do Entra ID expira, em no máximo 24 meses: em variável de ambiente, trocá-lo exige acesso ao servidor e deploy, e esquecer faz o sistema parar de mandar e-mail em silêncio. A tela tem:

* envio ligado ou desligado;
* endereço remetente e nome de exibição;
* tenant, client id e client secret;
* **enviar e-mail de teste**, que é o que de fato prova que a configuração funciona;
* situação do último envio e validade do secret, com aviso antes de vencer.

Regras da configuração:

* **o secret é só de escrita.** A API nunca o devolve: a tela mostra "configurado em" e um botão de trocar. Devolvê-lo entregaria a credencial a qualquer sessão de administrador, inclusive a um token roubado;
* **o secret é cifrado no banco** pelo Data Protection do ASP.NET, e **as chaves do Data Protection ficam em volume persistente.** Guardadas dentro do contêiner, um reinício gera chaves novas, o secret gravado deixa de ser decifrável e o e-mail para depois do deploy, sem erro que aponte a causa;
* **mudança de configuração é auditada**: quem, o quê, quando — nunca o valor do secret.

Pré-requisito do lado do Microsoft 365, que é do administrador do tenant: registrar o aplicativo com a permissão `Mail.Send` de aplicativo **e restringi-la à caixa de suporte** por política de acesso do Exchange. Sem a restrição, a permissão vale para toda caixa da empresa, e um secret vazado vira envio de e-mail em nome de qualquer pessoa.

**Decidido: fila, e não envio no mesmo request.** A notificação vira uma linha numa tabela de saída na mesma transação da mudança que a gerou, e um serviço em segundo plano envia, com novas tentativas. Comentário nunca falha porque o Microsoft 365 está fora do ar, e nenhuma notificação se perde entre gravar a mudança e enviar. A fila é uma tabela no PostgreSQL que já existe, não infraestrutura nova.

Com o envio desligado, **nenhum e-mail entra na fila** — ligar o envio não dispara uma avalanche de avisos antigos. Os que já estavam na fila esperam. Cada e-mail tem até cinco tentativas, espaçadas em 1, 5, 15 e 60 minutos; depois disso fica como desistido, contado na tela de configuração.

**Agrupar e-mails de chamado muito ativo.** Hoje cada gravação com resposta pública ou mudança de status é um e-mail. Numa conversa rápida, são vários e-mails em poucos minutos.

*A decidir:* se isso incomodar na prática, agrupar por janela de tempo (segurar o e-mail alguns minutos e juntar o que chegar nesse intervalo). Não fizemos antes de ver o uso real, porque atrasar o aviso também tem custo.

**Resolvido: despacho com mais de uma réplica.** Antes de enviar, cada réplica reserva o e-mail com uma atualização condicional — só ganha quem ainda vê o mesmo `NextAttemptAt` — e a reserva vale cinco minutos. Duas réplicas na mesma rodada mandam cada e-mail uma vez; há teste com envios concorrentes fixando isso.

**Atenção, e não é detalhe:** a invariante 2 vale em canal novo. Nota interna e anexo interno **nunca** entram no corpo de um e-mail, e o destinatário de cada mensagem precisa ser derivado da mesma regra de visibilidade que a API usa — não de uma lista montada à mão no serviço de notificação. Todo endpoint novo que exponha comentário pede teste do caso negativo; o mesmo vale para todo canal novo.

**Entregue: notificações na interface.** Sino no cabeçalho, para todos os perfis, com o número de não lidas; tela de notificações com filtro de não lidas e "marcar todas como lidas". O contador consulta a API a cada minuto com a aba visível — tempo real exigiria conexão aberta com o servidor, que fica para quando fizer falta.

**Entregue: alertas de SLA no portal.** Dois avisos por prazo — resposta e resolução —: um quando resta **25% do prazo da prioridade**, em horas úteis, e outro quando ele vence. Proporcional porque um valor fixo chegaria tarde para o crítico ou cedo para o de baixa prioridade: crítico, com 4 horas de resolução, avisa com 1 hora; média, com 30, avisa com 7h30.

* **quem recebe:** o responsável. Chamado sem responsável, ou com responsável desativado, avisa os administradores — do contrário o alerta mais importante, o do chamado que ninguém pegou, não iria para ninguém. O solicitante não recebe: o prazo é compromisso da equipe;
* **só no portal**, sem e-mail;
* **prazo que não corre não alerta:** resposta já dada, chamado resolvido, fechado ou cancelado, e resolução pausada enquanto aguarda o solicitante;
* **um aviso por prazo.** Quando o prazo se move — pausa encerrada, reabertura, reclassificação —, o prazo novo pode alertar de novo;
* **vencimento com mais de 24 horas não alerta.** Sem esse corte, a primeira verificação depois do deploy avisaria de uma vez todos os chamados vencidos da história; para eles existe o filtro de vencidos.

A verificação roda a cada cinco minutos no processo da API (`SlaAlerts:IntervalMinutes`; a fração é `SlaAlerts:WarningPercent`). Com várias réplicas, todas verificam, e um índice único no banco garante um aviso só.

### Versão 2.0 — Integrações

**Entregue: abertura de chamado por e-mail e caixa de SPAM.** Uma caixa do Microsoft 365, lida pelo Graph a cada minuto: mensagem nova vira chamado, resposta vira comentário — e reabre o chamado, pela regra da seção 5.9 —, e o que parece spam fica retido para revisão (seção 13.15). Especificação, e onde a implementação se afastou dela: [docs/integracao-email.md](docs/integracao-email.md).

**Entregue: importação de CSV e JSON, e do Freshdesk e do Freshservice.** Pelo arquivo de exportação das duas ferramentas, com conferência antes de gravar (seção 13.14). Chamado importado fica fora dos indicadores de SLA (seção 8.3).

**Entregue: entrar com a conta Microsoft.** Botão no login, ligado pelo administrador nas configurações gerais, com o mesmo aplicativo do Entra ID do e-mail. Quem é da empresa entra sem criar senha (seção 13.1). Decisões em [docs/arquitetura.md](docs/arquitetura.md), 4.25; o registro no Entra ID em [docs/integracao-email.md](docs/integracao-email.md), seção 6.2.

### Infraestrutura, quando sair do ambiente local

**Entregue: anexos em Azure Blob Storage.** `AttachmentStorage:Provider = AzureBlob`, com Managed Identity, e a subida grava um blob de sonda para acusar permissão faltando. Connection string com chave de conta é recusada; só a do emulador é aceita, para desenvolvimento e teste, que rodam contra o Azurite. Configuração em [docs/instalacao.md](docs/instalacao.md), seção 7. O que motivou, e as decisões, ficam abaixo.

O armazenamento em disco **não sobrevive a mais de uma réplica**: cada réplica tem seu próprio sistema de arquivos, e o anexo enviado numa dá 404 na outra. O sistema de arquivos de contêiner também é efêmero — um deploy leva os anexos embora. Ou seja, isto deixa de ser melhoria e passa a ser pré-requisito no momento em que a API escalar horizontalmente.

O caminho está preparado: é uma implementação nova de `IAttachmentStorage` (decisão 4.15 de [docs/arquitetura.md](docs/arquitetura.md)), sem tocar em domínio, serviço, schema ou filtro de visibilidade. A chave de armazenamento já é um caminho hierárquico, que é o que o Blob espera.

*Decisões e cuidados:*

* **Não guardar anexo no banco.** Binário em coluna infla backup e restore, prende conexão do pool durante o streaming, e custa por volta de uma ordem de grandeza mais por GB que blob — contado de novo no backup e na réplica.
* **Managed Identity**, nunca connection string com chave de conta: chave em configuração é credencial permanente que vaza em log e em dump de variável de ambiente.
* **Como os bytes chegam ao navegador.** Hoje passam pela API, que aplica o filtro de visibilidade antes de abrir o stream. A alternativa é devolver URL SAS e deixar o navegador buscar direto — mas **SAS é portador**: quem tem o link acessa sem autenticação até expirar. Se adotar: gerar depois da verificação de visibilidade, no mesmo endpoint de download e nunca junto da listagem, com *user delegation SAS* e expiração de minutos. A recomendação é continuar transmitindo pela API e migrar quando o egresso justificar.
* **Azure Files** montado como volume é a saída sem mexer em código, com latência pior e custo maior. Serve para adiar, não para resolver.
* **Defender for Storage** com varredura de malware, *soft delete* de 7 a 30 dias, e regra de ciclo de vida para camada fria. O sistema aceita zip e documento do Office de qualquer pessoa da empresa: o anexo é o vetor de entrada mais óbvio que existe.
* A varredura de pendentes roda no processo da API. Com várias réplicas, todas varrem — é idempotente e o lote é pequeno, mas se incomodar, `CleanUpIntervalHours = 0` desliga e a limpeza migra para um job com dono único.

**Entregue: forwarded headers.** O rate limiting particiona por IP de origem nas rotas anônimas, e atrás de proxy todo pedido chegaria com o IP do proxy. `UseForwardedHeaders` é o primeiro middleware, e só confia no cabeçalho vindo das redes e proxies listados em `ForwardedHeaders:KnownNetworks` e `KnownProxies` — confiar em qualquer origem deixaria o cliente escolher a própria partição. O compose de produção configura a rede interna dele.

**Migration como etapa de deploy.** A API só aplica migration automaticamente em Development. Em qualquer outro ambiente o schema sobe como passo explícito, antes da aplicação nova subir — junto com o seed de dados de referência e o bootstrap do primeiro administrador, descritos em "Primeira execução" acima. No compose de produção, é o serviço `setup`.

### Em aberto

O que ficou para depois, com o que precisa ser decidido antes:

| Item | A decidir |
| --- | --- |
| Agrupar e-mails de chamado muito ativo | se incomodar no uso real, segurar o e-mail alguns minutos e juntar o que chegar. Atrasar o aviso também custa (versão 1.2, acima) |
| Sincronização contínua com Freshdesk ou Freshservice | só faz sentido se as duas ferramentas forem conviver. Para migrar, a importação por arquivo resolve |
| Importação do GLPI | a origem já existe no modelo; falta o mapeamento das colunas da exportação |
| Leitura de caixa por IMAP | só se aparecer caixa fora do Microsoft 365 |
| Tabela de SLA editável pela interface | qual chamado o prazo novo alcança: só os abertos dali em diante, como nos feriados, é a resposta mais provável |
| Atribuição automática | por categoria, por rodízio ou por carga — e o que fazer com quem está ausente |
| Imagem do frontend com endereço de API em tempo de execução | só quando existir topologia com SPA e API em domínios separados ("Primeira execução", acima) |
| Base de conhecimento | artigos de solução escritos pela equipe e consultados pelo solicitante antes de abrir chamado. Falta decidir quem publica, se há artigo só para a equipe (a mesma fronteira do comentário interno) e se um chamado resolvido vira rascunho de artigo |
| Gerador de assinatura de e-mail | trazer para o portal o gerador que hoje existe separado, reformulado: a assinatura sai dos dados da própria conta (nome, cargo) mais o que for da empresa. Falta decidir quem mantém o modelo — provavelmente o administrador, nas configurações — e se a pessoa pode editar o resultado |

---

## 19. Critérios de sucesso da versão 1

A versão 1 será considerada concluída quando:

* um usuário conseguir se cadastrar e abrir chamado;
* um técnico conseguir visualizar e assumir chamado;
* um técnico conseguir responder e alterar status;
* um administrador conseguir visualizar todos os chamados;
* o sistema impedir acesso indevido por perfil;
* o chamado possuir histórico de alterações;
* o dashboard mostrar indicadores básicos;
* o projeto rodar localmente com Docker Compose;
* o README explicar como executar o sistema.
