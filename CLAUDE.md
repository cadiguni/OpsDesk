# CLAUDE.md

Instruções para agentes de IA que trabalham neste repositório.

## O projeto

OpsDesk é um sistema interno de chamados de TI (service desk), com três perfis: Usuário, Técnico e Gestor. O domínio é pequeno e bem definido; a complexidade real está em três pontos, e é neles que o cuidado deve se concentrar:

1. **autorização por recurso** — a fronteira é entre equipe e solicitante: técnico e gestor enxergam todos os chamados, e o solicitante nunca vê chamado de terceiro, comentário interno nem anexo interno;
2. **máquina de estados do chamado** — sete status, toda transição auditada;
3. **SLA em horas úteis** — com pausa enquanto o chamado aguarda o solicitante.

## Estado atual

**O roadmap até a versão 2.0 está entregue**, com compose de produção. Os nove critérios de sucesso da seção 19 do README estão atendidos desde a versão 1. O que segue em aberto está no fim da seção 18 do README, em "Em aberto".

**Backend:** domínio, persistência com migration, SLA em horas úteis com pausa e retomada, auditoria automática por interceptor (chamado e conta), seed de dados de referência e de demonstração, autenticação com refresh rotativo, login com a conta Microsoft (Entra ID), troca de senha, convite e "esqueci minha senha", instalação por linha de comando (`--migrate`, `--seed`, `--bootstrap-admin`, `--setup`, `--seed-demo`), chamados (abertura, listagem paginada com filtros e busca por trigramas, detalhe), atendimento (comentários público e interno, máquina de estados com reabertura, atribuição, histórico), dashboard por consultas agregadas (foto da fila e período), administração de usuários, categorias e feriados, configurações gerais, e-mail pelo Microsoft Graph nos dois sentidos com caixa de SPAM, importação de chamados, e anexos em disco ou no Azure Blob.

**Frontend:** login (com senha ou conta Microsoft), cadastro, troca e definição de senha, lista de chamados com filtros na URL, abertura, detalhe com comentários e histórico, ações de status (inclusive reabrir) e atribuição, dashboard com gráficos e período, notificações, e-mails recebidos e SPAM, e a área de administração (usuários, categorias, feriados, importação, configurações).

**Produção:** `deploy/` — compose com Caddy e HTTPS, `.env` gerado com segredos aleatórios, backup e restauração. Guia em `docs/instalacao.md`.

**Endpoints:**

```
GET    /health  /ready  /openapi/v1.json  /swagger
POST   /api/auth/{register,login,refresh,logout,password}   GET /api/auth/me   GET /api/auth/options (anônimo)
POST   /api/auth/password/forgot   POST /api/auth/password/reset   (anônimos; link por e-mail)
GET    /api/auth/microsoft/start   GET /api/auth/microsoft/callback   (anônimos; navegação, respondem com redirecionamento)
POST   /api/tickets                                    GET /api/tickets  (filtros, paginação)
GET    /api/tickets/{id}
GET    /api/tickets/{id}/comments    POST /api/tickets/{id}/comments
GET    /api/tickets/{id}/history
POST   /api/tickets/{id}/status      POST /api/tickets/{id}/assignment
POST   /api/tickets/{id}/classification   (prioridade e categoria; prioridade recalcula o SLA)
GET    /api/categories (?includeInactive, equipe)   GET /api/staff   GET /api/dashboard   GET /api/dashboard/period
GET    /api/users  (equipe; busca por nome ou e-mail, para abrir em nome de outra pessoa)
GET    /api/admin/users           (gestor; ativos e inativos, filtros, paginação)   POST /api/admin/users
POST   /api/admin/users/{id}/role   POST /api/admin/users/{id}/activation
POST   /api/admin/users/{id}/password-reset   GET /api/admin/users/{id}/history
GET    /api/admin/categories      POST /api/admin/categories   (gestor)
PUT    /api/admin/categories/{id}  POST /api/admin/categories/{id}/activation
GET    /api/notifications   GET /api/notifications/unread-count   (as do próprio usuário)
POST   /api/notifications/{id}/read   POST /api/notifications/read-all
GET    /api/admin/settings/email   PUT /api/admin/settings/email   POST /api/admin/settings/email/test   (gestor)
GET    /api/admin/settings/email/accounts   POST …/accounts   PUT …/accounts/{id}   (gestor; caixas monitoradas)
GET    /api/admin/settings/general   PUT /api/admin/settings/general   (gestor)
GET    /api/admin/holidays?year=   POST /api/admin/holidays   POST /api/admin/holidays/{id}/activation   DELETE /api/admin/holidays/{id}
POST   /api/admin/import/tickets?format=&dryRun=   (gestor; multipart, dryRun padrão true)
GET    /api/inbound   GET /api/inbound/{id}   POST /api/inbound/{id}/review   POST /api/inbound/{id}/reprocess   (equipe)
DELETE /api/inbound/{id} (gestor)   GET /api/inbound/rules   POST /api/inbound/rules/{id}/activation
POST   /api/attachments              GET  /api/attachments/{id}
GET    /api/tickets/{id}/attachments
```

