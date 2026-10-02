#!/usr/bin/env bash
set -euo pipefail

: "${CORVEES_SCRIPT_TEST_DATABASE_URL:?Set a PostgreSQL connection URI for script tests}"
root=$(cd "$(dirname "$0")/../.." && pwd)
tmp=$(mktemp -d)
export SCRIPT_TEST_PSQL
SCRIPT_TEST_PSQL=$(command -v psql)
export SCRIPT_TEST_SCHEMA="dev_test_$$"
cleanup() {
  "$SCRIPT_TEST_PSQL" "$CORVEES_SCRIPT_TEST_DATABASE_URL" -Xq -c "DROP SCHEMA IF EXISTS $SCRIPT_TEST_SCHEMA CASCADE"
  rm -rf "$tmp"
}
trap cleanup EXIT

"$SCRIPT_TEST_PSQL" "$CORVEES_SCRIPT_TEST_DATABASE_URL" -Xq -v ON_ERROR_STOP=1 <<SQL
CREATE SCHEMA $SCRIPT_TEST_SCHEMA;
CREATE TABLE $SCRIPT_TEST_SCHEMA.groups (LIKE public.groups INCLUDING ALL);
CREATE TABLE $SCRIPT_TEST_SCHEMA.members (LIKE public.members INCLUDING ALL);
ALTER TABLE $SCRIPT_TEST_SCHEMA.members ADD FOREIGN KEY (group_id) REFERENCES $SCRIPT_TEST_SCHEMA.groups (id);
SQL

mkdir -p "$tmp/scripts" "$tmp/bin"
cp "$root/scripts/dev.sh" "$root/scripts/seed.sh" "$root/scripts/admin.sh" "$tmp/scripts/"
printf '#!/usr/bin/env bash\nexit 0\n' > "$tmp/bin/docker"
cp "$tmp/bin/docker" "$tmp/bin/dotnet"
cat > "$tmp/bin/psql" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
export PGOPTIONS="-c search_path=$SCRIPT_TEST_SCHEMA"
exec "$SCRIPT_TEST_PSQL" "$CORVEES_SCRIPT_TEST_DATABASE_URL" "$@"
SH
chmod +x "$tmp/bin/"*
export PATH="$tmp/bin:$PATH"

fail() { printf '%s\n' "$1" >&2; exit 1; }
query() { psql -XAtq -v ON_ERROR_STOP=1 -c "$1"; }

first=$("$tmp/scripts/dev.sh")
second=$("$tmp/scripts/dev.sh")
[[ $first == "$second" ]] || fail 'Restart changed the local credentials or MCP URL'
[[ $(cd "$tmp/bin" && "$tmp/scripts/seed.sh") == "$first" ]] || fail 'Standalone seeding differs from startup seeding'
[[ $(query 'SELECT count(*) FROM groups') == 1 ]] || fail 'Restart created another group'
[[ $(query 'SELECT count(*) FROM members') == 1 ]] || fail 'Restart created another member'
token=${first##*http://localhost:5169/m/}
token=${token%/mcp}
[[ $token =~ ^[0-9a-f]{64}$ ]] || fail 'Invalid member token format'
hash=$(printf '%s' "$token" | openssl dgst -sha256 -r | cut -d' ' -f1)
[[ $(query "SELECT count(*) FROM members WHERE token_hash = '$hash' AND deleted_at IS NULL") == 1 ]] || fail 'Token does not authenticate the seeded member'

query "UPDATE groups SET name = 'Edited group', version = 2; UPDATE members SET display_name = 'Edited member', version = 2;" > /dev/null
before=$(query 'SELECT row_to_json(g) FROM groups g; SELECT row_to_json(m) FROM members m;')
"$tmp/scripts/dev.sh" > /dev/null
[[ $(query 'SELECT row_to_json(g) FROM groups g; SELECT row_to_json(m) FROM members m;') == "$before" ]] || fail 'Restart overwrote existing data'

query 'TRUNCATE groups, members;' > /dev/null
[[ $("$tmp/scripts/dev.sh") == "$first" ]] || fail 'Database reset changed the local credentials'

production_first=$("$tmp/scripts/admin.sh" create-group 'Other group' 'Other member')
production_second=$("$tmp/scripts/admin.sh" create-group 'Other group' 'Other member')
[[ ${production_first##*token: } != "${production_second##*token: }" ]] || fail 'Admin provisioning no longer generates random tokens'
printf 'Local startup regression tests passed\n'
