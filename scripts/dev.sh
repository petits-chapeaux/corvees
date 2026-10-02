#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
docker compose up -d --wait

export ConnectionStrings__Corvees="${ConnectionStrings__Corvees:-Host=127.0.0.1;Port=54329;Database=corvees;Username=corvees;Password=local-only}"

dotnet tool restore
# The EF tool on macOS may write a Windows-style bin path inside the host project.
rm -rf 'src/Corvees.Host/bin\Debug'
dotnet build Corvees.slnx
dotnet tool run dotnet-ef database update --project src/Corvees.Infrastructure --startup-project src/Corvees.Host --no-build
rm -rf 'src/Corvees.Host/bin\Debug'

./scripts/seed.sh
exec dotnet run --project src/Corvees.Host --launch-profile http
