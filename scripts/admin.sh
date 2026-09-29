#!/usr/bin/env bash
set -euo pipefail

# Run only with operator database credentials; tokens are printed once and never stored in plaintext.
command=${1:-}
if [[ ! $command =~ ^(create-group|add-member|rotate-token|delete-member|restore-member)$ ]]; then
  printf 'Usage: %s {create-group NAME FIRST_MEMBER|add-member GROUP_ID NAME|rotate-token MEMBER_ID|delete-member MEMBER_ID|restore-member MEMBER_ID}\n' "$0" >&2
  exit 2
fi

if [[ $command == create-group || $command == add-member ]]; then
  [[ $# == 3 ]] || { echo 'Expected two arguments' >&2; exit 2; }
else
  [[ $# == 2 ]] || { echo 'Expected one argument' >&2; exit 2; }
fi

if [[ $command == create-group || $command == add-member || $command == rotate-token || $command == restore-member ]]; then
  token=$(openssl rand -hex 32)
  hash=$(printf '%s' "$token" | openssl dgst -sha256 -r | cut -d' ' -f1)
fi

case $command in
  create-group)
    id=$(psql -XAtq -v ON_ERROR_STOP=1 -v group_name="$2" -v member_name="$3" -v hash="$hash" <<'SQL'
WITH new_group AS (INSERT INTO groups (id, name, version, created_at, updated_at)
  VALUES (gen_random_uuid(), :'group_name', 1, now(), now()) RETURNING id)
INSERT INTO members (id, group_id, display_name, token_hash, version, created_at, updated_at)
SELECT gen_random_uuid(), id, :'member_name', :'hash', 1, now(), now() FROM new_group RETURNING group_id;
SQL
)
    printf 'Group: %s\nFirst member token: %s\n' "$id" "$token"
    ;;
  add-member)
    id=$(psql -XAtq -v ON_ERROR_STOP=1 -v group_id="$2" -v member_name="$3" -v hash="$hash" <<'SQL'
INSERT INTO members (id, group_id, display_name, token_hash, version, created_at, updated_at)
VALUES (gen_random_uuid(), :'group_id'::uuid, :'member_name', :'hash', 1, now(), now()) RETURNING id;
SQL
)
    printf 'Member: %s\nMember token: %s\n' "$id" "$token"
    ;;
  rotate-token|restore-member)
    if [[ $command == rotate-token ]]; then condition='deleted_at IS NULL'; else condition='deleted_at IS NOT NULL'; fi
    id=$(psql -XAtq -v ON_ERROR_STOP=1 -v id="$2" -v hash="$hash" -v condition="$condition" <<'SQL'
UPDATE members SET token_hash = :'hash', deleted_at = NULL, updated_at = now(), version = version + 1
WHERE id = :'id'::uuid AND (:'condition' = 'deleted_at IS NULL' AND deleted_at IS NULL
  OR :'condition' = 'deleted_at IS NOT NULL' AND deleted_at IS NOT NULL) RETURNING id;
SQL
)
    [[ -n $id ]] || { echo 'Member not found or in the wrong state' >&2; exit 1; }
    printf 'Member: %s\nNew token: %s\n' "$id" "$token"
    ;;
  delete-member)
    id=$(psql -XAtq -v ON_ERROR_STOP=1 -v id="$2" <<'SQL'
UPDATE members SET deleted_at = now(), updated_at = now(), version = version + 1
WHERE id = :'id'::uuid AND deleted_at IS NULL RETURNING id;
SQL
)
    [[ -n $id ]] || { echo 'Member not found or already deleted' >&2; exit 1; }
    printf 'Revoked member: %s\n' "$id"
    ;;
esac
