# Instalação em produção

Guia para subir o OpsDesk num servidor da empresa e mantê-lo: primeira instalação, certificado, atualização, backup e restauração, e o que muda com mais de uma réplica.

Para **desenvolver**, o caminho é outro: o `docker-compose.yml` da raiz, descrito no [README](../README.md), seção 1. Os dois compose não se misturam. O de produção tem nome de projeto próprio (`opsdesk-prod`), e por isso volumes próprios.

---

## 1. O que sobe

```
navegador ──HTTPS──► caddy ──► web (nginx + SPA) ──/api──► api ──► postgres
                     :80/:443
```

| Serviço | Papel | Volume |
| --- | --- | --- |
| `caddy` | borda: HTTPS e redirecionamento de HTTP | `caddy-data`, com os certificados e a CA interna |
| `web` | SPA compilado, servido por nginx, que repassa `/api` para a API e aplica os cabeçalhos de segurança (CSP, HSTS) | — |
| `api` | a API, em `Production`, como usuário sem privilégio | `attachments-data` (anexos) e `keys-data` (chaves de cifragem) |
| `postgres` | banco | `postgres-data` |

**Só o Caddy publica porta.** Banco e API ficam na rede interna do compose, e a API só confia no `X-Forwarded-For` que vem dessa rede: é o que faz o rate limiting enxergar o IP de quem acessa, e não o do proxy.

Os arquivos ficam em [deploy/](../deploy):

| Arquivo | Para quê |
| --- | --- |
| `docker-compose.yml` | os serviços acima, e mais três que só rodam quando chamados: `setup`, `demo` e `files` |
| `Caddyfile` | a configuração da borda |
| `.env.example` | todas as variáveis, comentadas |
| `new-env.sh`, `new-env.ps1` | geram o `.env` com segredos aleatórios |
| `backup.sh`, `restore.sh` | backup e restauração de banco, anexos e chaves, juntos |

---

## 2. Pré-requisitos

* Linux com **Docker Engine** e o plugin **Compose v2** (`docker compose version`). Docker Desktop no Windows também serve para avaliar;
* 2 GB de memória e 10 GB de disco para começar. O que cresce é o volume de anexos;
* um nome para o sistema, como `opsdesk.empresa.com`, apontando para o servidor;
* portas 80 e 443 livres no servidor. Se estiverem ocupadas, `OPSDESK_HTTP_PORT` e `OPSDESK_HTTPS_PORT` trocam as portas publicadas.

---

## 3. Primeira instalação

Os comandos são executados na raiz do repositório.

**1. Gerar o `.env`.** O script copia o `.env.example`, sorteia a senha do banco, a chave dos tokens e a senha provisória do primeiro gestor, e mostra essa senha uma vez:

```bash
./deploy/new-env.sh opsdesk.empresa.com voce@empresa.com
# no Windows: .\deploy\new-env.ps1 -Site opsdesk.empresa.com -AdminEmail voce@empresa.com
```

Revise o `deploy/.env` gerado. As decisões que não têm padrão seguro são duas, o certificado (seção 4) e as portas. **Guarde uma cópia do `.env` fora do servidor**: sem ele o backup não abre.

**2. Construir as imagens:**

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env build
```

**3. Instalar o banco.** O serviço `setup` aplica as migrations, insere os dados de referência (categorias, políticas de SLA e feriados nacionais) e cria o primeiro gestor. Depois disso, termina:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile setup run --rm setup
```

