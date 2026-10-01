#!/usr/bin/env bash
# Backup do OpsDesk: banco, anexos e chaves de cifragem, juntos.
#
#   ./deploy/backup.sh
#
# Os três precisam andar juntos. Banco sem anexos lista arquivos que o download não
# encontra; anexos sem banco são lixo em disco; e sem as chaves do Data Protection o
# client secret do e-mail, que está no banco, não pode mais ser lido.
#
# O resultado vai para deploy/backups/<data-hora>/ (ou OPSDESK_BACKUP_DIR). Copie essa
# pasta para fora do servidor, junto com o deploy/.env.
set -euo pipefail

# No Git Bash do Windows, argumento que começa com "/" é traduzido para caminho do
# Windows antes de chegar ao docker — e "/backups/..." vira "C:/Program Files/Git/...".
# No Linux esta variável não existe e não faz nada.
export MSYS_NO_PATHCONV=1

# Caminho nativo: "C:/..." no Git Bash (pwd -W), o normal no Linux, onde -W não existe.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && { pwd -W 2>/dev/null || pwd; })"
compose=(docker compose -f "$here/docker-compose.yml" --env-file "$here/.env")

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_root="$(grep -E '^OPSDESK_BACKUP_DIR=' "$here/.env" | cut -d= -f2- || true)"
backup_root="${backup_root:-./backups}"

# Relativo à pasta deploy/, como o compose resolve o volume do serviço `files`.
case "$backup_root" in
  /*) target="$backup_root/$stamp" ;;
  *) target="$here/$backup_root/$stamp" ;;
esac

mkdir -p "$target"

echo "Banco..."
# Formato custom: compactado, e restaurável seletivamente pelo pg_restore.
"${compose[@]}" exec -T postgres pg_dump -U opsdesk -d opsdesk --format=custom > "$target/database.dump"

echo "Anexos e chaves..."
"${compose[@]}" --profile tools run --rm --no-deps files \
  sh -c "tar czf /backups/$stamp/attachments.tar.gz -C /volumes/attachments . && tar czf /backups/$stamp/keys.tar.gz -C /volumes/keys ."

cat > "$target/README.txt" <<EOF
Backup do OpsDesk de $stamp (UTC).

  database.dump         pg_dump --format=custom do banco opsdesk
  attachments.tar.gz    volume de anexos
  keys.tar.gz           chaves do Data Protection (decifram o client secret do e-mail)

Restaurar: ./deploy/restore.sh $target
EOF

echo "Backup em $target"
