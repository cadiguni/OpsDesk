#!/usr/bin/env bash
# Gera deploy/.env a partir de deploy/.env.example, com segredos aleatórios.
#
#   ./deploy/new-env.sh opsdesk.empresa.com voce@empresa.com
#
# O primeiro argumento é o endereço do sistema (localhost para testar na própria
# máquina); o segundo, o e-mail do primeiro gestor. A senha provisória dele é gerada e
# mostrada uma vez — anote, ela vale para um único login.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
env_file="$here/.env"

if [[ -e "$env_file" ]]; then
  echo "deploy/.env já existe. Apague-o antes, se quiser mesmo gerar segredos novos —" >&2
  echo "trocar a senha do banco de uma instalação existente impede a API de abri-lo." >&2
  exit 1
fi

site="${1:-}"
admin="${2:-}"

if [[ -z "$site" || -z "$admin" ]]; then
  echo "Uso: $0 <endereço-do-sistema> <e-mail-do-primeiro-gestor>" >&2
  exit 1
fi

# Só caracteres que dispensam escape em URL de conexão e em YAML.
#
# O pipefail sai nesta função de propósito: o `head` fecha o pipe depois de N bytes, o
# `tr` que ainda lia /dev/urandom morre de SIGPIPE, e com pipefail isso derrubaria o
# script inteiro (código 141) sem mensagem nenhuma.
random() { ( set +o pipefail; LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c "$1" ); }

postgres_password="$(random 32)"
jwt_key="$(random 64)"
admin_password="$(random 20)"

sed \
  -e "s|^OPSDESK_SITE_ADDRESS=.*|OPSDESK_SITE_ADDRESS=$site|" \
  -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$postgres_password|" \
  -e "s|^JWT_SIGNING_KEY=.*|JWT_SIGNING_KEY=$jwt_key|" \
  -e "s|^OPSDESK_ADMIN_EMAIL=.*|OPSDESK_ADMIN_EMAIL=$admin|" \
  -e "s|^OPSDESK_ADMIN_PASSWORD=.*|OPSDESK_ADMIN_PASSWORD=$admin_password|" \
  "$here/.env.example" > "$env_file"

chmod 600 "$env_file"

cat <<EOF
deploy/.env criado.

Primeiro gestor: $admin
Senha provisória: $admin_password

Próximos passos (docs/instalacao.md):
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env build
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile setup run --rm setup
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
EOF