**4. Subir:**

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
docker compose -f deploy/docker-compose.yml --env-file deploy/.env ps    # todos "healthy"
```

**5. Primeiro acesso.** Entre com o e-mail e a senha provisória do passo 1. O sistema exige trocar a senha antes de mostrar qualquer outra tela, e a API recusa tudo o mais até a troca acontecer. Depois disso, apague `OPSDESK_ADMIN_PASSWORD` do `.env`: ela não vale mais nada, mas não há motivo para guardá-la.

O bootstrap só age quando **não existe nenhum gestor**. Rodar o `setup` de novo, a cada atualização, não recria nem altera conta nenhuma.

### Depois do primeiro acesso

O que configurar, pela interface, antes de anunciar o sistema:

1. **Configurações → Geral:** nome da organização, se as pessoas podem se cadastrar sozinhas e de quais domínios de e-mail, e se entram com a conta Microsoft — o registro no Entra ID está em [integracao-email.md](integracao-email.md), seção 6.2;
2. **Configurações → E-mail:** envio pelo Microsoft Graph e, se a empresa quiser abrir chamado por e-mail, a caixa monitorada. O passo a passo do aplicativo no Entra ID está em [integracao-email.md](integracao-email.md), seção 6;
3. **Feriados:** os nacionais já estão lá. Faltam os municipais, os estaduais e o recesso de fim de ano;
4. **Usuários:** cadastre os técnicos. Com o e-mail configurado, cada um recebe um convite para definir a própria senha;
5. **Importação**, se houver histórico em outra ferramenta.

### Para avaliar antes de instalar de verdade

O serviço `demo` cria cinco pessoas e oito chamados, em todos os status, com conversa, nota interna e anexo. Ele recusa rodar em banco que já tem chamado, e as contas têm a senha conhecida `OpsDesk@123`. Por isso nunca rode numa instalação real:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile demo run --rm demo
```

Para testar na própria máquina, use `localhost` como endereço no passo 1 e portas livres, como `OPSDESK_HTTP_PORT=8088` e `OPSDESK_HTTPS_PORT=8443`.

---

## 4. Certificado

HTTPS não é opcional. O cookie de sessão é `Secure`, e sem HTTPS o navegador não o devolve: a sessão cairia a cada recarga.

`OPSDESK_TLS` escolhe de onde vem o certificado:

| Valor | Quando usar | Cuidados |
| --- | --- | --- |
| `internal` | rede interna, sem DNS público | o Caddy emite pela própria CA. O navegador avisa até a CA ser instalada nas máquinas |
| um e-mail | o nome é público na internet | Let's Encrypt. Exige o DNS apontando para o servidor e as portas 80 e 443 abertas para fora |

Para distribuir a CA interna, por GPO por exemplo, exporte o certificado raiz:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec caddy \
  cat /data/caddy/pki/authorities/local/root.crt > opsdesk-ca.crt