O **primeiro** gestor sai do comando de bootstrap; os demais são cadastrados ou promovidos por ele em `/usuarios`.

## Documentação

Cada assunto tem uma única fonte de verdade. Antes de responder sobre regra de negócio, leia o documento correspondente em vez de inferir do código.

| Arquivo | Assunto |
| --- | --- |
| `README.md` | escopo, perfis, status, prioridades, SLA, entidades, telas, roadmap |
| `docs/arquitetura.md` | stack, estrutura do repositório, decisões de arquitetura |
| `docs/integracao-email.md` | ingestão de e-mail e caixa de SPAM (versão 2.0), e onde a implementação se afastou da especificação |
| `docs/instalacao.md` | instalação em produção, atualização, backup, Azure Blob, réplicas |
| `docs/atendimento-e-aparencia.md` | enviar e fechar, interface de atendimento, temas |

Se uma mudança de código contrariar um desses documentos, atualize o documento na mesma alteração. Documentação divergente do código é pior do que documentação ausente.

## Stack

**Backend:** .NET 10, ASP.NET Core Web API, EF Core + Npgsql, PostgreSQL, JWT com refresh token, FluentValidation, Serilog, xUnit + Testcontainers.

**Frontend:** React + TypeScript, Vite, React Router, TanStack Query, React Hook Form + Zod, shadcn/ui + Tailwind, TanStack Table, Recharts, Vitest + Testing Library.

**Não introduza** MediatR, AutoMapper nem repositório genérico sobre o EF Core. A escolha é deliberada e está justificada em `docs/arquitetura.md`, seção 4.9.

## Comandos

Pré-requisitos: .NET 10 SDK, Node 24 LTS (`frontend/.nvmrc`; a faixa aceita é `^20.19 || >=22.12`), Docker.

### Ambiente local

```bash
docker compose up -d          # PostgreSQL + API em http://localhost:8080
docker compose logs -f api
docker compose down           # -v também descarta o volume do banco e o dos anexos

# Perfil `web`: acrescenta o SPA compilado, servido por nginx com /api repassado para a
# API — tudo na mesma origem, em http://localhost:3000. É para **usar** o sistema; quem
# está mexendo no frontend continua no `npm run dev`, que tem recarga imediata.
docker compose --profile web up -d --build
```

A API aplica migration e roda o seed na subida, **apenas em Development**. Em outros ambientes o schema sobe como etapa explícita do deploy.

Fora de Development a instalação é um comando explícito, no mesmo executável da API:

```bash
docker compose run --rm   -e OpsDesk__Bootstrap__Email=voce@empresa.com   -e OpsDesk__Bootstrap__Password='uma-senha-provisoria'   api --setup            # = --migrate + --seed + --bootstrap-admin
```

`--seed` nunca cria usuários de exemplo. O gestor do bootstrap nasce com `MustChangePassword`, e o bootstrap só age quando não existe nenhum gestor. `--seed-demo` cria cinco pessoas e oito chamados, e recusa banco que já tem chamado.

Produção usa outro compose, com nome de projeto próprio (`opsdesk-prod`) para não compartilhar volume com este:

```bash
./deploy/new-env.sh localhost voce@empresa.com     # gera deploy/.env; no Windows, deploy/new-env.ps1
docker compose -f deploy/docker-compose.yml --env-file deploy/.env build
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile setup run --rm setup
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
bash deploy/backup.sh                               # e deploy/restore.sh <pasta>
```

