# CLAUDE.md

Instruções para agentes de IA que trabalham neste repositório.

## O projeto

OpsDesk é um sistema interno de chamados de TI (service desk), com três perfis: Usuário, Técnico e Gestor. O domínio é pequeno e bem definido; a complexidade real está em três pontos, e é neles que o cuidado deve se concentrar:

1. **autorização por recurso** — solicitante nunca vê chamado de terceiro nem comentário interno;
2. **máquina de estados do chamado** — sete status, toda transição auditada;
3. **SLA em horas úteis** — com pausa enquanto o chamado aguarda o solicitante.

## Estado atual

**O MVP da versão 1 está completo e rodando.** Os nove critérios de sucesso da seção 19 do README estão atendidos.

**Backend:** domínio, persistência com migration, SLA em horas úteis com pausa e retomada, auditoria automática por interceptor, seed de dados de referência, autenticação com refresh rotativo, chamados (abertura, listagem paginada com filtros, detalhe), atendimento (comentários público e interno, máquina de estados, atribuição, histórico) e dashboard por consultas agregadas.

**Frontend:** login, cadastro, lista de chamados com filtros na URL, abertura, detalhe com comentários e histórico, ações de status e atribuição, e dashboard com gráficos.

**Endpoints:**

```
GET    /health  /openapi/v1.json  /swagger
POST   /api/auth/{register,login,refresh,logout}      GET /api/auth/me
POST   /api/tickets                                    GET /api/tickets  (filtros, paginação)
GET    /api/tickets/{id}
GET    /api/tickets/{id}/comments    POST /api/tickets/{id}/comments
GET    /api/tickets/{id}/history
POST   /api/tickets/{id}/status      POST /api/tickets/{id}/assignment
GET    /api/categories               GET  /api/staff        GET /api/dashboard
```

**Fora do escopo da versão 1, conforme o roadmap:** anexos, notificações, telas de administração de categorias e de usuários (versão 1.1), ingestão de e-mail e caixa de SPAM (versão 2.0). Perfis de técnico e gestor são definidos pelo seed ou direto no banco — não há tela para promover usuário.

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
docker compose down           # -v também descarta o volume do banco
```

A API aplica migration e roda o seed na subida, **apenas em Development**. Em outros ambientes o schema sobe como etapa explícita do deploy.

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

1. **O filtro de visibilidade é aplicado na query.** Toda leitura de chamado passa pelo filtro derivado do usuário autenticado, no `IQueryable`, antes da projeção. Nunca confie em atributo de rota ou em condicional na interface para esconder chamado de terceiro.
2. **Comentário com `IsInternal = true` nunca chega ao solicitante.** Não em resposta de API, não em e-mail, não em notificação. Qualquer endpoint novo que retorne comentários precisa de teste cobrindo esse caso.
3. **`TicketHistory` é append-only.** Nada de `Update` nem `Delete`. O preenchimento é feito pelo interceptor do EF Core, não espalhado pelos serviços.
4. **Datas em UTC, colunas `timestamptz`.** Conversão para `America/Sao_Paulo` só na interface e dentro do cálculo de horas úteis.
5. **`Ticket.Code` vem da sequence do PostgreSQL.** Nunca de `COUNT`, `MAX` ou contador em memória.
6. **Cálculo de horas úteis mora em `IBusinessCalendar`.** Não replique a lógica em serviço nenhum.
7. **Schema muda por migration do EF Core.** Sem `EnsureCreated`, sem SQL manual no banco.
8. **Senha usa `PasswordHasher<T>`.** Nunca hash artesanal, nunca MD5 ou SHA sem KDF.

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
* ausência de comentário interno nas respostas destinadas ao solicitante.

Testes de integração usam PostgreSQL real via Testcontainers. Não use o provider InMemory do EF Core: ele não reproduz sequences, `timestamptz` nem o comportamento transacional que o projeto depende.

## Armadilhas conhecidas

* O Npgsql rejeita `DateTime` com `Kind = Unspecified` em coluna `timestamptz`. Use `DateTimeOffset` ou `DateTime` em UTC explícito.
* **Trocar para `DateTimeOffset` não basta:** o Npgsql só aceita deslocamento **zero** em `timestamptz`. Um `DateTimeOffset` com `-03:00` é recusado na escrita, mesmo sendo o instante correto. Por isso o `IBusinessCalendar` faz a conta no fuso do expediente mas devolve o prazo em UTC. Há teste fixando os dois lados disso (`Npgsql_recusa_data_com_deslocamento_diferente_de_utc` e `O_prazo_volta_em_UTC_porque_e_assim_que_o_banco_aceita`).
* O expediente tem **dez** horas (08:00–18:00), então "1 dia útil" do README são 10 horas úteis, não 8. A tradução de dias para horas está no `DatabaseSeeder`, não espalhada.
* A imagem `postgres:18` quer o volume montado em `/var/lib/postgresql`, não em `/var/lib/postgresql/data`. Montar no caminho antigo faz o container recusar a subida.
* **Rotação de refresh token de uso único é estrita demais sem janela de tolerância.** Duas abas recarregando, ou o `StrictMode` do React executando o efeito duas vezes, produzem duas renovações concorrentes com o mesmo cookie — e a segunda parece reuso. `JwtOptions.RefreshTokenGraceSeconds` cobre isso no servidor, e no cliente `refreshSession` garante uma renovação por vez. Os dois lados são necessários: um sozinho não resolve.
* **Não misture cota de rate limiting entre login e renovação de sessão.** A interface renova a cada carregamento de página; com cota compartilhada, recarregar algumas vezes gastava as tentativas de credencial e o login legítimo recebia 429 na primeira tentativa.
* **Corpo de requisição malformado estoura como `BadHttpRequestException`** e, sem tratamento, sai como 500 — o que significa "defeito nosso" e alimenta alerta de produção. O `MalformedRequestHandler` traduz para 400.
* **O JSON da API serializa enum como texto**, configurado por `ConfigureHttpJsonOptions`. Sem isso o padrão é inteiro, e `role: 1` chega ao frontend onde o TypeScript espera `'Technician'` — o rótulo sai vazio e nada acusa o erro em tempo de compilação. Há teste de contrato fixando isso.
* **Ler `IConfiguration` durante a montagem do pipeline congela o valor daquele instante.** Fontes registradas depois — como as de um `WebApplicationFactory` em teste — são ignoradas em silêncio. Por isso `JwtOptions`, `RateLimitOptions` e a connection string são resolvidos por DI, e não lidos direto no `Program.cs`.
* O cookie de refresh é `Secure`. Chrome e Firefox aceitam cookie `Secure` sobre HTTP em `localhost`; **o Safari não**. Em Mac com Safari, a sessão não sobrevive ao reload em desenvolvimento.
* Os binários nativos do Vite e do oxlint declaram `engines: ^20.19 || >=22.12`. Em Node fora dessa faixa o npm **omite o binário em silêncio**: `npm install` termina com sucesso e o build estoura por binding ausente. O `frontend/.npmrc` liga `engine-strict` justamente para transformar isso em erro na instalação.
* "Aguardando usuário" pausa o SLA de resolução, mas **não** o de resposta. Ver README, seção 8.2.
* Chamado cancelado fica fora dos indicadores de SLA.
* Prioridade nunca é inferida do texto do chamado, nem do assunto de um e-mail. A triagem é da equipe.