```

A CA vive no volume `caddy-data`. Perder o volume gera uma CA nova, e as máquinas precisam recebê-la de novo.

Se a empresa já tem um proxy reverso com certificado, deixe-o na frente do Caddy ou troque o `Caddyfile` por um `reverse_proxy web:80` simples. Nos dois casos, o proxy de fora precisa entrar em `ForwardedHeaders` para a API confiar no IP que ele repassa.

---

## 5. Atualização

```bash
git pull
docker compose -f deploy/docker-compose.yml --env-file deploy/.env build
bash deploy/backup.sh                                                                          # antes de migrar
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile setup run --rm setup
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
```

O `setup` é seguro de repetir. Ele aplica só as migrations pendentes e só insere o dado de referência que falta. O seed de categorias, por exemplo, só age em banco sem nenhuma, para não ressuscitar a categoria que o gestor renomeou.

Esquecer o `setup` não derruba nada em silêncio: o `/ready` reprova a migration pendente, com o comando no corpo da resposta.

---

## 6. Backup e restauração

Banco, anexos e chaves de cifragem andam **juntos**:

* banco sem anexos lista arquivos que o download não encontra;
* anexos sem banco são lixo em disco;
* sem as chaves do Data Protection, o client secret do e-mail, guardado cifrado no banco, não pode mais ser lido.

```bash
bash deploy/backup.sh                     # grava em deploy/backups/<data-hora>/
bash deploy/restore.sh 20261001T120000Z   # substitui banco e arquivos pelo backup
```

O backup roda com o sistema no ar: `pg_dump` dá uma foto consistente do banco. A restauração para a API antes de começar, para ninguém gravar no meio, e confere todos os arquivos antes de apagar qualquer coisa.

Rotina recomendada:

* backup diário pelo `cron`, com a pasta copiada para **fora do servidor**, junto com o `deploy/.env`:

  ```
  30 2 * * *  cd /opt/opsdesk && bash deploy/backup.sh && rsync -a deploy/backups/ backup@cofre:/opsdesk/
  ```

* `OPSDESK_BACKUP_DIR` aponta para outro disco, se houver;
* **restaurar de verdade, de tempos em tempos, numa máquina de teste.** Backup que nunca foi restaurado não é backup.

Os volumes do Caddy não entram no backup. Perdê-los só obriga a emitir o certificado de novo.

---

## 7. Anexos no Azure Blob Storage

Por padrão os anexos ficam no volume `attachments-data`, o certo para **um servidor só**. Com mais de uma réplica da API, cada uma teria o seu disco, e o anexo enviado numa daria 404 na outra. Para esse caso existe o Azure Blob:

```bash
# deploy/.env
OPSDESK_ATTACHMENT_STORAGE=AzureBlob
OPSDESK_BLOB_SERVICE_URI=https://opsdeskanexos.blob.core.windows.net
OPSDESK_BLOB_CONTAINER=attachments
```

* **A autenticação é por identidade, nunca por chave da conta.** No Azure, use Managed Identity. Fora dele, use um service principal, pelas variáveis `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` e `AZURE_CLIENT_SECRET`. Connection string com chave de conta é recusada na subida: a chave dá controle total da conta e não expira. A connection string só é aceita para o emulador (Azurite), em desenvolvimento e teste;
* a identidade precisa do papel **Storage Blob Data Contributor** na conta ou no contêiner. A API cria o contêiner se faltar e grava um blob de sonda na subida. Sem a permissão, ela não sobe, e a mensagem diz o que falta;
* os bytes continuam passando pela API, que aplica a regra de visibilidade antes de abrir o arquivo. Não existe link direto para o blob;
* **o `backup.sh` não copia os anexos do Blob.** Ligue *soft delete* e versionamento na conta, e varredura de malware (Defender for Storage), porque qualquer pessoa da empresa envia arquivo;
* a troca de disco para Blob não move os anexos já enviados. Copie o conteúdo do volume para o contêiner antes de trocar, com `azcopy`, mantendo os caminhos: a chave de cada anexo é o caminho dele.

---

## 8. Mais de uma réplica

O compose deste guia sobe uma réplica de cada serviço. Para várias, num orquestrador:

| Assunto | O que fazer |
| --- | --- |
| Anexos | Azure Blob (seção 7) |
| Chaves do Data Protection | um armazenamento compartilhado entre as réplicas. Com chaves separadas, o secret cifrado por uma réplica fica ilegível para a outra |
| Fila de e-mail | já é segura: cada envio é reservado por uma réplica antes de sair |
| Alertas de SLA | já são seguros: índice único no banco, um aviso só |
| Leitura da caixa de e-mail | `Mailbox__IntervalSeconds=0` em todas as réplicas menos uma. Duas lendo a mesma caixa não duplicam chamado, mas gastam chamadas ao Graph e geram falhas de processamento que se resolvem sozinhas |
| Limpeza de anexos pendentes | `AttachmentStorage__CleanUpIntervalHours=0` em todas menos uma, se a varredura repetida incomodar |
| Migration | continua como etapa de deploy, rodada uma vez, antes das réplicas novas |

---

## 9. Diagnóstico

| Sintoma | Onde olhar |
| --- | --- |
| API reiniciando | `docker compose ... logs api`. A subida valida a configuração e diz qual variável falta ou está errada |
| Sistema no ar, mas o `/ready` reprova | o corpo da resposta traz o comando que falta, quase sempre o `setup` |
| Navegador avisa de certificado | `OPSDESK_TLS=internal` sem a CA instalada na máquina (seção 4) |
| Sessão cai a cada recarga | acesso por HTTP, ou pelo IP em vez do nome configurado |
| E-mail parou depois de um deploy | o volume `keys-data` foi perdido, e com ele as chaves. Cadastre o client secret de novo em Configurações → E-mail |
| `Permission denied` em anexo | volume criado com outro dono. A imagem já cria as pastas com o dono certo; recriar o volume resolve |

`/health` e `/ready` respondem pela API e, pelo Caddy, em `https://<endereço>/health`. O primeiro diz se o processo está vivo e decide reiniciar o contêiner. O segundo diz se a instalação está completa e decide mandar tráfego.