Quem esquecer o comando é acusado pelo `/ready`, que reprova migration pendente e ausência de dados de referência, com o que fazer no corpo da resposta. O `/health` continua verde nesse caso de propósito: ele decide reiniciar o contêiner, e reiniciar não aplica migration.

Feriados nacionais se renovam por tarefa diária (`Holidays:RenewalIntervalHours`), em qualquer ambiente.

### Backend

```bash
dotnet restore backend/OpsDesk.slnx
dotnet build backend/OpsDesk.slnx
dotnet test backend/OpsDesk.slnx                                  # unidade + integração
dotnet test backend/OpsDesk.slnx --filter FullyQualifiedName~Unit # só unidade, sem Docker
dotnet run --project backend/OpsDesk.Api                          # usa o Postgres do compose
```

Os testes de integração sobem PostgreSQL por Testcontainers e **exigem Docker rodando**. Os de anexo no Azure Blob sobem também o Azurite.

### Migrations

`dotnet-ef` está fixado no manifesto de ferramentas; rode `dotnet tool restore` uma vez.

```bash
dotnet dotnet-ef migrations add NomeDescritivoNoImperativo \
  --project backend/OpsDesk.Infrastructure \
  --startup-project backend/OpsDesk.Api \
  --output-dir Persistence/Migrations

dotnet dotnet-ef migrations has-pending-model-changes \
  --project backend/OpsDesk.Infrastructure \
  --startup-project backend/OpsDesk.Api
```

O `OpsDeskDbContextFactory` atende as ferramentas de design-time. Para apontar para outro banco, defina `OPSDESK_MIGRATIONS_CONNECTION`.

### Frontend

```bash
cd frontend
npm ci
npm run dev        # http://localhost:5173
npm run typecheck
npm run lint
npm test           # Vitest + Testing Library, em jsdom
npm run build
```

### Ponta a ponta

Playwright contra o perfil `web` (porta 3000), com o seed de desenvolvimento:

```bash
docker compose --profile web up -d --build
cd e2e && npm ci && npx playwright install chromium
npm test                                # OPSDESK_URL troca o endereço
```

### Usuários de desenvolvimento

Criados pelo seed só em Development, senha `OpsDesk@123`:

| E-mail | Perfil |
| --- | --- |
| `gestor@opsdesk.local` | Gestor |
| `tecnico@opsdesk.local` | Técnico |
| `usuario@opsdesk.local` | Usuário |

## Invariantes

Estas regras não são preferências de estilo. Quebrá-las é bug, e em alguns casos é vazamento de dado.

