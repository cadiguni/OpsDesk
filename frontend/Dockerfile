# Build do SPA e nginx servindo o resultado.
#
#   docker compose --profile web up -d --build
#
# Imagem para **ver** o sistema rodando, não para desenvolver nele: o bundle é estático e
# cada alteração de código exige rebuild. Para desenvolver, `npm run dev` continua sendo o
# caminho — com recarga imediata, que container com bind mount no Windows não entrega sem
# polling de arquivo.
FROM node:24-alpine AS build
WORKDIR /app

# Instalação em camada própria: só invalida o cache quando o lockfile muda.
#
# O .npmrc vem junto porque liga `engine-strict`. Sem ele, um Node fora da faixa aceita
# pelos binários nativos do Vite faria o npm omitir esses binários em silêncio, e o build
# quebraria adiante com erro de módulo ausente em vez de erro de versão.
COPY package.json package-lock.json .npmrc ./
RUN npm ci

COPY . .

# Raiz, e não a URL absoluta da API: o cliente passa a chamar caminho relativo, e o nginx
# desta mesma imagem repassa /api para a API. Com URL absoluta o navegador falaria com
# outra origem e voltaria a depender de CORS e de cookie entre origens.
#
# `/` em vez de string vazia de propósito: o cliente resolve a base com `??`, que só cai no
# padrão quando o valor é nulo — e string vazia depende de o minificador dobrar a expressão
# para se manter legível no bundle. Com `/` o resultado é o mesmo e não há o que interpretar.
ENV VITE_API_URL=/
RUN npm run build

FROM nginx:1.29-alpine AS final

COPY --from=build /app/dist /usr/share/nginx/html

# Template, e não configuração pronta: o endereço da API muda conforme onde a imagem
# roda — `api:8080` no compose, `127.0.0.1:8080` no App Service com sidecar. A imagem
# oficial passa os arquivos de /etc/nginx/templates pelo envsubst na subida. O filtro
# limita a troca às variáveis OPSDESK_, para que nenhuma variável de ambiente da
# plataforma colida com as do nginx.
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
ENV NGINX_ENVSUBST_FILTER=OPSDESK_ \
    OPSDESK_API_UPSTREAM=api:8080 \
    OPSDESK_EDGE_SETS_FORWARDED_FOR=false

EXPOSE 80
