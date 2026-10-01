#!/usr/bin/env bash
# Restaura um backup feito por deploy/backup.sh.
#
#   bash deploy/restore.sh 20260930T120000Z
#
# O argumento é o nome da pasta do backup, dentro de deploy/backups (ou de
# OPSDESK_BACKUP_DIR). Backup trazido de outro lugar: copie a pasta para lá antes.
#
# SUBSTITUI o banco e os arquivos atuais pelo conteúdo do backup. A ordem importa: tudo é
# conferido antes de qualquer coisa ser apagada; a API para, para ninguém gravar no meio;
# o banco volta inteiro; os volumes de arquivos são esvaziados e recebem o conteúdo do
# backup; e só então a API sobe de novo.
set -euo pipefail

# No Git Bash do Windows, argumento que começa com "/" é traduzido para caminho do
# Windows antes de chegar ao docker — e "/backups/..." vira "C:/Program Files/Git/...".
# No Linux esta variável não existe e não faz nada.
export MSYS_NO_PATHCONV=1

# Caminho nativo: "C:/..." no Git Bash (pwd -W), o normal no Linux, onde -W não existe.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && { pwd -W 2>/dev/null || pwd; })"
compose=(docker compose -f "$here/docker-compose.yml" --env-file "$here/.env")

# Aceita o nome da pasta ou um caminho até ela: o que importa é o nome.
stamp="$(basename "${1:-}")"

if [[ -z "$stamp" ]]; then
  echo "Uso: $0 <pasta-do-backup>   (por exemplo 20260930T120000Z)" >&2
  exit 1
fi

# Os arquivos são lidos pelo serviço `files`, que monta a pasta de backups em /backups.
# Assim nenhum caminho do servidor passa pela linha de comando do docker — no Git Bash do
# Windows, essa tradução de caminho entregava ao contêiner uma pasta vazia.
files=("${compose[@]}" --profile tools run --rm --no-deps -T files)

echo "Conferindo o backup $stamp..."
if ! "${files[@]}" sh -c "
    test -s /backups/$stamp/database.dump &&
    tar tzf /backups/$stamp/attachments.tar.gz > /dev/null &&
    tar tzf /backups/$stamp/keys.tar.gz > /dev/null"; then
  echo "Backup incompleto ou ilegível em $stamp: nada foi alterado." >&2
  exit 1
fi

if [[ "${OPSDESK_RESTORE_CONFIRM:-}" != "sim" ]]; then
  read -r -p "Isto substitui o banco e os anexos atuais. Digite 'sim' para continuar: " answer
  [[ "$answer" == "sim" ]] || { echo "Cancelado."; exit 1; }
fi

echo "Parando a API e o SPA..."
"${compose[@]}" stop caddy web api

echo "Banco..."
# --clean --if-exists: derruba os objetos atuais antes de recriar, para não misturar o
# schema restaurado com o que estava lá.
"${files[@]}" cat "/backups/$stamp/database.dump" |
  "${compose[@]}" exec -T postgres pg_restore -U opsdesk -d opsdesk --clean --if-exists --no-owner

echo "Anexos e chaves..."
"${files[@]}" sh -c "
    set -e
    find /volumes/attachments -mindepth 1 -delete
    find /volumes/keys -mindepth 1 -delete
    tar xzf /backups/$stamp/attachments.tar.gz -C /volumes/attachments
    tar xzf /backups/$stamp/keys.tar.gz -C /volumes/keys
    # A API roda como usuário sem privilégio (APP_UID 1654 nas imagens .NET).
    chown -R 1654:1654 /volumes/attachments /volumes/keys"

echo "Subindo de novo..."
"${compose[@]}" up -d

echo "Restaurado de $stamp"
