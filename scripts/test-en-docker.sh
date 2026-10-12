#!/usr/bin/env bash
# Compila y ejecuta todas las pruebas dentro de un contenedor Linux con el SDK de .NET 10,
# igual que el pipeline de CI. Útil cuando Smart App Control de Windows bloquea los DLL compilados.
# Uso: ./scripts/test-en-docker.sh   (en Windows, desde Git Bash)
set -euo pipefail

cd "$(dirname "$0")/.."

repo="$(pwd -W 2>/dev/null || pwd)"

# Las pruebas de integración usan Testcontainers: el contenedor de pruebas recibe el socket de Docker
# para levantar PostgreSQL como contenedor hermano, y lo alcanza por el puerto publicado en el host.
# El reporte de cobertura (dominio y aplicación, ver coverage.runsettings) queda en ./coverage/index.html.
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "${repo}:/repo:ro" \
  -v "${repo}/coverage:/salida" \
  -v /var/run/docker.sock:/var/run/docker.sock \
  --add-host host.docker.internal:host-gateway \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c '
  set -e
  mkdir /w
  cd /repo
  tar --exclude=./.vs --exclude=./.git --exclude=./coverage --exclude=./TestResults --exclude="*/bin" --exclude="*/obj" -cf - . | tar -xf - -C /w
  cd /w
  dotnet restore Logistica.slnx
  dotnet build Logistica.slnx --no-restore -c Release
  dotnet test Logistica.slnx --no-build -c Release \
    --settings coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults
  dotnet tool restore
  dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:/salida \
    -reporttypes:"Html;TextSummary" -title:"Logística · dominio y aplicación"
  head -n 20 /salida/Summary.txt
'
