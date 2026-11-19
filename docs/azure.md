# Implantação no Azure

Como o OpsDesk roda no Azure App Service, e o que muda em relação ao compose de produção de [instalacao.md](instalacao.md). Este guia descreve o desenho e os requisitos. A infraestrutura de uma instalação concreta (Terraform, pipelines, nomes de recurso, credenciais) fica num repositório de deploy separado e privado, pelo motivo da seção 6.

## 1. O que sobe

| Recurso | Papel |
| --- | --- |
| App Service (Linux, B1 ou acima) | uma Web App com **dois contêineres**: o nginx do SPA (principal) e a API (sidecar) |
| Azure Database for PostgreSQL – Flexible Server | o banco |
| Storage Account (Blob) | os anexos ([instalacao.md](instalacao.md), seção 7) |
| Key Vault | connection string e chave de assinatura do JWT |
| Container Registry | as imagens da API e do SPA |
| Identidade gerenciada (atribuída pelo usuário) | pull do ACR, leitura do Key Vault e escrita no Blob, sem senha |
| Log Analytics | os logs dos contêineres e das requisições |

Azure SQL não serve: o OpsDesk depende do PostgreSQL (sequence do código do chamado, índices de trigramas, `timestamptz`).

## 2. Por que uma Web App com sidecar

O App Service faz o papel do Caddy: HTTPS, certificado e redirecionamento. O resto é o desenho do compose. O nginx recebe o tráfego na porta 80 e repassa `/api` para a API em `127.0.0.1:8080`, porque os contêineres de uma Web App com sidecar compartilham a rede.

Duas Web Apps separadas, uma para o SPA e outra para a API, quebrariam o login. O cookie de refresh é `SameSite=Strict` e precisa de SPA e API na mesma origem. E com o nginx repassando para o endereço público da outra Web App, a API ficaria exposta na internet e veria o IP do nginx no lugar do de quem acessa.

O modo sidecar exige `LinuxFxVersion = sitecontainers`. O provider `azurerm` do Terraform não cria esse modo: a Web App e os contêineres saem pelo `azapi` (`Microsoft.Web/sites` e `Microsoft.Web/sites/sitecontainers`).

| Contêiner | `isMain` | Porta | Imagem |
| --- | --- | --- | --- |
| `web` | `true` | 80 | `frontend/Dockerfile` |
| `api` | `false` | 8080 | `backend/OpsDesk.Api/Dockerfile` |

## 3. Configuração da Web App

As app settings chegam como variáveis de ambiente aos dois contêineres. Segredo entra só como referência ao Key Vault (`@Microsoft.KeyVault(SecretUri=...)`), lida com a identidade gerenciada.

| App setting | Valor | Por quê |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `ConnectionStrings__OpsDesk` | referência ao Key Vault | ver seção 4 |
| `Jwt__SigningKey` | referência ao Key Vault | |
| `DataProtection__KeysPath` | `/home/opsdesk/keys` | `/home` é o armazenamento persistente do App Service, montado em todos os contêineres. Sem ele, cada reinício gera chaves novas e o client secret do e-mail fica ilegível |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE` | `true` | mantém o `/home` persistente |
| `AttachmentStorage__Provider` | `AzureBlob` | |
| `AttachmentStorage__AzureBlob__ServiceUri` | `https://<conta>.blob.core.windows.net/` | |
| `AttachmentStorage__AzureBlob__ManagedIdentityClientId` | client id da identidade | a identidade precisa do papel `Storage Blob Data Contributor` na conta |
| `ForwardedHeaders__ForwardLimit` | `1` | ver abaixo |
| `OPSDESK_API_UPSTREAM` | `127.0.0.1:8080` | para onde o nginx repassa `/api`. `127.0.0.1`, e não `localhost`: no Alpine, `localhost` resolve primeiro para `::1` |
| `OPSDESK_EDGE_SETS_FORWARDED_FOR` | `true` | ver abaixo |

Na configuração do site: **Always On ligado**, health check em `/health`, `httpsOnly`, e pull do ACR pela identidade gerenciada (`acrUseManagedIdentityCreds`).

**IP de quem acessa.** O rate limiting de login particiona por IP. No compose, a API confia na rede interna para ler dois saltos do `X-Forwarded-For` (Caddy e nginx). No App Service quem chama o nginx é o front-end da plataforma, com endereço interno que não é publicado, e não há rede para a API confiar. Com `OPSDESK_EDGE_SETS_FORWARDED_FOR=true`, o nginx repassa o `X-Forwarded-For` como o App Service o montou, com o IP de quem acessa por último. A API lê só esse salto (`ForwardLimit=1`), vindo do nginx no loopback, em que ela já confia. Isso só é seguro porque nada alcança o nginx sem passar pelo App Service.

