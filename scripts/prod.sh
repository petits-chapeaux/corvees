#!/usr/bin/env bash
set -euo pipefail
umask 077

root=$(cd "$(dirname "$0")/.." && pwd)
env_file=${CORVEES_PROD_ENV_FILE:-$root/.env.production}
release_file=${CORVEES_RELEASE_ENV_FILE:-$(dirname "$env_file")/.env.release}
project=${CORVEES_PROJECT_NAME:-corvees-prod}
backup_dir=${CORVEES_BACKUP_DIR:-$root/backups}
command=${1:-}
shift || true

usage() {
  printf 'Usage: %s {build|up|app|status|logs|down|backup|admin COMMAND [ARGS...]}\n' "$0" >&2
  exit 2
}

case $command in
  build|up|app|status|logs|down|backup) [[ $# == 0 ]] || usage ;;
  admin) [[ $# -gt 0 ]] || usage ;;
  *) usage ;;
esac
[[ -f $env_file ]] || { printf 'Missing production configuration: %s\n' "$env_file" >&2; exit 1; }

compose() {
  local files=(--env-file "$env_file")
  if [[ -f $release_file ]]; then files+=(--env-file "$release_file"); fi
  docker compose --project-name "$project" "${files[@]}" -f "$root/compose.prod.yaml" "$@"
}

start_app() {
  if ! docker network inspect "${project}_default" > /dev/null 2>&1; then
    compose up --no-start --no-deps app
  fi
  CORVEES_TRUSTED_PROXY=$(docker network inspect "${project}_default" --format '{{(index .IPAM.Config 0).Gateway}}')
  export CORVEES_TRUSTED_PROXY
  compose up -d --no-deps --wait --wait-timeout 120 app
}

backup() {
  mkdir -p "$backup_dir"
  local file
  file="$backup_dir/$(date -u +%Y%m%dT%H%M%SZ)-$$.dump"
  if ! compose run --rm -T --no-deps --entrypoint pg_dump tools --format=custom --no-owner --no-acl > "$file"; then
    rm -f "$file"
    return 1
  fi
  printf '%s\n' "$file"
}

case $command in
  build)
    compose --profile tools build app migrate tools
    ;;
  up)
    exec 9> "$root/.prod-$project.lock"
    flock -n 9 || { echo 'Another deployment is running' >&2; exit 1; }
    compose up -d --wait postgres
    backup
    compose run --rm -T --no-deps migrate
    compose run --rm -T --no-deps --entrypoint psql tools -X -v ON_ERROR_STOP=1 -f /app/app-grants.sql
    start_app
    ;;
  app)
    start_app
    ;;
  admin)
    compose run --rm -T --no-deps tools "$@"
    ;;
  backup)
    backup
    ;;
  status)
    compose ps
    ;;
  logs)
    compose logs --tail=100 app postgres
    ;;
  down)
    compose down
    ;;
esac
