# CLAUDE.md

Instruções para agentes de IA que trabalham neste repositório.

## O projeto

OpsDesk é um sistema interno de chamados de TI (service desk), com três perfis: Usuário, Técnico e Gestor. O domínio é pequeno e bem definido; a complexidade real está em três pontos, e é neles que o cuidado deve se concentrar:

1. **autorização por recurso** — a fronteira é entre equipe e solicitante: técnico e gestor enxergam todos os chamados, e o solicitante nunca vê chamado de terceiro, comentário interno nem anexo interno;
2. **máquina de estados do chamado** — sete status, toda transição auditada;
3. **SLA em horas úteis** — com pausa enquanto o chamado aguarda o solicitante.

## Estado atual

**O MVP da versão 1 está completo e rodando.** Os nove critérios de sucesso da seção 19 do README estão atendidos.

**Backend:** domínio, persistência com migration, SLA em horas úteis com pausa e retomada, auditoria automática por interceptor, seed de dados de referência, autenticação com refresh rotativo e troca de senha, instalação por linha de comando (`--migrate`, `--seed`, `--bootstrap-admin`, `--setup`), chamados (abertura, listagem paginada com filtros, detalhe), atendimento (comentários público e interno, máquina de estados, atribuição, histórico), dashboard por consultas agregadas e administração de usuários pelo gestor.

**Frontend:** login, cadastro, troca de senha, lista de chamados com filtros na URL, abertura, detalhe com comentários e histórico, ações de status e atribuição, dashboard com gráficos, e administração de usuários.

**Endpoints:**

```
GET    /health  /ready  /openapi/v1.json  /swagger
POST   /api/auth/{register,login,refresh,logout,password}   GET /api/auth/me
POST   /api/tickets                                    GET /api/tickets  (filtros, paginação)
GET    /api/tickets/{id}
GET    /api/tickets/{id}/comments    POST /api/tickets/{id}/comments
GET    /api/tickets/{id}/history
POST   /api/tickets/{id}/status      POST /api/tickets/{id}/assignment
POST   /api/tickets/{id}/classification   (prioridade e categoria; prioridade recalcula o SLA)
GET    /api/categories               GET  /api/staff        GET /api/dashboard
GET    /api/users  (equipe; busca por nome ou e-mail, para abrir em nome de outra pessoa)
GET    /api/admin/users           (gestor; ativos e inativos, filtros, paginação)
POST   /api/admin/users/{id}/role   POST /api/admin/users/{id}/activation
POST   /api/attachments              GET  /api/attachments/{id}
GET    /api/tickets/{id}/attachments
```

**Fora do escopo da versão 1, conforme o roadmap:** notificações, tela de administração de categorias (versão 1.1), ingestão de e-mail e caixa de SPAM (versão 2.0). Anexos e administração de usuários foram antecipados da 1.1 e já existem. O **primeiro** gestor sai do comando de bootstrap; os demais são promovidos por ele em `/usuarios`.

## Documentação

Cada assunto tem uma única fonte de verdade. Antes de responder sobre regra de negócio, leia o documento correspondente em vez de inferir do código.

| Arquivo | Assunto |
| --- | --- |
| `README.md` | escopo, perfis, status, prioridades, SLA, entidades, telas, roadmap |
| `docs/arquitetura.md` | stack, estrutura do repositório, decisões de arquitetura |
| `docs/integracao-email.md` | ingestão de e-mail e caixa de SPAM (versão 2.0) |

Se uma mudança de código contrariar um desses documentos, atualize o documento na mesma alteração. Documentação divergente do código é pior do que documentação ausente.

## Stack

**Backend:** .NET 10, ASP.NET Core Web API, EF Core + Npgsql, PostgreSQL, JWT com refresh token, FluentValidation, Serilog, xUnit + Testcontainers.

**Frontend:** React + TypeScript, Vite, React Router, TanStack Query, React Hook Form + Zod, shadcn/ui + Tailwind, TanStack Table, Recharts.

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

`--seed` nunca cria usuários de exemplo. O gestor do bootstrap nasce com `MustChangePassword`, e o bootstrap só age quando não existe nenhum gestor.

Quem esquecer o comando é acusado pelo `/ready`, que reprova migration pendente e ausência de dados de referência, com o que fazer no corpo da resposta. O `/health` continua verde nesse caso de propósito: ele decide reiniciar o contêiner, e reiniciar não aplica migration.

Ainda **não** implementado: feriados não se renovam sozinhos fora de Development. Ver README, seção 18, em "Primeira execução: instalar em branco".

### Backend

```bash
dotnet restore backend/OpsDesk.slnx
dotnet build backend/OpsDesk.slnx
dotnet test backend/OpsDesk.slnx                                  # unidade + integração
dotnet test backend/OpsDesk.slnx --filter FullyQualifiedName~Unit # só unidade, sem Docker
dotnet run --project backend/OpsDesk.Api                          # usa o Postgres do compose
```

Os testes de integração sobem PostgreSQL por Testcontainers e **exigem Docker rodando**.

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
npm run build
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

Testes de integração usam PostgreSQL real via Testcontainers. Não use o provider InMemory do EF Core: ele não reproduz sequences, `timestamptz` nem o comportamento transacional que o projeto depende.

## Armadilhas conhecidas

* O Npgsql rejeita `DateTime` com `Kind = Unspecified` em coluna `timestamptz`. Use `DateTimeOffset` ou `DateTime` em UTC explícito.
* **Trocar para `DateTimeOffset` não basta:** o Npgsql só aceita deslocamento **zero** em `timestamptz`. Um `DateTimeOffset` com `-03:00` é recusado na escrita, mesmo sendo o instante correto. Por isso o `IBusinessCalendar` faz a conta no fuso do expediente mas devolve o prazo em UTC. Há teste fixando os dois lados disso (`Npgsql_recusa_data_com_deslocamento_diferente_de_utc` e `O_prazo_volta_em_UTC_porque_e_assim_que_o_banco_aceita`).
* O expediente tem **dez** horas (08:00–18:00), então "1 dia útil" do README são 10 horas úteis, não 8. A tradução de dias para horas está no `DatabaseSeeder`, não espalhada.
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
* "Aguardando usuário" pausa o SLA de resolução, mas **não** o de resposta. Ver README, seção 8.2.
* Chamado cancelado fica fora dos indicadores de SLA.
* Prioridade nunca é inferida do texto do chamado, nem do assunto de um e-mail. A triagem é da equipe.
