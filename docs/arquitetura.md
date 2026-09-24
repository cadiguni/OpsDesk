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
│   ├── Authentication/       ICurrentUser sobre HttpContext, cookie de refresh
│   ├── Authorization/        políticas por perfil
│   ├── Endpoints/            grupos de minimal API
│   ├── RateLimiting/         política dos endpoints anônimos
│   └── Validation/           filtro de endpoint do FluentValidation
├── OpsDesk.Domain/           entidades, enums, regras invariantes
│   ├── Common/               interfaces de timestamp
│   ├── Entities/
│   ├── Enums/
│   └── Tickets/              máquina de estados
├── OpsDesk.Application/      serviços de caso de uso, DTOs, validators
│   ├── Abstractions/         IClock, IBusinessCalendar, ICurrentUser, IOpsDeskDbContext
│   ├── Auth/                 AuthService, contratos, validators, claims
│   ├── Authorization/        filtro de visibilidade no IQueryable
│   ├── Common/               paginação
│   └── Sla/                  SlaClock
├── OpsDesk.Infrastructure/   DbContext, migrations, interceptors, integrações
│   ├── Authentication/       JwtOptions, emissão de JWT, refresh token
│   ├── Persistence/
│   │   ├── Configurations/
│   │   ├── Interceptors/
│   │   ├── Migrations/
│   │   └── Seed/
│   └── Time/                 BusinessCalendar, feriados, relógio
└── OpsDesk.Tests/
    ├── Unit/                 domínio, SLA, horas úteis, visibilidade
    └── Integration/          PostgreSQL real via Testcontainers, API por HTTP
