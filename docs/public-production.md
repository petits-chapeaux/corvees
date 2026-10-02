# Production publique et livraison automatique

## Accès

`https://corvees.cacou.ca` suit ce chemin : Cloudflare Tunnel → Traefik HTTPS → `127.0.0.1:5169` → application → PostgreSQL privé. Les clients n'ont pas besoin de Tailscale. SSH, les commandes opérateur et PostgreSQL ne sont pas publiés.

La configuration du tunnel et de Traefik appartient à `alexisloiselle/home-server-infra`. Le connecteur sert uniquement `corvees.cacou.ca`, utilise un certificat d'origine vérifié et renvoie 404 pour les autres hôtes. Traefik impose HTTPS, désactive les access logs de cette route et laisse passer les flux MCP sans middleware de buffering.

L'application accepte les en-têtes transférés uniquement depuis sa passerelle Docker et le loopback. `scripts/prod.sh` découvre la passerelle à chaque démarrage. Deux sauts sont acceptés : Traefik, puis le connecteur local. En production, MCP et REST sont limités à 120 requêtes par minute et par IP, avant l'authentification; les sondes restent disponibles. Les journaux ASP.NET de requête sont désactivés et les scopes exclus du JSON console. Le connecteur utilise le niveau `fatal` pour éviter les URL sensibles dans ses erreurs de requête.

Les jetons MCP sont dans l'URL : Cloudflare les voit nécessairement. Ne pas activer Logpush ou des access logs contenant les chemins MCP. Aucun cache, challenge navigateur ou écran Cloudflare Access ne doit intercepter MCP. Utiliser exclusivement des URL HTTPS publiques et tourner immédiatement un jeton divulgué.

## Provisionnement restant

### Cloudflare

Créer un jeton API temporaire **Account / Cloudflare Tunnel / Edit**, limité au compte hébergeant `cacou.ca`. Le jeton DNS déjà utilisé par Traefik n'a pas ces droits; il reste réservé au DNS.

Sur `serv`, `~/home-server-infra/scripts/configure-corvees-tunnel.py` lit ce jeton sur stdin. Il crée ou configure le tunnel nommé `corvees`, ajoute le CNAME proxifié, écrit le jeton du connecteur dans `~/.config/cloudflared/corvees-token` avec permissions 600, puis démarre le projet Compose `corvees-tunnel`. Il ne stocke ni n'affiche le jeton API temporaire. Révoquer ce jeton API après le provisionnement; conserver le jeton du connecteur sur le serveur seulement.

### Tailscale

Créer `tag:corvees-ci`, avec un propriétaire administrateur. Autoriser cette identité à joindre uniquement `100.97.58.88:22`. Les règles Tailscale sont additives : une règle existante `* → *` doit exclure ce tag, sinon la règle SSH seule ne restreint rien. Préserver les accès des membres avec `autogroup:member` plutôt qu'un accès universel aux identités taguées.

Créer une identité fédérée dans [Trust credentials](https://console.tailscale.com/admin/settings/trust-credentials) :

- émetteur GitHub Actions;
- sujet `repo:petits-chapeaux/corvees:environment:production`;
- claim supplémentaire `ref = refs/heads/production`;
- scope d'écriture `auth_keys`, limité à `tag:corvees-ci`.

Enregistrer le **Client ID** dans la variable GitHub `TS_CLIENT_ID` et l'**Audience** dans `TS_AUDIENCE`. Ces valeurs ne sont pas secrètes. Aucun jeton Tailscale durable ni clé du serveur existant n'est utilisé en CI.

L'environnement GitHub `production` accepte uniquement la branche `production`. `DEPLOY_SSH_KEY`, `DEPLOY_HOST`, `DEPLOY_USER` et `DEPLOY_KNOWN_HOSTS` sont provisionnés séparément. La clé SSH n'autorise qu'une commande de déploiement validée, sans shell interactif, PTY ou forwarding. Docker et les commandes opérateur restent des privilèges de confiance sur l'hôte.

## Publier une version

La branche par défaut du dépôt est **`main`**, pas `master`. Le déclencheur est un push de `production`, y compris un push après rebase :

```sh
git fetch origin
git switch production
git rebase origin/main
git push --force-with-lease origin production
```

Préférer un fast-forward quand `production` n'a aucun commit propre. Ne pas stocker la configuration ou les secrets de production dans cette branche. Le déploiement refuse un commit qui n'inclut pas le `main` courant ou qui a été remplacé entre-temps sur `production`.

`.github/workflows/production.yml` :

1. tests .NET/PostgreSQL, scripts et formatage;
2. construction et test Docker, puis publication des trois images dans GHCR;
3. connexion Tailscale éphémère via OIDC, SSH privé, téléchargement par digest, sauvegarde, migrations, démarrage et vérification HTTPS publique.

Les images portent le SHA source et sont vérifiées par digest et label avant utilisation. Le jeton GHCR est celui du job GitHub, transmis sur stdin SSH; sa configuration Docker temporaire est supprimée en fin d'exécution. Aucun identifiant PostgreSQL n'est transmis à GitHub. Il n'y a pas de runner permanent sur le serveur et aucune livraison depuis une PR.

Les livraisons sont sérialisées et ne sont pas annulées pendant une migration. Une erreur de migration ou de démarrage rétablit le checkout et l'application précédents, sans migration inverse. Une erreur de sonde HTTPS après livraison est signalée par GitHub et exige de vérifier le tunnel; elle ne déclenche pas de restauration des données.

Le checkout de `~/corvees` doit être propre avant une livraison. Ne pas y modifier le code manuellement. `.env.production` conserve les secrets et le domaine; `.env.release` conserve les digests actifs. Les deux sont ignorés par Git. Les administrateurs continuent d'utiliser `./scripts/prod.sh admin ...`.

## Retour arrière

Si un démarrage échoue, la commande SSH tente automatiquement de relancer l'application précédente. Pour un retour volontaire, promouvoir un nouveau commit compatible depuis `main`, ou restaurer les références d'images et le checkout précédents puis lancer `scripts/prod.sh app` (sans migrations). Ne pas reconstruire un ancien tag ni appliquer aveuglément les migrations d'une ancienne version.

La [procédure privée](deploiement.md) couvre les commandes opérateur et les sauvegardes. Configurer la planification, la rétention et une copie chiffrée hors hôte avant de stocker des données importantes. Le moteur Docker 27 de `serv` doit aussi être mis à jour pour une isolation stricte des ports localhost vis-à-vis du LAN.
