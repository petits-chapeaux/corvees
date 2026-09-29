#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

docker compose up -d --wait

export CapabilityToken="${CapabilityToken:-$(openssl rand -hex 32)}"
export ConnectionStrings__Corvees="${ConnectionStrings__Corvees:-Host=127.0.0.1;Port=54329;Database=corvees;Username=corvees;Password=local-only}"

printf 'MCP (local only): http://localhost:5169/g/%s/mcp\n' "$CapabilityToken"
exec dotnet run --project src/Corvees.Host --launch-profile http
