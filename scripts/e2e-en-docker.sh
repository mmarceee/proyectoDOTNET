#!/usr/bin/env bash
# Compila y ejecuta las pruebas de extremo a extremo (opcional 7.2) en un contenedor Linux con el SDK de .NET 10,
# igual que el job e2e del CI. Instala Chromium dentro del contenedor, así que no hace falta nada en Windows.
# Uso: ./scripts/e2e-en-docker.sh            todas las pruebas de extremo a extremo
#      ./scripts/e2e-en-docker.sh Portal     sólo las que contienen "Portal" en su nombre completo
# (en Windows, desde Git Bash, con Docker Desktop abierto)
set -euo pipefail

cd "$(dirname "$0")/.."

repo="$(pwd -W 2>/dev/null || pwd)"
filtro="${1:-}"

# Como test-en-docker.sh: el socket de Docker para que Testcontainers levante PostgreSQL. Los paquetes de NuGet y el
# navegador quedan en volúmenes, así las corridas siguientes no los vuelven a descargar. Si una prueba falla,
# Playwright deja la captura y la traza en TestResults/e2e-*.
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "${repo}:/repo:ro" \
  -v "${repo}/TestResults:/resultados" \
  -v logistica-nuget:/root/.nuget/packages \
  -v logistica-playwright:/root/.cache/ms-playwright \
  -v /var/run/docker.sock:/var/run/docker.sock \
  --add-host host.docker.internal:host-gateway \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e FILTRO="$filtro" \
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c '
  set -e
  mkdir /w
  cd /repo
  tar --exclude=./.vs --exclude=./.git --exclude=./coverage --exclude=./TestResults --exclude="*/bin" --exclude="*/obj" -cf - . | tar -xf - -C /w
  cd /w
  resultados=$(mktemp -d /resultados/e2e-XXXXXXXX)
  dotnet build tests/Logistica.E2ETests -c Release
  dotnet build-server shutdown
  pwsh tests/Logistica.E2ETests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
  export E2E_RESULTADOS="$resultados"
  if [ -n "$FILTRO" ]; then
    dotnet test tests/Logistica.E2ETests --no-build -c Release --filter "FullyQualifiedName~$FILTRO" \
      --logger "console;verbosity=normal" --logger trx --results-directory "$resultados"
    exit 0
  fi
  dotnet test tests/Logistica.E2ETests --no-build -c Release \
    --logger "console;verbosity=normal" --logger trx --results-directory "$resultados"
'
