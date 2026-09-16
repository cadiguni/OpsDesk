# Arquitetura — OpsDesk

Este documento registra a stack, a estrutura do repositório e as decisões de arquitetura do OpsDesk, com as justificativas de cada uma. As regras de negócio e o escopo estão no [README.md](../README.md).

---

## 1. Visão geral

O OpsDesk é um monorepo com dois artefatos executáveis e um banco relacional:

```
opsdesk/
├── backend/             API ASP.NET Core (OpsDesk.slnx)
├── frontend/            SPA React + Vite
├── docs/                documentação técnica
├── .github/workflows/   CI
├── docker-compose.yml   Postgres + API (+ Mailpit a partir da 2.0)
├── dotnet-tools.json    dotnet-ef fixado por versão
└── README.md            especificação funcional
```

Não há microsserviços, fila ou cache distribuído na versão 1. O volume de um service desk interno não justifica, e cada peça adicional é uma peça a mais para operar.

---

## 2. Stack

### Backend

| Camada | Escolha |
| --- | --- |
| Runtime | .NET 10 (LTS) |
| API | ASP.NET Core Web API |
| ORM | Entity Framework Core + Npgsql |
| Banco | PostgreSQL |
| Autenticação | JWT de acesso, com refresh token |
| Hash de senha | `PasswordHasher<T>` do ASP.NET Core Identity |
| Validação | FluentValidation |
| Documentação da API | Swagger / OpenAPI |
| Logs | Serilog, em formato estruturado |
| Testes | xUnit, `WebApplicationFactory`, Testcontainers |

### Frontend

| Camada | Escolha |
| --- | --- |
| Framework | React + TypeScript |
| Build | Vite |
| Rotas | React Router |
| Estado de servidor | TanStack Query |
| Formulários | React Hook Form + Zod |
| UI | shadcn/ui + Tailwind CSS |
| Tabelas | TanStack Table |
| Gráficos | Recharts |
| HTTP | Axios, com tipos gerados do OpenAPI |

Não há biblioteca de estado global. O que o TanStack Query guarda é cache de servidor, e o pouco de estado realmente local cabe em `useState` e na URL.

---

## 3. Estrutura do backend

```
backend/
├── OpsDesk.Api/              endpoints, autenticação, middlewares, DI
│   ├── Authentication/       JwtOptions, ICurrentUser sobre HttpContext
│   └── Authorization/        políticas por perfil
├── OpsDesk.Domain/           entidades, enums, regras invariantes
│   ├── Common/               interfaces de timestamp
│   ├── Entities/
│   ├── Enums/
│   └── Tickets/              máquina de estados
├── OpsDesk.Application/      serviços de caso de uso, DTOs, validators
│   ├── Abstractions/         IClock, IBusinessCalendar, ICurrentUser
│   ├── Authorization/        filtro de visibilidade no IQueryable
│   ├── Common/               paginação
│   └── Sla/                  SlaClock
├── OpsDesk.Infrastructure/   DbContext, migrations, interceptors, integrações
│   ├── Persistence/
│   │   ├── Configurations/
│   │   ├── Interceptors/
│   │   ├── Migrations/
│   │   └── Seed/
│   └── Time/                 BusinessCalendar, feriados, relógio
└── OpsDesk.Tests/
    ├── Unit/                 domínio, SLA, horas úteis, visibilidade
    └── Integration/          PostgreSQL real via Testcontainers
```

Quatro projetos são o suficiente. A regra prática: se uma classe nova não tem onde morar entre esses quatro, o problema provavelmente é a classe, não a estrutura.

---

## 4. Decisões de arquitetura

### 4.1 Autorização é aplicada na query, não no endpoint

Toda leitura de chamados passa por um filtro derivado do usuário autenticado, aplicado no `IQueryable` antes de qualquer projeção:

* **Usuário**: apenas chamados em que ele é o solicitante;
* **Técnico**: chamados sem responsável e chamados atribuídos a ele;
* **Gestor**: todos.

Comentários internos (`IsInternal = true`) são removidos na mesma camada quando o solicitante é o requisitante do chamado.

**Por quê:** se a proteção viver em atributo de rota, basta alguém criar um endpoint novo e esquecer o atributo para vazar chamado de terceiro. Amarrando ao acesso aos dados, o caminho inseguro deixa de existir.

### 4.2 Histórico é gravado por interceptor do EF Core

`TicketHistory` é preenchido por um `SaveChangesInterceptor` que compara as entradas modificadas de `Ticket` e gera um registro por campo relevante alterado, com valor anterior, valor novo, autor e instante.

**Por quê:** o requisito de auditoria do README é absoluto. Depender de cada serviço lembrar de chamar `historico.Add(...)` transforma auditoria em algo que funciona até a primeira distração.

O histórico é *append-only*: nunca é atualizado nem excluído por código de aplicação.

### 4.3 `Ticket.Code` vem de uma sequence do PostgreSQL

O código legível (`OPS-000123`) é gerado por uma sequence do banco, lida na mesma transação da inserção.

**Por quê:** `COUNT(*) + 1` ou `MAX(id) + 1` produzem código duplicado sob concorrência, e chamado com código repetido é um problema que só aparece em produção.

### 4.4 Tempo é sempre UTC no banco

Todas as colunas de data e hora são `timestamptz`, gravadas em UTC. A conversão para `America/Sao_Paulo` acontece na interface. O cálculo de horas úteis converte explicitamente para o fuso do expediente antes de contar.

**Por quê:** o Npgsql é rígido com `DateTime.Kind` e rejeita `Unspecified` em `timestamptz`. Definir a regra antes da primeira migration evita uma refatoração de dados depois.

