#!/usr/bin/env bash
# Se ejecuta dentro del SDK Docker. Copia el checkout a /w; no compila en la carpeta montada.
set -euo pipefail
mkdir -p /w
cd /repo
tar --exclude=./.vs --exclude=./.git --exclude="*/bin" --exclude="*/obj" -cf - . | tar -xf - -C /w
cd /w
dotnet restore Logistica.slnx
dotnet build Logistica.slnx --no-restore -c Release
if [[ "${1:-build}" == "migrations" ]]; then
  dotnet tool restore
  for module in Administracion Deposito Envios Planificacion; do
    project="src/Modules/${module}/Logistica.Modules.${module}"
    output="Infrastructure/Persistence/Migrations"
    dotnet ef migrations add PlanificacionCU40 --project "$project" --startup-project src/Logistica.Api --context "${module}DbContext" --output-dir "$output"
    mkdir -p "/repo/$project/$output"
    cp -r "$project/$output/." "/repo/$project/$output/"
  done
fi
if [[ "${1:-build}" == "test" ]]; then
  dotnet test Logistica.slnx --no-build -c Release
fi
