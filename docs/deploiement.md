# Déploiement privé sur `serv`

Suivi : [issue #10](https://github.com/petits-chapeaux/corvees/issues/10).

## Stack

`compose.prod.yaml` est indépendant de la stack domestique et de `compose.yaml` (développement). Le projet Compose se nomme `corvees-prod` et conserve PostgreSQL dans son propre volume. Aucun port PostgreSQL n'est publié. Seul `127.0.0.1:5169` expose l'application; aucun accès public n'est configuré à cette étape.

Les images sont construites depuis le même checkout et portent le même label `CORVEES_RELEASE` :

- `corvees-app` : runtime .NET 10, utilisateur non-root, système de fichiers en lecture seule;
- `corvees-migrate` : bundle EF autonome, exécuté une seule fois avec le rôle opérateur;
- `corvees-tools` : commandes d'administration, `psql`, `pg_dump` et `pg_restore`.

L'application utilise `corvees_app`, sans droits DDL, de création de groupe ou de modification de jeton. `corvees_operator` possède le schéma et sert aux migrations et à l'administration. `postgres` reste réservé au bootstrap et à la récupération. Mettre à jour `docker/app-grants.sql` quand une migration ajoute des tables ou des capacités nécessitant de nouveaux droits applicatifs.

## Installation

Prérequis sur l'hôte Linux : Docker Compose v2, Bash, `flock` et OpenSSL. Pas de SDK .NET ni de client PostgreSQL sur l'hôte. Prévoir du temps et de la mémoire pour la compilation initiale.

`serv` utilise actuellement Docker 27.3.1. [Avant Docker 28, un port publié sur localhost peut être accessible depuis le même segment réseau local](https://docs.docker.com/engine/network/port-publishing/). Mettre à jour le moteur avant de considérer cette liaison comme une isolation stricte du LAN; aucune mise à jour globale de Docker n'est faite par ce déploiement.

Placer le checkout dans `~/corvees`. Ne pas ajouter ces services à `~/docker-compose.yml` et ne pas partager la base Immich.

Créer la configuration sans afficher les mots de passe :

```sh
cd ~/corvees
umask 077
# À exécuter uniquement lors de la première installation.
{
  printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 32)"
  printf 'CORVEES_OPERATOR_PASSWORD=%s\n' "$(openssl rand -hex 32)"
  printf 'CORVEES_APP_PASSWORD=%s\n' "$(openssl rand -hex 32)"
  printf 'CORVEES_RELEASE=initial-1\n'
  printf 'CORVEES_ALLOWED_HOSTS=localhost;127.0.0.1\n'
  printf 'CORVEES_HTTP_PORT=5169\n'
} > .env.production
chmod 600 .env.production
./scripts/prod.sh build
./scripts/prod.sh up
./scripts/prod.sh status
curl --fail http://127.0.0.1:5169/readyz
```

Les mots de passe hexadécimaux évitent les délimiteurs des chaînes de connexion et l'interpolation Compose. `.env.production` est ignoré par Git et exclu du contexte Docker. Ne pas le remplacer lors d'une mise à jour : les rôles PostgreSQL sont créés uniquement à l'initialisation du volume, et changer ce fichier ne tourne pas leurs mots de passe.

`up` attend PostgreSQL, sauvegarde la base, applique le bundle de migration, attribue les droits applicatifs puis démarre l'application et attend sa disponibilité. Toute erreur interrompt la suite. Aucun groupe ni jeton de développement n'est créé en production.

## Administration

Les administrateurs sont des opérateurs SSH, pas un nouveau rôle de membre. L'accès Docker donne des droits élevés sur l'hôte : le réserver aux personnes de confiance.

```sh
ssh serv
cd ~/corvees
./scripts/prod.sh admin list-groups
./scripts/prod.sh admin create-group 'Les chapeaux' 'Alexis'
./scripts/prod.sh admin list-members GROUPE_UUID
./scripts/prod.sh admin add-member GROUPE_UUID 'Camille'
./scripts/prod.sh admin rotate-token MEMBRE_UUID
./scripts/prod.sh admin delete-member MEMBRE_UUID
./scripts/prod.sh admin restore-member MEMBRE_UUID
```

Les listes ne contiennent ni jetons ni hashes. Les membres révoqués restent dans la liste avec `deleted_at`. La création, la rotation et la restauration affichent le nouveau jeton une seule fois. Le transmettre par un canal privé; ne pas le copier dans une issue, un journal ou un historique de commande. Les conteneurs `tools` sont supprimés après exécution et leur journalisation Docker est désactivée.

Pour un accès local temporaire depuis un autre ordinateur :

```sh
ssh -L 5169:127.0.0.1:5169 serv
```

Le client local peut alors utiliser `http://localhost:5169/m/JETON/mcp`. Ce lien HTTP est réservé au loopback via SSH; l'accès public futur devra utiliser HTTPS.

## Mise à jour manuelle et retour arrière

Avant l'activation de la livraison GitHub, livrer le code révisé dans le checkout, choisir un nouveau `CORVEES_RELEASE` dans `.env.production`, puis lancer `build` et `up`. Après activation, `.env.release` sélectionne les images GHCR par digest : ne plus construire ou éditer le checkout de production manuellement; suivre [la procédure publique](public-production.md). Ne jamais reconstruire un label déjà déployé. Les services domestiques ne sont pas affectés. Garder les images précédentes et noter le label de chaque livraison.

Les déploiements sont verrouillés pour éviter deux migrations concurrentes. Les migrations doivent rester compatibles avec l'ancienne application pendant leur application (expansion/contraction). Ne pas lancer d'administration pendant une migration. L'échec d'une migration ne déclenche pas le remplacement de l'application; un échec de disponibilité après remplacement nécessite une intervention.

Pour revenir à l'application précédente **sans exécuter de migration inverse** :

```sh
CORVEES_RELEASE=LABEL_PRECEDENT docker compose --project-name corvees-prod \
  --env-file .env.production -f compose.prod.yaml up -d --no-deps --wait app
```

Ce retour exige un schéma compatible. Une sauvegarde n'est pas un rollback automatique; une restauration peut perdre les écritures intervenues depuis la sauvegarde.

```sh
./scripts/prod.sh logs
./scripts/prod.sh down
```

`down` conserve les données. Ne jamais utiliser `down -v` sur la production.

## Sauvegarde et restauration

```sh
./scripts/prod.sh backup
```

Le script affiche le chemin d'un fichier `backups/*.dump` au format PostgreSQL custom, avec permissions privées. Une sauvegarde précède chaque migration, même à l'installation. Les sauvegardes contiennent des données privées : les copier vers un stockage chiffré hors de `serv`, sans les committer. La planification quotidienne, la rétention et l'export hors hôte restent à configurer avant l'ouverture publique.

Pour tester une restauration sans toucher à la base active :

```sh
docker compose --project-name corvees-prod --env-file .env.production -f compose.prod.yaml \
  exec -T postgres psql -X -U postgres -d corvees -v ON_ERROR_STOP=1 \
  -c 'CREATE DATABASE corvees_restore OWNER corvees_operator;'
docker compose --project-name corvees-prod --env-file .env.production -f compose.prod.yaml \
  run --rm -T --no-deps -e PGDATABASE=corvees_restore --entrypoint pg_restore tools \
  --dbname=corvees_restore --exit-on-error --no-owner --no-acl < backups/FICHIER.dump
```

Utiliser une base de restauration vide, vérifier les données et ne jamais diriger ces commandes vers la base active. Pour une récupération complète, recréer les rôles avec le bootstrap, restaurer la sauvegarde dans une base vide, puis appliquer les migrations et les droits du release choisi. Conserver les secrets séparément du dump, qui ne contient pas les mots de passe des rôles.

## Validation

```sh
bash tests/scripts/prod-test.sh
```

Sur un hôte Linux avec Docker, `curl` et Python 3, ce test crée un projet Compose et une configuration temporaires, construit les trois images, puis vérifie migrations, authentification REST/MCP, commandes d'administration, droits applicatifs, persistance et restauration d'une sauvegarde. Il détruit uniquement son propre volume de test.

Pour réutiliser des images déjà construites : `CORVEES_TEST_RELEASE=LABEL CORVEES_TEST_BUILD=0 bash tests/scripts/prod-test.sh`.

Le wrapper accepte `CORVEES_PROD_ENV_FILE`, `CORVEES_PROJECT_NAME` et `CORVEES_BACKUP_DIR` pour isoler les environnements. Les mots de passe ne sont jamais nécessaires dans les arguments du wrapper.

## Étapes suivantes

La configuration du tunnel, la limitation de débit et le workflow de livraison sont décrits dans [la procédure publique](public-production.md). L'activation exige les autorisations Cloudflare et Tailscale, puis une validation MCP réelle en HTTPS.