**Uma instância só.** Leitura da caixa de e-mail, fila de envio, alertas de SLA e renovação de feriados rodam dentro da API. Com Always On e uma instância, rodam uma vez. Para escalar, siga [instalacao.md](instalacao.md), seção 8.

## 4. PostgreSQL Flexible Server

* **`pg_trgm` liberado.** O servidor gerenciado só aceita `CREATE EXTENSION` das extensões listadas no parâmetro `azure.extensions`. Sem `PG_TRGM` nele, a migration `AddTicketSearchTrigramIndexes` falha.
* **TLS verificado.** A connection string usa `SSL Mode=VerifyFull`. A cadeia da Microsoft já está nas CAs da imagem da API.
* **Senha fora do state.** Ela é gerada pelo Terraform como recurso efêmero e gravada só no servidor e no Key Vault, por argumentos write-only.
* **Rede.** Sem VNet, o firewall precisa da regra "Allow Azure services" para a Web App conectar. Isso deixa qualquer serviço hospedado no Azure tentar conexão, ainda barrado por senha e TLS. Integração com VNet e acesso privado tira essa regra, mas exige que a migration rode de dentro da rede (seção 5).
* **Versão.** Nada no OpsDesk depende do PostgreSQL 18 do compose; o 16 e o 17 do Azure servem.

## 5. Implantação de uma versão

A ordem é a do compose de produção. A API não aplica migration sozinha fora de Development.

1. Gerar as imagens da API e do SPA e enviá-las ao ACR, com a tag do commit.
2. Rodar a imagem **da API** com `--migrate` contra o banco: `ConnectionStrings__OpsDesk` e `DataProtection__KeysPath` (qualquer pasta gravável) bastam. Na primeira instalação, `--setup` com `OpsDesk__Bootstrap__Email` e `OpsDesk__Bootstrap__Password` no lugar do `--migrate`. O agente que roda isso precisa alcançar o banco: uma regra de firewall temporária com o IP dele, removida ao fim.
3. Trocar a imagem dos dois contêineres (API primeiro) e reiniciar a Web App.
4. Esperar `/ready` responder 200. Ele reprova migration pendente e falta de dados de referência, com o que fazer no corpo da resposta.

Rollback de imagem é rodar o pipeline de novo num commit anterior. Rollback de migration não existe: migration nova precisa ser compatível com a versão anterior da API durante a troca.

## 6. Pipeline no Azure DevOps

O [azure-pipelines.yml](../azure-pipelines.yml) da raiz só dispara e estende um template de um repositório de deploy no Azure Repos:

```yaml
resources:
  repositories:
    - repository: deploy
      type: git
      name: <projeto>/<repositório-de-deploy>
extends:
  template: pipelines/build-deploy.yml@deploy
  parameters:
    databaseCommand: --migrate   # ou --setup
```

O desenho tem dois motivos:

* **O gatilho precisa estar deste lado.** O Azure Pipelines não dispara pipeline a partir de um repositório do GitHub declarado como resource; só a partir do repositório do próprio pipeline.
* **Este repositório é público.** Pool de agentes, nomes de recurso, service connection e variable groups são do ambiente de quem hospeda e ficam no repositório de deploy, privado.

O template recebe `databaseCommand` e faz as etapas da seção 5. Proteja a service connection e o environment de produção com o check **Required template**, apontando para esse template. Sem ele, quem pudesse alterar este `azure-pipelines.yml` conseguiria usar a service connection sem passar pelo template. Pull request não dispara o pipeline (`pr: none`): código de fork nunca roda com as credenciais do deploy.

## 7. Depois do primeiro deploy

* **Domínio próprio:** cadastre em Web App → Domínios personalizados, com certificado gerenciado (gratuito).
* **Login Microsoft:** acrescente `https://<domínio>/api/auth/microsoft/callback` no registro de aplicativo do Entra ID e confira o endereço mostrado em Configurações → Geral.
* **E-mail:** em Configurações → E-mail, preencha também o endereço do portal com o domínio definitivo.

## 8. Diagnóstico

* **Web App → Log stream** mostra o console dos dois contêineres ao vivo.
* No Log Analytics, a tabela `AppServiceConsoleLogs` tem o que a API escreve pelo Serilog, e a `AppServiceHTTPLogs` as requisições.
* A API recusa subir com configuração quebrada e diz o porquê no log: pasta de chaves sem escrita, Blob sem permissão, `KeysPath` ausente.