1. **O filtro de visibilidade é aplicado na query.** Toda leitura de chamado passa pelo filtro derivado do usuário autenticado, no `IQueryable`, antes da projeção. Nunca confie em atributo de rota ou em condicional na interface para esconder chamado de terceiro. O corte é por `TicketViewer.IsStaff` — equipe vê tudo, solicitante vê o próprio — e qualquer perfil desconhecido cai no caso restrito.
2. **Comentário com `IsInternal = true` nunca chega ao solicitante.** Não em resposta de API, não em e-mail, não em notificação. Qualquer endpoint novo que retorne comentários precisa de teste cobrindo esse caso. **Vale igual para anexo:** o arquivo de uma nota interna é tão restrito quanto o texto dela — some da listagem e o download direto responde 404.
3. **`TicketHistory` é append-only.** Nada de `Update` nem `Delete`. O preenchimento é feito pelo interceptor do EF Core, não espalhado pelos serviços.
4. **Datas em UTC, colunas `timestamptz`.** Conversão para `America/Sao_Paulo` só na interface e dentro do cálculo de horas úteis.
5. **`Ticket.Code` vem da sequence do PostgreSQL.** Nunca de `COUNT`, `MAX` ou contador em memória.
6. **Cálculo de horas úteis mora em `IBusinessCalendar`.** Não replique a lógica em serviço nenhum.
7. **Schema muda por migration do EF Core.** Sem `EnsureCreated`, sem SQL manual no banco.
8. **Senha usa `PasswordHasher<T>`.** Nunca hash artesanal, nunca MD5 ou SHA sem KDF.
9. **Anexo nasce pendente, e é o vínculo que define a visibilidade.** Enviar cria um anexo sem chamado, visível só para quem enviou; a abertura ou o comentário é que o vincula e copia o `IsInternal`. Nunca vincule anexo enviado por outra pessoa, e nunca torne um anexo visível antes de saber a que comentário ele pertence.
10. **Senha provisória só serve para trocar a si mesma.** Enquanto `User.MustChangePassword` for verdadeiro, nenhuma rota fora de `/api/auth` responde — a trava é o middleware `UsePasswordChangeRequired`, e não um atributo por rota, para que endpoint novo nasça coberto. Não a substitua por verificação rota a rota nem a contorne em serviço: a credencial do bootstrap chega por variável de ambiente, e é essa trava que a faz valer uma vez em vez de para sempre.
11. **Aviso de chamado nasce do dado, não do serviço.** Notificação e e-mail são gerados pelo `TicketNotificationInterceptor`, na mesma transação da mudança, e a regra de quem recebe o quê mora no `NotificationPlanner`. Não chame envio de e-mail de dentro de serviço: endpoint novo que grave comentário, status ou responsável já notifica sozinho. E-mail só para o solicitante e **nunca** com nota interna — há teste fixando os dois (`Nota_interna_nunca_vira_email`, `Nota_interna_nao_entra_no_email_de_outro_evento_da_mesma_gravacao`).
12. **Credencial guardada no banco é só de escrita e cifrada.** O client secret do e-mail passa pelo `ISecretProtector` (Data Protection) e nenhuma resposta da API o devolve. Configuração nova com credencial segue o mesmo desenho.
13. **Só Portal e E-mail entram no SLA.** Chamado importado (CSV, API externa, Freshdesk, Freshservice) fica fora de vencidos, dashboard e alertas. Toda consulta de indicador de SLA filtra por `TicketSla.MeasuredSources`, junto com o corte de cancelado; consulta nova também. Na interface, o espelho é `slaMeasuredSources`.
14. **Link de senha é credencial.** Só o hash do token vai para o banco, e o corpo do e-mail que leva o link é apagado da fila depois do envio (`OutboundEmail.IsSensitive`). E-mail novo com link de acesso segue o mesmo caminho.

## Convenções

* **Idioma:** código, nomes de tipo, propriedades e commits em inglês; documentação, mensagens ao usuário e rótulos da interface em português.
* **Nomes do domínio** seguem o README: `Ticket`, `TicketComment`, `TicketHistory`, `Category`, `SlaPolicy`, `User`. Não invente sinônimos como `Call`, `Issue` ou `Request`.
* **Enums** (`TicketStatus`, `TicketPriority`, `UserRole`, `TicketSource`) são persistidos como string, não como inteiro, para que o banco continue legível.
* **Paginação e filtro** sempre no backend, com limite máximo de página.
* **Dashboard** usa consultas agregadas dedicadas. Não carregue chamados para contar em memória.
* **Migrations** recebem nome descritivo em inglês, no imperativo: `AddTicketSlaPauseColumns`.

## O que precisa de teste

Antes de considerar uma alteração concluída, verifique se ela tem cobertura quando toca:

* transições da máquina de estados;
* regras de autorização, inclusive os casos negativos (o que cada perfil **não** pode ver);
* cálculo de SLA, com pausa, retomada, virada de expediente, fim de semana e feriado;
* ausência de comentário interno nas respostas destinadas ao solicitante;
* a trava de senha provisória, inclusive o caso negativo: a rota nova recusa enquanto `MustChangePassword` estiver de pé?
* entrada vinda de fora (e-mail recebido, arquivo importado): processar a mesma coisa duas vezes não pode criar dois chamados.

No frontend, os testes de componente ficam ao lado do arquivo testado (`*.test.tsx`) e montam a tela com `renderWithProviders`, de `src/test/render.tsx`, que injeta a sessão pelo contexto e simula a API com `vi.mock('@/features/tickets/api')`. Mudança que toque marcação de interno, filtros na URL ou ações de status precisa de teste ali.

Testes de integração usam PostgreSQL real via Testcontainers. Não use o provider InMemory do EF Core: ele não reproduz sequences, `timestamptz` nem o comportamento transacional que o projeto depende.

## Armadilhas conhecidas

