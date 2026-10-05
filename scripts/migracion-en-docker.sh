#!/usr/bin/env bash
# Genera una migración de EF Core dentro de un contenedor Linux, porque Smart App Control de Windows
# bloquea los DLL que carga dotnet ef. Copia la carpeta Migrations resultante al módulo.
# Uso: ./scripts/migracion-en-docker.sh <Modulo> <NombreMigracion>   (en Windows, desde Git Bash)
# Ejemplo: ./scripts/migracion-en-docker.sh Envios Inicial
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Uso: $0 <Modulo> <NombreMigracion>" >&2
  exit 1
fi

cd "$(dirname "$0")/.."

repo="$(pwd -W 2>/dev/null || pwd)"

MSYS_NO_PATHCONV=1 docker run --rm -v "${repo}:/repo" -e MODULO="$1" -e NOMBRE="$2" mcr.microsoft.com/dotnet/sdk:10.0 bash -c '
  set -e
  mkdir /w
  cd /repo
  tar --exclude=./.vs --exclude=./.git --exclude="*/bin" --exclude="*/obj" -cf - . | tar -xf - -C /w
  cd /w
  dotnet tool restore
  dotnet restore src/Logistica.Api/Logistica.Api.csproj
  proyecto="src/Modules/${MODULO}/Logistica.Modules.${MODULO}"
  migraciones="Infrastructure/Persistence/Migrations"
  dotnet ef migrations add "$NOMBRE" \
    --project "$proyecto" \
    --startup-project src/Logistica.Api \
    --context "${MODULO}DbContext" \
    --output-dir "$migraciones"
  mkdir -p "/repo/${proyecto}/${migraciones}"
  cp -r "${proyecto}/${migraciones}/." "/repo/${proyecto}/${migraciones}/"
'
