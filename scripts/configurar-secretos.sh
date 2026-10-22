#!/usr/bin/env bash
# Carga en dotnet user-secrets las cadenas de conexión para ejecutar la Api y el Worker
# desde Visual Studio o con dotnet run, usando los valores de .env (Propuesta de stack, sección 4.3).
# Uso: ./scripts/configurar-secretos.sh   (en Windows, desde Git Bash)
set -euo pipefail

cd "$(dirname "$0")/.."

if [[ ! -f .env ]]; then
  echo "No existe .env. Copiá .env.example como .env y completalo." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
source .env
set +a

secreto() {
  dotnet user-secrets set "$1" "$2" --project src/Logistica.Api >/dev/null
}

secreto "ConnectionStrings:Postgres" "Host=localhost;Port=${POSTGRES_PORT};Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
secreto "ConnectionStrings:Valkey" "localhost:${VALKEY_PORT}"
secreto "ConnectionStrings:RabbitMQ" "amqp://${RABBITMQ_USER}:${RABBITMQ_PASSWORD}@localhost:${RABBITMQ_PORT}/"
secreto "ConnectionStrings:Smtp" "smtp://localhost:${MAILPIT_SMTP_PORT:-1025}"
secreto "Seguimiento:Avisos:ClaveCifrado" "${AVISOS_CLAVE_CIFRADO:-}"
# CU-60 y CU-62: el Worker también firma los enlaces de seguimiento que van en los correos.
secreto "Seguimiento:ClaveToken" "${SEGUIMIENTO_CLAVE_TOKEN:-}"
secreto "Aplicacion:UrlPublica" "${URL_PUBLICA:-http://localhost:${API_PORT:-8080}}"
secreto "Seguimiento:Correo:Remitente" "${CORREO_REMITENTE:-avisos@logistica.local}"

echo "Secretos cargados para Logistica.Api y Logistica.Worker (UserSecretsId: logistica-local)."
