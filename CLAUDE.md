# CLAUDE.md

Instruções para agentes de IA que trabalham neste repositório.

## O projeto

OpsDesk é um sistema interno de chamados de TI (service desk), com três perfis: Usuário, Técnico e Gestor. O domínio é pequeno e bem definido; a complexidade real está em três pontos, e é neles que o cuidado deve se concentrar:

1. **autorização por recurso** — solicitante nunca vê chamado de terceiro nem comentário interno;
2. **máquina de estados do chamado** — sete status, toda transição auditada;
3. **SLA em horas úteis** — com pausa enquanto o chamado aguarda o solicitante.

## Estado atual

O repositório contém apenas documentação. Nenhum código foi escrito ainda, e nenhum comando de build, teste ou execução existe. Ao criar o primeiro código, atualize a seção "Comandos" abaixo com o que passar a existir de verdade.

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

Ainda não existem. Serão preenchidos quando o código for criado.

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
* "Aguardando usuário" pausa o SLA de resolução, mas **não** o de resposta. Ver README, seção 8.2.
* Chamado cancelado fica fora dos indicadores de SLA.
* Prioridade nunca é inferida do texto do chamado, nem do assunto de um e-mail. A triagem é da equipe.