```

Quatro projetos são o suficiente. A regra prática: se uma classe nova não tem onde morar entre esses quatro, o problema provavelmente é a classe, não a estrutura.

---

## 4. Decisões de arquitetura

### 4.1 Autorização é aplicada na query, não no endpoint

Toda leitura de chamados passa por um filtro derivado do usuário autenticado, aplicado no `IQueryable` antes de qualquer projeção:

* **Usuário**: apenas chamados em que ele é o solicitante;
* **Técnico**: todos os chamados, como o gestor — a fronteira de visibilidade é entre equipe e solicitante, e não entre perfis da equipe;
* **Gestor**: todos.

Comentários internos (`IsInternal = true`) são removidos na mesma camada quando o solicitante é o requisitante do chamado.

**Por quê:** se a proteção viver em atributo de rota, basta alguém criar um endpoint novo e esquecer o atributo para vazar chamado de terceiro. Amarrando ao acesso aos dados, o caminho inseguro deixa de existir.

Todos os predicados decidem por `TicketViewer.IsStaff`, que é falso para qualquer valor de perfil fora de técnico e gestor. É um fail closed deliberado: perfil novo que ninguém lembrou de tratar cai no caso mais restrito em vez de virar acesso amplo, e há teste fixando isso.

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

O token de acesso vale quinze minutos e trafega em `Authorization`, guardado apenas em memória no navegador. O refresh token vive em cookie `httpOnly`, `Secure` e `SameSite=Strict`, com `Path=/api/auth`, é rotacionado a cada uso e revogável no banco.

**Por quê:** token de acesso em `localStorage` é legível por qualquer XSS. O sistema ser interno reduz a exposição, não o impacto.

Detalhes que a implementação acrescenta:

* **Só o hash do refresh token é persistido**, em SHA-256. Um dump do banco não permite assumir sessão de ninguém. O hash é SHA-256 e não `PasswordHasher` de propósito: a invariante 8 trata de senha, segredo de baixa entropia que precisa de KDF lento contra dicionário; este token é 256 bits aleatórios, e pagar PBKDF2 a cada renovação só adicionaria latência.
* **Rotação com detecção de reuso, e uma janela de tolerância.** Cada refresh token vale um uso. Apresentar um token já rotacionado **depois** da janela é sinal de vazamento, e a resposta é revogar todas as sessões daquele usuário; `ReplacedByTokenHash` mantém a cadeia auditável. **Dentro** da janela — trinta segundos por padrão — um novo token é emitido em vez disso, porque ali a causa provável é concorrência do próprio cliente: duas abas recarregando juntas, uma requisição repetida por queda de rede, ou o efeito executado duas vezes pelo `StrictMode` do React. Sem essa tolerância, a rotação de uso único é estrita demais para o mundo real e o cliente legítimo derruba a própria sessão. É o mesmo desenho que as boas práticas do OAuth 2.0 chamam de *leeway*.
* **Uma renovação por vez no cliente.** Sem isso, uma tela que dispara várias consultas com o token expirado abriria várias renovações simultâneas; como o token é de uso único, a primeira invalidaria as demais e o backend interpretaria o resto como reuso, derrubando a sessão. O sintoma seria logout aleatório ao abrir telas pesadas.
* **Toda recusa de login devolve a mesma resposta.** E-mail inexistente, senha errada, conta desativada e conta sem senha são indistinguíveis em status, mensagem e tempo de resposta — este último garantido comparando contra um hash descartável quando não há o que comparar. Diferenciar transformaria o login em consulta de "esta pessoa trabalha aqui".
* **Rate limiting particionado por IP, com cotas separadas.** Um limitador global seria pior do que nenhum: quem testasse senhas consumiria a cota de todos e trancaria os usuários legítimos para fora. Login e cadastro dividem uma cota apertada — são as duas portas que aceitam senha. A renovação de sessão tem cota própria e bem mais larga, porque a interface renova a cada carregamento de página: com cota compartilhada, algumas recargas gastavam as tentativas de credencial e o usuário recebia "muitas tentativas" na primeira vez que digitava a senha, sem nunca ter errado nada.

### 4.7 Tipos do frontend são gerados do OpenAPI

Os tipos de requisição e resposta são gerados a partir do schema OpenAPI da API, não escritos à mão.

**Por quê:** DTO duplicado nos dois lados diverge silenciosamente, e a divergência aparece como bug de runtime em vez de erro de compilação.

### 4.8 Testes de integração usam PostgreSQL real

Os testes de integração sobem um PostgreSQL efêmero com Testcontainers e exercitam a API por `WebApplicationFactory`.

**Por quê:** o provider InMemory do EF Core não tem as semânticas que este projeto usa, entre elas sequences, `timestamptz` e comportamento transacional. Teste que passa nele e falha no Postgres é pior do que não ter teste.

Os testes de integração vêm em dois níveis, sobre o mesmo container: os que exercitam o `DbContext` com os mesmos interceptors da API, provando sequence, `timestamptz`, índice único e histórico automático; e os que exercitam a API por HTTP com `WebApplicationFactory`, provando status, corpo, atributos de cookie e autorização.

O cliente HTTP de teste guarda cookie entre requisições e usa base `https`, porque o transporte do `TestServer` é em memória — o suporte nativo a cookie não entra no caminho — e porque um `CookieContainer` se recusa a devolver cookie `Secure` em requisição `http`. Com base `http`, o teste de renovação de sessão passaria sem testar nada.

O que precisa de cobertura obrigatória:

* cada transição da máquina de estados do chamado;
* cada regra de autorização por perfil, inclusive os casos negativos;
* o cálculo de SLA, incluindo pausa e retomada;
* a ausência de comentário interno em toda resposta destinada ao solicitante.

### 4.9 Sem MediatR, AutoMapper ou repositório genérico

Casos de uso são classes de serviço injetadas por DI. O mapeamento para DTO é feito com `Select` direto no `IQueryable`. O acesso a dados usa o `DbContext`, exposto à camada de aplicação pela interface `IOpsDeskDbContext`.

Essa interface existe por causa do sentido das dependências — os serviços moram na Application, o contexto mora na Infrastructure, e é a Infrastructure que referencia a Application. Ela **não** é o repositório genérico que esta decisão descarta: expõe os mesmos `DbSet<T>` do contexto real, então os serviços continuam escrevendo LINQ com `Where`, `Include` e projeção, e o filtro de visibilidade continua sendo aplicado no `IQueryable`. Um repositório genérico trocaria isso por `GetAll` e `FindBy`, e é justamente aí que a query se esconde.

**Por quê:** MediatR e AutoMapper passaram a licença comercial e, mesmo antes disso, resolviam problemas de escala que este projeto não tem. A projeção manual ainda gera SQL melhor, porque só traz as colunas usadas. O `DbContext` já é Unit of Work e já expõe `IQueryable`.

### 4.10 Atribuição não muda status

Definir o responsável mexe só no responsável. A seção 10 do README diz que o status "poderá" mudar para "Em atendimento" ao assumir; optamos por não fazer isso implicitamente.

**Por quê:** a máquina de estados é a única coisa no sistema que muda status, e uma ação que altera dois campos por conta própria torna o histórico mais difícil de ler — dois eventos aparecem sem que ninguém tenha pedido o segundo — e a operação mais difícil de prever. Assumir e iniciar atendimento são dois cliques, e cada um fica separado na trilha de auditoria.

### 4.11 Reabrir chamado limpa `ResolvedAt` e `ClosedAt`, e o SLA retoma de onde parou

Quando um chamado volta de "Resolvido" ou "Fechado" para "Em atendimento", as datas de resolução e de fechamento são apagadas — depois que `SlaClock.ResumeAfterReopening` usou a de resolução para somar ao prazo o tempo útil em que o chamado ficou parado. As regras estão na seção 5.9 do README.

A janela de reabertura de chamado fechado depende do relógio e da configuração, e por isso mora em `ReopenPolicy`, na Application, e não no `TicketStatusMachine`: o grafo diz que a aresta existe, a política diz se ela ainda vale para aquele chamado.

**Por quê:** mantê-la faria o chamado nunca mais aparecer como vencido, por mais que a reabertura se arrastasse — `IsResolutionOverdue` depende de `ResolvedAt` ser nulo. A informação não se perde: a transição para "Resolvido" está no `TicketHistory` com data e autor, que é a trilha de auditoria de verdade. O campo na tabela é o estado atual, não o histórico.

### 4.12 Configuração é lida por DI, não durante a montagem do pipeline

`JwtOptions`, `RateLimitOptions` e a connection string são resolvidos de `IOptions<T>` ou do `IServiceProvider` no momento do uso, nunca por uma leitura direta de `IConfiguration` no `Program.cs`.

**Por quê:** ler configuração enquanto o pipeline é montado captura o valor daquele instante e ignora fontes registradas depois. É exatamente o que acontece com `WebApplicationFactory`, que injeta a sua configuração após a execução do ponto de entrada: o teste configura um valor, a aplicação usa outro, e nada falha para indicar o problema. Descobrimos isso com a suíte de integração sendo estrangulada pelo rate limiting de produção mesmo tendo configurado um limite alto.

### 4.13 Nomes do banco em snake_case

Tabelas e colunas usam `snake_case`, aplicado pela convenção do `EFCore.NamingConventions`. As classes e propriedades continuam em `PascalCase`.

**Por quê:** é a convenção do PostgreSQL, e identificador em `PascalCase` no Postgres obriga a citar tudo entre aspas em qualquer consulta manual — `SELECT "AssignedTechnicianId" FROM "Tickets"`. É o mesmo motivo de persistir enum como string: o banco precisa continuar legível para quem for investigar um chamado às duas da manhã.

### 4.14 Migrations desde o primeiro commit

O schema evolui exclusivamente por migrations do EF Core, versionadas no repositório. Nada de alteração manual no banco, nem de `EnsureCreated`.

### 4.15 Anexo fora do banco, atrás de uma interface

O conteúdo dos anexos vai para o `IAttachmentStorage`; o banco guarda metadado e a chave. A versão 1 implementa em disco (`FileSystemAttachmentStorage`), com volume no compose e caminho em `AttachmentStorage:RootPath`.

**Por quê:** guardar binário em coluna `bytea` infla o banco e o backup, e transforma cada leitura de metadado num risco de trazer megabytes junto. Disco resolve o ambiente local sem subir serviço novo, e a interface deixa a troca por S3 ou MinIO ser uma implementação nova — sem tocar em serviço, domínio ou schema.

A chave é gerada pelo sistema (`ano/mês/identificador.extensão`) e nunca deriva do nome enviado pelo usuário. Nome de arquivo é entrada não confiável: derivar caminho dele é travessia de diretório no primeiro `../`. O `FileSystemAttachmentStorage` ainda recusa qualquer caminho resolvido fora da raiz, para que uma mudança futura nesse ponto apareça como exceção em vez de leitura de arquivo arbitrário.

### 4.16 Anexo nasce pendente, e o vínculo define a visibilidade

Enviar um arquivo cria um anexo **sem chamado**, visível só para quem enviou. A abertura do chamado ou o envio do comentário é que o vincula — e copia o `IsInternal` do comentário.

**Por quê:** o formulário de abertura precisa aceitar arquivo antes de o chamado existir, e o editor de resposta precisa aceitar arquivo antes de o comentário existir. Vincular direto ao chamado no momento do envio resolveria o primeiro problema e criaria um vazamento no segundo: entre o envio do print e o envio da nota interna, o arquivo estaria legível para o solicitante. O estado pendente fecha essa janela.

A cópia do `IsInternal` no anexo é redundância deliberada. O filtro de visibilidade fica sem junção com a tabela de comentários, e o vínculo é o único ponto do sistema que escreve esse campo.

### 4.17 Instalar é um comando do próprio executável da API

Fora de `Development`, schema, dados de referência e primeiro gestor sobem por `--migrate`, `--seed`, `--bootstrap-admin` e `--setup`, passados ao mesmo binário que serve a API. O processo faz a tarefa e termina, sem abrir porta e sem iniciar os serviços em segundo plano.

**Por quê o mesmo executável, e não um utilitário separado:** a imagem do contêiner já é essa, e as migrations já estão nela. Um `opsdesk-cli` seria uma segunda imagem para publicar, versionar e manter em sincronia com a primeira, carregando exatamente o mesmo código.

**Por quê comando, e não execução automática na subida:** é o argumento da decisão 4.14 estendido. Alteração de schema não deve ser descoberta lendo log de inicialização, e o mesmo vale para a criação de uma conta administrativa. O custo é quem esquecer de rodar subir um sistema que parece saudável e recusa o primeiro chamado — e é por isso que o readiness do roadmap importa.

### 4.18 A credencial de bootstrap vale uma vez

O primeiro gestor nasce de `OpsDesk:Bootstrap`, e nasce com `User.MustChangePassword`. Enquanto a marca estiver de pé, um middleware recusa com 403 toda requisição autenticada fora de `/api/auth`.

**Por quê a marca existe:** a senha chegou por variável de ambiente, que aparece em log de deploy, em `docker inspect` e no histórico do shell de quem instalou. Sem a troca obrigatória, uma credencial que passou por todos esses lugares seria a senha permanente do administrador do sistema.

**Por quê middleware, e não filtro de endpoint:** filtro se declara rota a rota, e rota nova nasce sem ele — o mesmo raciocínio da decisão 4.1. Aqui o padrão é recusar, e abrir exceção exige dizer qual.

**Por quê o bootstrap só age sem nenhum gestor:** a variável tende a ficar esquecida no orquestrador. Recriar a conta a cada deploy ressuscitaria, em silêncio, um administrador que alguém pode ter desativado de propósito.

### 4.19 Liveness e readiness respondem perguntas diferentes

`/health` diz se o processo está vivo e o banco alcançável. `/ready` acrescenta schema na versão da aplicação e dados de referência presentes.

**Por quê separar:** as duas respostas levam a ações opostas. Um contêiner que não responde deve ser reiniciado; um contêiner cujo schema está desatualizado não deve receber tráfego — e reiniciá-lo não conserta nada, porque reiniciar não aplica migration. Com um endpoint só, o segundo caso vira laço de reinício sem diagnóstico.

**Por quê o corpo do `/ready` é detalhado:** o escritor padrão devolve a palavra `Unhealthy` e nada mais, o que joga fora um diagnóstico que já existe. Como ele é anônimo, o texto fala de schema e de dados de referência, nunca de connection string ou caminho de arquivo.

---

## 5. Requisitos não funcionais na prática

| Requisito | Como é atendido |
| --- | --- |
| Paginação | paginação por cursor ou offset sempre no backend, com limite máximo de página |
| Filtros | aplicados em SQL, nunca em memória |
| Dashboard | consultas agregadas dedicadas, uma por indicador, sem carregar chamados. A ordenação por contagem acontece sobre o agregado, antes da projeção: ordenar pela propriedade de um record já projetado não traduz para SQL, e o EF Core recusa a query com uma mensagem que aponta para o `Join` da navegação, não para a ordenação |
| Senhas | `PasswordHasher<T>`, nunca hash próprio. Troca de senha exige a senha atual e revoga as demais sessões |
| Rate limiting | middleware nativo do ASP.NET Core, particionado por IP, com cota apertada para credencial (login e cadastro) e cota larga e separada para renovação de sessão |
| Logs | estruturados, sem corpo de comentário nem dado pessoal desnecessário |

---

## 6. Ambiente local

`docker compose up` sobe PostgreSQL e API. O frontend tem dois modos: `vite dev` apontando para a API, que é o de desenvolvimento, e o perfil `web` do compose, que compila o SPA e o serve por nginx.

No perfil `web` o nginx também repassa `/api` para a API, o que põe SPA e API na mesma origem. Isso tira CORS e cookie entre origens do caminho no ambiente local e aproxima o desenho de produção, onde Front Door ou Application Gateway ficam na frente dos dois. A imagem é construída com `VITE_API_URL=/` justamente para o cliente chamar caminho relativo.

O seed inicial cria as categorias da seção 7 do README (só em banco sem nenhuma: depois da instalação o catálogo é do gestor), as políticas de SLA da seção 8 e um usuário de cada perfil. Os **usuários** só entram em ambiente de desenvolvimento; os dados de referência valem em qualquer ambiente, e fora de `Development` sobem pelo comando `--seed` (decisão 4.17).

---

## 7. CI

GitHub Actions (`.github/workflows/ci.yml`), em todo push e pull request, em três jobs paralelos:

**Backend:** restore, build em Release, testes de unidade e de integração, e `ef migrations has-pending-model-changes` — que falha se alguém mudou uma entidade e esqueceu de gerar a migration. Os testes de integração usam o Docker do próprio runner, via Testcontainers; não há serviço de banco declarado no workflow, porque o ciclo de vida do container é do teste.

**Frontend:** `npm ci`, typecheck, lint e build. A versão do Node vem do `frontend/.nvmrc`, para não existirem duas fontes de verdade sobre isso no repositório.

**Imagem da API:** `docker build` do Dockerfile, sem publicar. Serve para o Dockerfile não apodrecer em silêncio junto com o código.