* O Npgsql rejeita `DateTime` com `Kind = Unspecified` em coluna `timestamptz`. Use `DateTimeOffset` ou `DateTime` em UTC explícito.
* **Trocar para `DateTimeOffset` não basta:** o Npgsql só aceita deslocamento **zero** em `timestamptz`. Um `DateTimeOffset` com `-03:00` é recusado na escrita, mesmo sendo o instante correto. Por isso o `IBusinessCalendar` faz a conta no fuso do expediente mas devolve o prazo em UTC. Há teste fixando os dois lados disso (`Npgsql_recusa_data_com_deslocamento_diferente_de_utc` e `O_prazo_volta_em_UTC_porque_e_assim_que_o_banco_aceita`).
* O expediente tem **dez** horas (08:00–18:00), então "1 dia útil" do README são 10 horas úteis, não 8. A tradução de dias para horas está no `DatabaseSeeder`, não espalhada.
* **O seed de categorias só age em banco sem nenhuma.** Comparar por nome, como antes, recriava a categoria que o gestor renomeou — a cada subida em Development e a cada `--seed` em produção. Há teste fixando isso (`Seed_nao_ressuscita_categoria_renomeada_nem_reativa_desativada`).
* A imagem `postgres:18` quer o volume montado em `/var/lib/postgresql`, não em `/var/lib/postgresql/data`. Montar no caminho antigo faz o container recusar a subida.
* **Rotação de refresh token de uso único é estrita demais sem janela de tolerância.** Duas abas recarregando, ou o `StrictMode` do React executando o efeito duas vezes, produzem duas renovações concorrentes com o mesmo cookie — e a segunda parece reuso. `JwtOptions.RefreshTokenGraceSeconds` cobre isso no servidor, e no cliente `refreshSession` garante uma renovação por vez. Os dois lados são necessários: um sozinho não resolve.
* **O `UseRateLimiter` vem depois do `UseAuthentication`.** A cota de envio de anexo é por usuário, e a partição só lê a claim do token depois que a autenticação preencheu o `context.User`. Com o limitador antes, toda requisição autenticada cai na partição de fallback por IP — num escritório atrás de NAT isso é uma cota única para a empresa, e na suíte de testes era uma cota única para todos os testes, que passaram a falhar em bloco com 429. Há teste fixando os dois lados (`UploadRateLimitTests`).
* **A janela de tolerância do refresh vale para rotação, nunca para revogação deliberada.** O que distingue as duas no banco é `ReplacedByTokenHash`: rotação preenche esse campo junto com `RevokedAt`, e logout, troca de senha, conta desativada e o corte por reuso detectado preenchem só o `RevokedAt`. Sem essa separação a tolerância anula a revogação — quem troca a senha para cortar um intruso vê o navegador dele renovar a sessão segundos depois. O teste do logout não pegava isso porque o logout também apaga o cookie, e a renovação seguinte ia sem token nenhum: o 401 vinha pelo motivo errado. Há teste apresentando o token guardado de propósito (`Token_revogado_no_logout_nao_ganha_a_janela_de_tolerancia`).
* **Não misture cota de rate limiting entre login e renovação de sessão.** A interface renova a cada carregamento de página; com cota compartilhada, recarregar algumas vezes gastava as tentativas de credencial e o login legítimo recebia 429 na primeira tentativa.
* **Corpo de requisição malformado estoura como `BadHttpRequestException`** e, sem tratamento, sai como 500 — o que significa "defeito nosso" e alimenta alerta de produção. O `MalformedRequestHandler` traduz para 400.
* **O JSON da API serializa enum como texto**, configurado por `ConfigureHttpJsonOptions`. Sem isso o padrão é inteiro, e `role: 1` chega ao frontend onde o TypeScript espera `'Technician'` — o rótulo sai vazio e nada acusa o erro em tempo de compilação. Há teste de contrato fixando isso.
* **Ler `IConfiguration` durante a montagem do pipeline congela o valor daquele instante.** Fontes registradas depois — como as de um `WebApplicationFactory` em teste — são ignoradas em silêncio. Por isso `JwtOptions`, `RateLimitOptions` e a connection string são resolvidos por DI, e não lidos direto no `Program.cs`.
* **Duas formas de servir o SPA, duas portas.** O dev server fica na 5173 e o contêiner do perfil `web` na 3000. Não unifique: com um `npm run dev` rodando, o Docker Desktop no Windows publica a porta ocupada **sem erro**, os dois escutam, o dev server atende, e o contêiner parece no ar servindo conteúdo que não é o dele — diagnosticar isso custa mais que a porta extra.
* **A imagem do frontend é construída com `VITE_API_URL=/`**, para o cliente chamar caminho relativo e o nginx repassar `/api`. String vazia também funcionaria, mas o cliente resolve a base com `??`, que só cai no padrão em valor nulo: com `""` o literal do padrão continua no bundle e qualquer verificação textual passa a mentir sobre o que foi embutido.
* O cookie de refresh é `Secure`. Chrome e Firefox aceitam cookie `Secure` sobre HTTP em `localhost`; **o Safari não**. Em Mac com Safari, a sessão não sobrevive ao reload em desenvolvimento.
* Os binários nativos do Vite e do oxlint declaram `engines: ^20.19 || >=22.12`. Em Node fora dessa faixa o npm **omite o binário em silêncio**: `npm install` termina com sucesso e o build estoura por binding ausente. O `frontend/.npmrc` liga `engine-strict` justamente para transformar isso em erro na instalação.
* **Volume do Docker herda o dono do caminho que existir na imagem.** A API roda como usuário sem privilégio (`USER $APP_UID`), e sem o `mkdir -p /var/opsdesk/attachments && chown` no Dockerfile o volume nasce pertencendo ao root: o envio de anexo falha com `UnauthorizedAccessException: Permission denied`. Só no container — `dotnet run` e os testes gravam em pasta do próprio usuário e passam. Trocar a raiz dos anexos exige repetir o `mkdir` com o dono certo, e recriar o volume (`docker compose down -v`), porque o dono é fixado no primeiro uso.
* **`api.post` com `FormData` precisa de `'Content-Type': undefined`.** O cliente axios tem `application/json` como padrão; mantido, o multipart vai sem `boundary` e o servidor recusa. Remover o cabeçalho deixa o navegador montá-lo.
* **O cookie do fluxo do login Microsoft é `SameSite=Lax`, e o retorno é `response_mode=query`.** O retorno do Entra ID é navegação vinda de outro site: cookie Strict, como o de refresh, não chega, e `form_post` seria POST entre sites, que nem o Lax acompanha. Sem o cookie todo retorno é recusado como "expirou". E o erro volta a `/entrar`, que é a rota do login no SPA, e não a `/login`. Ver `docs/arquitetura.md`, 4.25.
* "Aguardando usuário" pausa o SLA de resolução, mas **não** o de resposta. Ver README, seção 8.2.
* **Filtro de data chega com `-03:00` e precisa virar UTC antes da query.** Mesma recusa do Npgsql de deslocamento diferente de zero, agora em parâmetro de consulta: sem o `ToUniversalTime()` em `TicketFilterExtensions`, filtrar por período respondia 500. Há teste que falha sem a conversão (`Periodo_com_deslocamento_de_sao_paulo_respeita_a_virada_do_dia_local`).
* **Instalar Vitest junto com o resto num só `npm install` falha** com `ERESOLVE` (`Found: vite@undefined`), por um ciclo de peer opcional entre `vitest` e `@vitest/browser-playwright`. Instalar o `vitest` sozinho primeiro resolve.
* **Alerta de SLA compara prazo, não chamado.** O registro de alerta dado (`SlaAlert`) tem o prazo na chave única, para que prazo movido — pausa, reabertura, reclassificação — possa alertar de novo. Vencimento com mais de 24 horas não alerta: sem esse corte, a primeira verificação depois de um deploy, ou de a tarefa ficar parada, avisaria de uma vez todos os chamados vencidos da história.
* **As chaves do Data Protection precisam de volume persistente.** Elas cifram o client secret do e-mail. Sem `DataProtection:KeysPath` apontando para um volume, cada deploy gera chaves novas, o secret gravado vira ilegível e o e-mail para — sem erro, a não ser a mensagem na tela de configuração dias depois. Por isso `KeysPath` é obrigatório (a API não sobe sem ele), a subida grava um arquivo de sonda na pasta, e o Dockerfile cria `/var/opsdesk/keys` com o dono certo, como faz com os anexos.
* **`WebUtility.HtmlEncode` transforma acento em entidade** (`Concluído` vira `Conclu&#237;do`). É HTML válido e todo cliente de e-mail exibe normalmente, mas teste que procura texto em português no corpo do e-mail precisa decodificar antes.
* **Reabrir retoma o SLA, não o recomeça.** O tempo útil entre a resolução e a reabertura entra como pausa, e `ResolvedAt`/`ClosedAt` só são limpos depois disso — a retomada precisa da data de resolução. A janela de sete dias para chamado fechado mora em `ReopenPolicy`, não no grafo. Ver README, seção 5.9.
* Chamado cancelado fica fora dos indicadores de SLA.
* Prioridade nunca é inferida do texto do chamado, nem do assunto de um e-mail. A triagem é da equipe.
* **A busca precisa continuar como `EF.Functions.Like(coluna.ToLower(), padrão, "\\")`.** É exatamente a expressão dos índices de trigramas da migration `AddTicketSearchTrigramIndexes`. Trocar por `Contains` vira `strpos`, que nenhum índice atende, e a busca volta a varrer a tabela sem erro nenhum. Há teste conferindo o plano de execução (`TicketSearchTests`).
* **Processamento em segundo plano age como alguém pelo `IActorScope`.** O e-mail recebido grava comentário em nome do remetente, para histórico, notificação e visibilidade valerem como no portal. A troca vale para a instância do escopo, e cada mensagem roda num escopo próprio. Fora de um escopo dedicado, a troca contaminaria a requisição HTTP que compartilha o `ICurrentUser`.
* **Criar chamado não notifica, e a importação depende disso.** O `TicketNotificationInterceptor` ignora chamado `Added`. Mudar isso faria uma importação de cinco mil linhas mandar cinco mil e-mails a solicitantes de chamados de anos atrás.
* **O `sendMail` do Graph não aceita `In-Reply-To` nem `References`**, só cabeçalho `x-`. A resposta por e-mail volta para o chamado pelo código `[OPS-000123]` no assunto. Não remova o código do assunto dos avisos.
* **Fila de saída com várias réplicas: a reserva é um `ExecuteUpdate` condicional** em `NextAttemptAt`. Selecionar e depois marcar, em dois passos, manda o mesmo e-mail duas vezes — o teste concorrente mandava 9 em vez de 5.
* **O Azurite recusa a versão de API do SDK mais novo** com 400. Os testes sobem o emulador com `--skipApiVersionCheck`. Connection string em `AttachmentStorage:AzureBlob` só é aceita para o emulador (`AccountName=devstoreaccount1` ou `UseDevelopmentStorage=true`): conta de verdade usa `ServiceUri` e identidade.
* **A CSP do Caddy bloqueia script inline.** O tema é aplicado antes do React por `public/theme-init.js`, e não por `<script>` no `index.html`. Script inline novo some em produção e funciona no `npm run dev`, que não passa pelo Caddy.
* **No Alpine, `localhost` resolve primeiro para `::1`.** O nginx do SPA escuta também em `[::]:80`, e os healthchecks usam `127.0.0.1`. Sem isso o contêiner fica `unhealthy` com o nginx respondendo normalmente pelo IPv4.
* **Script de deploy no Git Bash:** argumento que começa com `/` vira caminho do Windows antes de chegar ao `docker`. Os scripts exportam `MSYS_NO_PATHCONV=1` e usam `pwd -W` para o caminho do volume. E `set -o pipefail` com `head` sai com 141 (SIGPIPE): o `new-env.sh` desliga o pipefail só na função que sorteia os segredos.
* **Captura de tela do dashboard com `fullPage` reinicia a animação do Recharts** e sai com as barras vazias. O teste de ponta a ponta usa viewport alto em vez de `fullPage`.
* **Input de arquivo com `required` reprova no jsdom** mesmo depois do `userEvent.upload`: a validação nativa não enxerga o arquivo simulado, e o `submit` nunca dispara. A tela de importação controla o botão pelo estado em vez de `required`.
* **O EF loga ERR ao checar migration pendente em banco vazio**, porque a tabela de histórico ainda não existe. Os comandos de instalação chamam `CreateIfNotExistsAsync` do `IHistoryRepository` antes — log de erro numa instalação bem-sucedida faz quem instala procurar problema onde não há.