Há uma sutileza que só aparece na primeira gravação: trocar `DateTime` por `DateTimeOffset` **não** resolve sozinho. O Npgsql aceita apenas deslocamento zero em `timestamptz` — um `DateTimeOffset` com `-03:00` é recusado na escrita, mesmo representando o instante correto. Por isso o `IBusinessCalendar` faz a conta no fuso do expediente e devolve o prazo em UTC, e não em horário local. Os dois lados dessa regra têm teste.

### 4.5 Horas úteis ficam isoladas em `IBusinessCalendar`

Uma única abstração responde: "somando N horas úteis a partir deste instante, qual é o prazo?" e "quantos minutos úteis existem entre A e B?". Expediente e feriados são dados, não código.

**Por quê:** é a única parte genuinamente complicada da versão 1 e a que mais gera bug silencioso. Isolada, ela é coberta por testes de unidade em cima de casos conhecidos: virada de expediente, fim de semana, feriado emendado, chamado aberto às 17:59.

### 4.6 JWT curto, refresh em cookie `httpOnly`

O token de acesso é curto e trafega em `Authorization`. O refresh token vive em cookie `httpOnly`, `Secure` e `SameSite=Strict`, é rotacionado a cada uso e revogável no banco.

**Por quê:** token de acesso em `localStorage` é legível por qualquer XSS. O sistema ser interno reduz a exposição, não o impacto.

### 4.7 Tipos do frontend são gerados do OpenAPI

Os tipos de requisição e resposta são gerados a partir do schema OpenAPI da API, não escritos à mão.

**Por quê:** DTO duplicado nos dois lados diverge silenciosamente, e a divergência aparece como bug de runtime em vez de erro de compilação.

### 4.8 Testes de integração usam PostgreSQL real

Os testes de integração sobem um PostgreSQL efêmero com Testcontainers e exercitam a API por `WebApplicationFactory`.

**Por quê:** o provider InMemory do EF Core não tem as semânticas que este projeto usa, entre elas sequences, `timestamptz` e comportamento transacional. Teste que passa nele e falha no Postgres é pior do que não ter teste.

Enquanto não há endpoint, os testes de integração exercitam o `DbContext` com os mesmos interceptors da API — é o que prova sequence, `timestamptz`, índice único e histórico automático. Os testes por `WebApplicationFactory` entram junto com os endpoints.

O que precisa de cobertura obrigatória:

* cada transição da máquina de estados do chamado;
* cada regra de autorização por perfil, inclusive os casos negativos;
* o cálculo de SLA, incluindo pausa e retomada;
* a ausência de comentário interno em toda resposta destinada ao solicitante.

### 4.9 Sem MediatR, AutoMapper ou repositório genérico

Casos de uso são classes de serviço injetadas por DI. O mapeamento para DTO é feito com `Select` direto no `IQueryable`. O acesso a dados usa o `DbContext`.

**Por quê:** MediatR e AutoMapper passaram a licença comercial e, mesmo antes disso, resolviam problemas de escala que este projeto não tem. A projeção manual ainda gera SQL melhor, porque só traz as colunas usadas. O `DbContext` já é Unit of Work e já expõe `IQueryable`.

### 4.10 Nomes do banco em snake_case

Tabelas e colunas usam `snake_case`, aplicado pela convenção do `EFCore.NamingConventions`. As classes e propriedades continuam em `PascalCase`.

**Por quê:** é a convenção do PostgreSQL, e identificador em `PascalCase` no Postgres obriga a citar tudo entre aspas em qualquer consulta manual — `SELECT "AssignedTechnicianId" FROM "Tickets"`. É o mesmo motivo de persistir enum como string: o banco precisa continuar legível para quem for investigar um chamado às duas da manhã.

### 4.11 Migrations desde o primeiro commit

O schema evolui exclusivamente por migrations do EF Core, versionadas no repositório. Nada de alteração manual no banco, nem de `EnsureCreated`.

---

## 5. Requisitos não funcionais na prática

| Requisito | Como é atendido |
| --- | --- |
| Paginação | paginação por cursor ou offset sempre no backend, com limite máximo de página |
| Filtros | aplicados em SQL, nunca em memória |
| Dashboard | consultas agregadas dedicadas, uma por indicador, sem carregar chamados |
| Senhas | `PasswordHasher<T>`, nunca hash próprio |
| Rate limiting | no login e nos endpoints anônimos, via middleware nativo do ASP.NET Core |
| Logs | estruturados, sem corpo de comentário nem dado pessoal desnecessário |

---

## 6. Ambiente local

`docker compose up` sobe PostgreSQL e API. O frontend roda em `vite dev` apontando para a API, até ser containerizado.

O seed inicial cria as categorias da seção 7 do README, as políticas de SLA da seção 8 e um usuário de cada perfil, exclusivamente em ambiente de desenvolvimento.

---

## 7. CI

GitHub Actions (`.github/workflows/ci.yml`), em todo push e pull request, em três jobs paralelos:

**Backend:** restore, build em Release, testes de unidade e de integração, e `ef migrations has-pending-model-changes` — que falha se alguém mudou uma entidade e esqueceu de gerar a migration. Os testes de integração usam o Docker do próprio runner, via Testcontainers; não há serviço de banco declarado no workflow, porque o ciclo de vida do container é do teste.

**Frontend:** `npm ci`, typecheck, lint e build. A versão do Node vem do `frontend/.nvmrc`, para não existirem duas fontes de verdade sobre isso no repositório.

**Imagem da API:** `docker build` do Dockerfile, sem publicar. Serve para o Dockerfile não apodrecer em silêncio junto com o código.
