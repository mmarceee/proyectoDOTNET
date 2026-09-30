#!/usr/bin/env bash
# Compila y ejecuta todas las pruebas dentro de un contenedor Linux con el SDK de .NET 10,
# igual que el pipeline de CI. Útil cuando Smart App Control de Windows bloquea los DLL compilados.
# Uso: ./scripts/test-en-docker.sh   (en Windows, desde Git Bash)
set -euo pipefail

cd "$(dirname "$0")/.."

repo="$(pwd -W 2>/dev/null || pwd)"

MSYS_NO_PATHCONV=1 docker run --rm -v "${repo}:/repo:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash -c '
  set -e
  mkdir /w
  cd /repo
  tar --exclude=./.vs --exclude=./.git --exclude="*/bin" --exclude="*/obj" -cf - . | tar -xf - -C /w
  cd /w
  dotnet restore Logistica.slnx
  dotnet build Logistica.slnx --no-restore -c Release
  dotnet test Logistica.slnx --no-build -c Release
'
