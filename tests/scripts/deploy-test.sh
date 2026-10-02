#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/scripts" "$tmp/bin"
printf '#!/usr/bin/env bash\nexit 0\n' > "$tmp/bin/flock"
cp "$root/scripts/deploy-ssh.sh" "$tmp/scripts/"
export DEPLOY_TEST_TRACE="$tmp/trace"
export DEPLOY_TEST_REVISION=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
export DEPLOY_TEST_PREVIOUS=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
export PATH="$tmp/bin:$PATH"

cat > "$tmp/bin/git" <<'SH'
#!/usr/bin/env bash
printf 'git %s\n' "$*" >> "$DEPLOY_TEST_TRACE"
case "$1 $2" in
  'status --porcelain') printf '%s' "${DEPLOY_TEST_DIRTY:-}" ;;
  'rev-parse origin/production') printf '%s\n' "${DEPLOY_TEST_HEAD:-$DEPLOY_TEST_REVISION}" ;;
  'rev-parse HEAD') printf '%s\n' "$DEPLOY_TEST_PREVIOUS" ;;
  'merge-base --is-ancestor') exit "${DEPLOY_TEST_ANCESTRY_FAILURE:-0}" ;;
esac
SH
cat > "$tmp/bin/docker" <<'SH'
#!/usr/bin/env bash
printf 'docker %s\n' "$*" >> "$DEPLOY_TEST_TRACE"
if [[ $1 == login ]]; then read -r token || true; fi
if [[ $1 == image ]]; then printf '%s\n' "${DEPLOY_TEST_IMAGE_REVISION:-$DEPLOY_TEST_REVISION}"; fi
SH
cat > "$tmp/scripts/prod.sh" <<'SH'
#!/usr/bin/env bash
printf 'prod %s\n' "$*" >> "$DEPLOY_TEST_TRACE"
if [[ $1 == up ]]; then exit "${DEPLOY_TEST_UP_FAILURE:-0}"; fi
SH
chmod +x "$tmp/bin/"* "$tmp/scripts/"*
digest="sha256:$(printf '%064d' 0)"
valid_command="deploy $DEPLOY_TEST_REVISION $digest $digest $digest alexisloiselle"
fail() { printf '%s\n' "$1" >&2; exit 1; }
run() {
  : > "$DEPLOY_TEST_TRACE"
  printf 'test-registry-token\n' | SSH_ORIGINAL_COMMAND="$1" "$tmp/scripts/deploy-ssh.sh" > "$tmp/output" 2>&1
}
rejected_before_pull() {
  if run "$1"; then fail 'Unsafe deployment succeeded'; fi
  if grep -q 'docker pull\|prod up' "$DEPLOY_TEST_TRACE"; then fail 'Rejected deployment changed production'; fi
}

rejected_before_pull 'id'
export DEPLOY_TEST_DIRTY=' M modified-file'
rejected_before_pull "$valid_command"
unset DEPLOY_TEST_DIRTY
export DEPLOY_TEST_HEAD=$DEPLOY_TEST_PREVIOUS
rejected_before_pull "$valid_command"
unset DEPLOY_TEST_HEAD
export DEPLOY_TEST_ANCESTRY_FAILURE=1
rejected_before_pull "$valid_command"
unset DEPLOY_TEST_ANCESTRY_FAILURE
export DEPLOY_TEST_IMAGE_REVISION=$DEPLOY_TEST_PREVIOUS
if run "$valid_command"; then fail 'Mismatched image revision was deployed'; fi
if grep -q 'prod up\|git reset' "$DEPLOY_TEST_TRACE"; then fail 'Mismatched images changed the checkout'; fi
unset DEPLOY_TEST_IMAGE_REVISION

printf 'CORVEES_RELEASE=previous\n' > "$tmp/.env.release"
export DEPLOY_TEST_UP_FAILURE=1
if run "$valid_command"; then fail 'Failed deployment was reported as successful'; fi
grep -q "git reset --hard $DEPLOY_TEST_PREVIOUS" "$DEPLOY_TEST_TRACE" || fail 'Previous checkout was not restored'
grep -q 'prod app' "$DEPLOY_TEST_TRACE" || fail 'Previous application was not restarted'
grep -q '^CORVEES_RELEASE=previous$' "$tmp/.env.release" || fail 'Failed deployment changed the active release'
unset DEPLOY_TEST_UP_FAILURE

run "$valid_command"
grep -q "^CORVEES_RELEASE=$DEPLOY_TEST_REVISION$" "$tmp/.env.release" || fail 'Successful deployment did not record its release'
if grep -q 'test-registry-token' "$DEPLOY_TEST_TRACE" "$tmp/output"; then fail 'Registry token was logged'; fi
printf 'SSH deployment tests passed\n'
