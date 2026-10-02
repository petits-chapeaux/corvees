#!/usr/bin/env bash
set -euo pipefail
umask 077

root=$(cd "$(dirname "$0")/../.." && pwd)
tmp=$(mktemp -d)
release=${CORVEES_TEST_RELEASE:-test}
export CORVEES_PROD_ENV_FILE="$tmp/production.env"
export CORVEES_PROJECT_NAME="corvees-test-$$"
export CORVEES_BACKUP_DIR="$tmp/backups"
unset CORVEES_RELEASE POSTGRES_PASSWORD CORVEES_OPERATOR_PASSWORD CORVEES_APP_PASSWORD CORVEES_ALLOWED_HOSTS CORVEES_HTTP_PORT
app_password=$(openssl rand -hex 32)
printf 'CORVEES_RELEASE=%s\nPOSTGRES_PASSWORD=%s\nCORVEES_OPERATOR_PASSWORD=%s\nCORVEES_APP_PASSWORD=%s\nCORVEES_HTTP_PORT=0\n' \
  "$release" "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" "$app_password" > "$CORVEES_PROD_ENV_FILE"

compose() {
  docker compose --project-name "$CORVEES_PROJECT_NAME" --env-file "$CORVEES_PROD_ENV_FILE" -f "$root/compose.prod.yaml" "$@"
}
prod() { "$root/scripts/prod.sh" "$@"; }
fail() { printf '%s\n' "$1" >&2; exit 1; }
query() { compose run --rm -T --no-deps --entrypoint psql tools -XAtq -v ON_ERROR_STOP=1 -c "$1"; }
cleanup() {
  local status=$?
  if [[ $status != 0 ]]; then compose logs --tail=100 app postgres >&2 || true; fi
  compose --profile tools down --volumes >&2
  rm -f "$root/.prod-$CORVEES_PROJECT_NAME.lock"
  rm -rf "$tmp"
}
trap cleanup EXIT

if [[ ${CORVEES_TEST_BUILD:-1} == 1 ]]; then prod build; fi
prod up
port=$(compose port app 8080)
url="http://127.0.0.1:${port##*:}"
[[ -z $(compose port postgres 5432 2>/dev/null || true) ]] || fail 'Database port is published'
[[ $(curl -sS -o /dev/null -w '%{http_code}' -H 'Host: unexpected.invalid' "$url/healthz") == 400 ]] || fail 'Unexpected host is allowed'
[[ $(curl -sS -o /dev/null -w '%{http_code}' "$url/api/v1/me") == 401 ]] || fail 'Anonymous REST access is allowed'

credentials=$(prod admin create-group "Test group's name" 'First member')
group_id=$(printf '%s\n' "$credentials" | head -n 1)
group_id=${group_id#Group: }
token=${credentials##*token: }
[[ $token =~ ^[0-9a-f]{64}$ ]] || fail 'Invalid token format'
[[ $(prod admin list-groups) == *"$group_id"* ]] || fail 'Group is absent from the directory'
members=$(prod admin list-members "$group_id")
[[ $members != *token* && $members != *hash* ]] || fail 'Member directory contains credentials'
member_id=$(query "SELECT id FROM members WHERE group_id = '$group_id';")
curl --fail -sS "$url/api/v1/group" -X PATCH -H "Authorization: Bearer $token" -H 'If-Match: "1"' \
  -H 'Content-Type: application/json' -d '{"name":"Edited group"}' > /dev/null
curl --fail -sS "$url/api/v1/me" -X PATCH -H "Authorization: Bearer $token" -H 'If-Match: "1"' \
  -H 'Content-Type: application/json' -d '{"displayName":"Edited member"}' > /dev/null

project=$(curl --fail -sS "$url/api/v1/projects" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' -d '{"title":"Persisted project"}')
project_id=$(printf '%s' "$project" | python3 -c 'import json,sys; print(json.load(sys.stdin)["data"]["id"])')
initialize=$(curl --fail -sS "$url/m/$token/mcp" -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"prod-test","version":"1.0"}}}')
[[ $initialize == *protocolVersion* && $initialize != *'"error"'* ]] || fail 'MCP initialization failed'

second=$(prod admin add-member "$group_id" 'Second member')
[[ ${second##*token: } != "$token" ]] || fail 'Member tokens are not random'
rotated=$(prod admin rotate-token "$member_id")
rotated=${rotated##*token: }
[[ $rotated != "$token" ]] || fail 'Token rotation did not change the token'
[[ $(curl -sS -o /dev/null -w '%{http_code}' "$url/api/v1/me" -H "Authorization: Bearer $token") == 401 ]] || fail 'Old token still authenticates'
prod admin delete-member "$member_id"
[[ $(curl -sS -o /dev/null -w '%{http_code}' "$url/api/v1/me" -H "Authorization: Bearer $rotated") == 401 ]] || fail 'Deleted member still authenticates'
[[ $(prod admin list-members "$group_id") == *"$member_id"* ]] || fail 'Deleted member cannot be found by an operator'
restored=$(prod admin restore-member "$member_id")
restored=${restored##*token: }
[[ $restored != "$rotated" ]] || fail 'Restoration reused a revoked token'
curl --fail -sS "$url/api/v1/me" -H "Authorization: Bearer $restored" > /dev/null

permissions=$(compose run --rm -T --no-deps -e PGUSER=corvees_app -e PGPASSWORD="$app_password" --entrypoint psql tools -XAtq -v ON_ERROR_STOP=1 <<'SQL'
SELECT has_database_privilege(current_database(), 'CREATE'), has_schema_privilege('public', 'CREATE'),
  has_table_privilege('groups', 'INSERT'), has_column_privilege('members', 'token_hash', 'UPDATE'),
  has_table_privilege('"__EFMigrationsHistory"', 'UPDATE');
SQL
)
[[ $permissions == 'f|f|f|f|f' ]] || fail 'Application has operator or migration privileges'

backup=$(prod backup)
[[ -s $backup ]] || fail 'Backup is empty'
[[ $(stat -c %a "$backup") == 600 ]] || fail 'Backup permissions expose data'
compose exec -T postgres psql -X -U postgres -d corvees -v ON_ERROR_STOP=1 -c 'CREATE DATABASE corvees_restore OWNER corvees_operator;'
compose run --rm -T --no-deps -e PGDATABASE=corvees_restore --entrypoint pg_restore tools \
  --dbname=corvees_restore --exit-on-error --no-owner --no-acl < "$backup"
restored_project=$(compose run --rm -T --no-deps -e PGDATABASE=corvees_restore --entrypoint psql tools \
  -XAtq -v ON_ERROR_STOP=1 -c "SELECT title FROM projects WHERE id = '$project_id';")
[[ $restored_project == 'Persisted project' ]] || fail 'Backup did not restore project data'

prod up
curl --fail -sS "$url/api/v1/projects/$project_id" -H "Authorization: Bearer $restored" > /dev/null
[[ $(query 'SELECT count(*) FROM groups;') == 1 ]] || fail 'Redeployment seeded another group'
[[ $(compose logs --no-color app) != *"$token"* ]] || fail 'Application logs contain a member token'
printf 'Production stack tests passed\n'
