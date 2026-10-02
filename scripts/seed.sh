#!/usr/bin/env bash
set -euo pipefail

export PGHOST=127.0.0.1 PGPORT=54329 PGDATABASE=corvees PGUSER=corvees PGPASSWORD=local-only

# This public development token is only seeded in the loopback database above.
token=$(printf '%s' 'corvees-local-member' | openssl dgst -sha256 -r | cut -d' ' -f1)
hash=$(printf '%s' "$token" | openssl dgst -sha256 -r | cut -d' ' -f1)
group_id=00000000-0000-0000-0000-000000000001
member_id=00000000-0000-0000-0000-000000000002
psql -Xq -v ON_ERROR_STOP=1 -v group_id="$group_id" -v member_id="$member_id" -v hash="$hash" <<'SQL'
BEGIN;
INSERT INTO groups (id, name, version, created_at, updated_at)
VALUES (:'group_id'::uuid, 'Groupe local', 1, now(), now()) ON CONFLICT (id) DO NOTHING;
INSERT INTO members (id, group_id, display_name, token_hash, version, created_at, updated_at)
VALUES (:'member_id'::uuid, :'group_id'::uuid, 'Membre local', :'hash', 1, now(), now())
ON CONFLICT (id) DO NOTHING;
COMMIT;
SQL
printf 'Group: %s\nFirst member token: %s\n' "$group_id" "$token"
printf 'MCP (local only): http://localhost:5169/m/%s/mcp\n' "$token"
