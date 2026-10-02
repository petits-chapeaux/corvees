#!/usr/bin/env bash
set -euo pipefail
umask 077

pattern='^deploy ([0-9a-f]{40}) (sha256:[0-9a-f]{64}) (sha256:[0-9a-f]{64}) (sha256:[0-9a-f]{64}) ([A-Za-z0-9-]+(\[bot\])?)$'
[[ ${SSH_ORIGINAL_COMMAND:-} =~ $pattern ]] || { echo 'Only a pinned production deployment is allowed' >&2; exit 2; }
revision=${BASH_REMATCH[1]}
app_digest=${BASH_REMATCH[2]}
migrate_digest=${BASH_REMATCH[3]}
tools_digest=${BASH_REMATCH[4]}
registry_user=${BASH_REMATCH[5]}
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"
exec 9> .prod-deploy.lock
flock -n 9 || { echo 'Another deployment is running' >&2; exit 1; }

[[ -z $(git status --porcelain) ]] || { echo 'Production checkout has local changes' >&2; exit 1; }
git fetch --no-tags origin +refs/heads/production:refs/remotes/origin/production +refs/heads/main:refs/remotes/origin/main
[[ $(git rev-parse origin/production) == "$revision" ]] || { echo 'Skipping a superseded production commit' >&2; exit 1; }
git merge-base --is-ancestor origin/main "$revision" || { echo 'Rebase production on main before deploying' >&2; exit 1; }
previous=$(git rev-parse HEAD)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
export DOCKER_CONFIG="$tmp/docker"
mkdir -p "$DOCKER_CONFIG"
IFS= read -r registry_token
printf '%s' "$registry_token" | docker login ghcr.io --username "$registry_user" --password-stdin
unset registry_token

app_image="ghcr.io/petits-chapeaux/corvees-app@$app_digest"
migrate_image="ghcr.io/petits-chapeaux/corvees-migrate@$migrate_digest"
tools_image="ghcr.io/petits-chapeaux/corvees-tools@$tools_digest"
for image in "$app_image" "$migrate_image" "$tools_image"; do
  docker pull "$image"
  [[ $(docker image inspect "$image" --format '{{index .Config.Labels "org.opencontainers.image.revision"}}') == "$revision" ]] || {
    echo 'Image revision does not match production' >&2; exit 1;
  }
done

git fetch --no-tags origin +refs/heads/production:refs/remotes/origin/production
[[ $(git rev-parse origin/production) == "$revision" ]] || { echo 'Skipping a superseded production commit' >&2; exit 1; }
printf 'CORVEES_RELEASE=%s\nCORVEES_APP_IMAGE=%s\nCORVEES_MIGRATE_IMAGE=%s\nCORVEES_TOOLS_IMAGE=%s\n' \
  "$revision" "$app_image" "$migrate_image" "$tools_image" > "$tmp/release.env"
export CORVEES_RELEASE_ENV_FILE="$tmp/release.env"
git reset --hard "$revision"
if ! ./scripts/prod.sh up; then
  git reset --hard "$previous"
  unset CORVEES_RELEASE_ENV_FILE
  ./scripts/prod.sh app || echo 'Application rollback also failed; operator intervention required' >&2
  echo 'Deployment failed; database migrations are not rolled back' >&2
  exit 1
fi
install -m 600 "$tmp/release.env" .env.release.new
mv .env.release.new .env.release
printf 'Deployed %s\n' "$revision"
