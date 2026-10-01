# Gera deploy\.env a partir de deploy\.env.example, com segredos aleatórios.
#
#   .\deploy\new-env.ps1 -Site opsdesk.empresa.com -AdminEmail voce@empresa.com
#
# A senha provisória do primeiro gestor é gerada e mostrada uma vez — anote, ela vale
# para um único login.
param(
    [Parameter(Mandatory)] [string] $Site,
    [Parameter(Mandatory)] [string] $AdminEmail
)

$ErrorActionPreference = 'Stop'

$envFile = Join-Path $PSScriptRoot '.env'

if (Test-Path $envFile) {
    Write-Error ("deploy\.env já existe. Apague-o antes, se quiser mesmo gerar segredos novos — " +
        "trocar a senha do banco de uma instalação existente impede a API de abri-lo.")
}

# Só letras e números: dispensam escape em URL de conexão e em YAML.
function New-Secret([int] $length) {
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'.ToCharArray()
    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes($length)
    -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
}

$adminPassword = New-Secret 20

$content = Get-Content (Join-Path $PSScriptRoot '.env.example') | ForEach-Object {
    switch -Regex ($_) {
        '^OPSDESK_SITE_ADDRESS=' { "OPSDESK_SITE_ADDRESS=$Site"; break }
        '^POSTGRES_PASSWORD=' { "POSTGRES_PASSWORD=$(New-Secret 32)"; break }
        '^JWT_SIGNING_KEY=' { "JWT_SIGNING_KEY=$(New-Secret 64)"; break }
        '^OPSDESK_ADMIN_EMAIL=' { "OPSDESK_ADMIN_EMAIL=$AdminEmail"; break }
        '^OPSDESK_ADMIN_PASSWORD=' { "OPSDESK_ADMIN_PASSWORD=$adminPassword"; break }
        default { $_ }
    }
}

# Sem BOM e com LF: o docker compose lê o arquivo, e um BOM no começo vira parte do nome
# da primeira variável.
[System.IO.File]::WriteAllText($envFile, (($content -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

Write-Host "deploy\.env criado."
Write-Host ""
Write-Host "Primeiro gestor: $AdminEmail"
Write-Host "Senha provisória: $adminPassword"
Write-Host ""
Write-Host "Próximos passos (docs\instalacao.md):"
Write-Host "  docker compose -f deploy/docker-compose.yml --env-file deploy/.env build"
Write-Host "  docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile setup run --rm setup"
Write-Host "  docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d"
