# ADR 0002 : Modèle de projets et contrats MCP/REST v1

- **Statut :** accepté
- **Date :** 2026-09-29
- **Issue :** [#2](https://github.com/petits-chapeaux/corvees/issues/2)
- **Complément :** champ calculé `nextStep` ajouté aux projets, compatible avec `schemaVersion:1`; voir [prototype MCP Apps](../research/mcp-apps.md)
- **Suite de :** [ADR 0001](0001-stack-backend-mcp.md). La décision d'un jeton partagé par groupe est remplacée ici par un jeton par membre. Le transport MCP demeure sans OAuth.

## Décision et périmètre

Le premier incrément permet de créer et modifier des projets, des étapes ordonnées, des lieux et leurs dépendances. Les dépendances servent à informer, jamais à interdire un changement de statut. Les membres disposent d'un nom et d'un jeton personnel. Aucun propriétaire de projet ni assignation d'étape. Calendrier, séances, achats, outils partagés, priorités, échéances, historique d'événements et recherche plein texte sont différés.

Un opérateur, muni des droits de base de données, crée les groupes et les membres, révoque/restaure les membres et tourne leurs jetons avec `scripts/admin.sh`. Il n'y a aucune API publique de provisionnement. Les membres peuvent consulter le répertoire, changer leur propre nom et modifier le nom du groupe; chacun a les mêmes droits sur les projets et lieux du groupe.

Le jeton est un secret aléatoire de 32 octets, affiché une seule fois; seul son SHA-256 hexadécimal est stocké. MCP : `/m/{memberToken}/mcp`; REST : `Authorization: Bearer {memberToken}` sur `/api/v1/*` sauf la racine publique `/api/v1`. Le serveur déduit le membre et son groupe du jeton. Supprimer un membre révoque immédiatement son jeton; le restaurer en émet un nouveau. Pas d'OAuth en v1. TLS et masquage des URL, en-têtes et erreurs contenant les secrets sont requis en production. Les journaux de proxy doivent aussi masquer l'URL MCP.

## Objets et stockage

Chaque identifiant public est un UUID immuable; titres et noms ne sont jamais des clés. Tous les objets modifiables ont `version` (entier positif attribué par le serveur), `createdAt`, `updatedAt`; les objets supprimables ont `deletedAt` nullable. Les horodatages sont en UTC, ISO 8601. Les mises à jour ignorent les champs absents; un `null` explicite efface les champs optionnels. Les noms et titres non blancs sont limités à 200 caractères, les adresses à 500 et les descriptions à 10 000.

| Table | Champs métiers | Invariants |
|---|---|---|
| `groups` | `id`, `name`, `version`, dates | groupe créé hors API |
| `members` | `id`, `group_id`, `display_name`, `token_hash` unique, `version`, dates, `deleted_at` | nom non unique; jeton unique et irréversible |
| `locations` | `id`, `group_id`, `name`, `address?`, `version`, dates, `deleted_at` | partageable entre projets; suppression autorisée même si référencé |
| `projects` | `id`, `group_id`, `title`, `description?`, `location_id?`, `version`, `step_list_version`, dates, `archived_at?`, `deleted_at?` | au plus un lieu; statut calculé, pas de colonne `status` |
| `steps` | `id`, `group_id`, `project_id`, `title`, `description?`, `status`, `position`, `version`, dates, `deleted_at?` | `status`: `todo`, `in_progress`, `done`; `(project_id, position)` unique, y compris les étapes supprimées, contrainte différable |
| `project_dependencies` | `group_id`, `project_id`, `prerequisite_id` | clé composée, deux projets du même groupe, pas d'auto-dépendance |
| `step_dependencies` | `group_id`, `project_id`, `step_id`, `prerequisite_id` | clé composée, deux étapes du même projet, pas d'auto-dépendance |

Des clés étrangères composées empêchent les références entre groupes, et entre projets pour les étapes; le filtre EF `GroupId` protège aussi les requêtes applicatives. Les cycles sont rejetés dans une transaction prenant un verrou consultatif par groupe. Les étapes supprimées conservent leur `position`; un déplacement peut renuméroter toutes les étapes, même supprimées, en transaction. Une restauration conserve alors la position courante. Une étape ajoutée prend `max(position) + 1`. L'ordre n'est pas déduit des dépendances.

Le statut d'un projet est `archived` si `archivedAt` est défini; sinon `complete` si au moins une étape non supprimée existe et toutes sont `done`; sinon `active` si au moins une est `in_progress` ou `done`; sinon `planned`. Il se recalcule à chaque lecture. Seuls archiver et désarchiver sont manuels. Un projet archivé reste lisible, mais doit être désarchivé avant de modifier ses champs, ses étapes ou ses dépendances. Un projet archivé peut être supprimé; la restauration conserve `archivedAt`.

Supprimer un projet masque ses étapes sans changer leur `deletedAt`. Une étape supprimée individuellement ne peut être restaurée avant son projet. Les liens de dépendance sont conservés à la suppression des cibles, mais les cibles supprimées sont omises des lectures ordinaires; la restauration révèle les liens. Une nouvelle dépendance vers un objet supprimé est interdite. Un lieu supprimé conserve son lien au projet : `locationId` reste présent, et `location` devient `{id, available:false, name:null}` jusqu'à restauration.

## Schémas publics

Les noms d'outils sont explicitement fixés en `snake_case` (convention du SDK C#, non imposée par MCP). Toute réponse métier réussie contient `{ "schemaVersion": 1, "kind": "<nom de l'outil>", "data": <objet> }`. Pour une liste, `data` est un tableau et `nextCursor` (chaîne opaque ou `null`) est adjacent. Les outils annoncent `inputSchema` et `outputSchema`, retournent `structuredContent` conforme et une copie JSON textuelle. Les annotations MCP indiquent lecture, caractère destructeur, idempotence et `openWorldHint:false`; ce sont des indices, jamais une permission.

Formes de `data` (les champs non suffixés `?` sont toujours présents) :

- **Group** : `id`, `name`, `version`, `createdAt`, `updatedAt`.
- **Member** : `id`, `groupId`, `displayName`, `version`, `createdAt`, `updatedAt`, `deletedAt?`. Aucun jeton ou hash dans les lectures.
- **Location** : `id`, `name`, `address?`, `version`, `createdAt`, `updatedAt`, `deletedAt?`.
- **Project** : `id`, `title`, `description?`, `locationId?`, `location?` (`id`, `available`, `name?`), `status` (`planned|active|complete|archived`), `dependencies` (UUID[] actifs), `version`, `stepListVersion`, `createdAt`, `updatedAt`, `archivedAt?`, `deletedAt?`. Les étapes se lisent séparément.
- **Step** : `id`, `projectId`, `title`, `description?`, `status` (`todo|in_progress|done`), `position` (entier), `dependencies` (UUID[] actifs), `version`, `createdAt`, `updatedAt`, `deletedAt?`.

Le suffixe `?` veut dire nullable et présent dans la réponse, non absent. Les entrées de liste reprennent la forme de leur objet. Les arguments `projectId`, `stepId`, `locationId`, `prerequisiteId`, `targetStepId` sont des UUID, sauf `locationId` optionnel qui peut être `null` pour détacher un lieu. `expectedVersion` et `expectedListVersion` sont des entiers positifs. Les mutations ne permettent pas d'écrire `id`, `position`, les dates, ou les versions.

| Outil MCP | Entrée obligatoire | Entrée optionnelle | Sortie `data` |
|---|---|---|---|
| `get_group` | aucune | aucune | Group |
| `update_group` | `name`, `expectedVersion` | aucune | Group |
| `list_members` | aucune | `limit`, `cursor` | Member[] |
| `get_me` | aucune | aucune | Member |
| `update_me` | `displayName`, `expectedVersion` | aucune | Member |
| `list_locations` | aucune | `limit`, `cursor`, `deletedOnly` | Location[] |
| `get_location` | `locationId` | `includeDeleted` | Location |
| `create_location` | `name` | `address` | Location |
| `update_location` | `locationId`, `expectedVersion` | `name`, `address` | Location |
| `delete_location`, `restore_location` | `locationId`, `expectedVersion` | aucune | Location |
| `list_projects` | aucune | `limit`, `cursor`, `deletedOnly`, `status`, `locationId` | Project[] |
| `get_project` | `projectId` | `includeDeleted` | Project |
| `create_project` | `title` | `description`, `locationId` | Project |
| `update_project` | `projectId`, `expectedVersion` | `title`, `description`, `locationId` | Project |
| `archive_project`, `unarchive_project`, `delete_project`, `restore_project` | `projectId`, `expectedVersion` | aucune | Project |
| `list_steps` | `projectId` | `limit`, `cursor`, `deletedOnly` | Step[] |
| `get_step` | `projectId`, `stepId` | `includeDeleted` | Step |
| `create_step` | `projectId`, `title`, `expectedListVersion` | `description` | Step |
| `update_step` | `projectId`, `stepId`, `expectedVersion` | `title`, `description`, `status` | Step |
| `move_step` | `projectId`, `stepId`, `targetStepId`, `placement` (`before|after`), `expectedListVersion` | aucune | Step |
| `delete_step`, `restore_step` | `projectId`, `stepId`, `expectedVersion`, `expectedListVersion` | aucune | Step |
| `add_project_dependency`, `remove_project_dependency` | `projectId`, `prerequisiteId`, `expectedVersion` | aucune | Project |
| `add_step_dependency`, `remove_step_dependency` | `projectId`, `stepId`, `prerequisiteId`, `expectedVersion` | aucune | Step |

`limit` vaut 50 par défaut et 100 au maximum. Les listes de projets sont triées par dernière modification décroissante; les étapes par `position`. `deletedOnly:true` rend uniquement les projets, étapes ou lieux supprimés; sans ce filtre, ils sont exclus. Les membres supprimés ne sont accessibles qu'à l'opérateur. `includeDeleted:true` autorise une lecture individuelle. Le curseur est opaque; la pagination est au mieux cohérente si des mutations surviennent entre pages. Aucun `totalCount` ni `allowedActions` en v1.

## Correspondance REST

Chaque route métier est préfixée `/api/v1` et partage le même service applicatif que l'outil homonyme. `GET /group`, `PATCH /group`, `GET /members`, `GET /me`, `PATCH /me`; `GET|POST /locations`, `GET|PATCH|DELETE /locations/{locationId}`, `POST /locations/{locationId}/restore`; `GET|POST /projects`, `GET|PATCH|DELETE /projects/{projectId}`, `POST /projects/{projectId}/{archive|unarchive|restore}`; `POST /projects/{projectId}/dependencies`, `DELETE /projects/{projectId}/dependencies/{prerequisiteId}`; `GET|POST /projects/{projectId}/steps`, `GET|PATCH|DELETE /projects/{projectId}/steps/{stepId}`, `POST /projects/{projectId}/steps/{stepId}/{move|restore|dependencies}`, `DELETE /projects/{projectId}/steps/{stepId}/dependencies/{prerequisiteId}`. Les paramètres d'identité viennent de l'URL, les filtres de l'URL, et les autres champs du JSON. Les suppressions REST s'exécutent immédiatement; le client se charge de confirmer, tout comme un client MCP pour ses outils.

Les lectures renvoient `ETag: "<version>"`; les mutations exigent `If-Match: "<expectedVersion>"` ou `If-Match: "<expectedListVersion>"` pour créer/déplacer une étape. Pour supprimer ou restaurer une étape, `If-Match` vise l'étape et `X-Step-List-Version` vise la liste. En MCP, les mêmes préconditions sont des arguments obligatoires. Les versions sont vérifiées par les jetons de concurrence EF au commit. Une modification d'étape indépendante n'invalide pas celle d'une autre étape; créer, supprimer, restaurer et déplacer des étapes incrémente `stepListVersion`.

Codes d'erreur JSON : `validation_error` (400), `missing_precondition` (428), `version_conflict` (412), `not_found` (404), `duplicate_dependency`, `dependency_cycle`, `invalid_state` (409). Un jeton REST absent/invalide donne 401; un jeton d'URL MCP absent/invalide donne 404. Les identifiants d'un autre groupe donnent 404. Les erreurs d'exécution MCP ont `isError:true` et un code métier textuel; les erreurs de protocole restent JSON-RPC. Aucune clé d'idempotence de création en v1 : réessayer un POST après un délai réseau peut créer un doublon.

### Exemples

```http
POST /api/v1/projects
Authorization: Bearer <memberToken>
Content-Type: application/json

{"title":"Réparer le deck","description":"Remplacer deux planches"}
```

```json
{"schemaVersion":1,"kind":"create_project","data":{"id":"7a7244c8-d725-449e-b40a-0874d868de8e","title":"Réparer le deck","description":"Remplacer deux planches","locationId":null,"location":null,"status":"planned","dependencies":[],"version":1,"stepListVersion":1,"createdAt":"2026-09-29T12:00:00Z","updatedAt":"2026-09-29T12:00:00Z","archivedAt":null,"deletedAt":null}}
```

```json
{"name":"create_step","arguments":{"projectId":"7a7244c8-d725-449e-b40a-0874d868de8e","title":"Mesurer les planches","expectedListVersion":1}}
```

La réponse de `create_step` est `{"schemaVersion":1,"kind":"create_step","data":<Step>}`; le client relit ensuite le projet pour connaître son nouveau `stepListVersion`. Pour supprimer une étape, le client transmet sa `version` et la `stepListVersion` du projet.

## Justification et compromis

Une API uniquement consultative ou centrée sur les séances n'accomplirait pas la gestion de projet demandée pour ce premier incrément. Les dépendances sont des tables de jointure car un projet ou une étape peut avoir plusieurs prérequis; une seule clé étrangère ne suffirait pas. Un outil par opération rend les effets visibles à l'agent, au prix d'une liste d'environ 30 outils. Le statut calculé évite de synchroniser une colonne avec les étapes, tandis que `archived_at` reste un choix humain. Les jetons de membre dans l'URL MCP sont faciles à configurer sans OAuth, mais sensibles aux journaux, navigateurs et partages de liens. Les curseurs à décalage peuvent perdre leur cohérence après une mutation; un curseur par clé sera envisagé si les listes grandissent.

## Compatibilité et suivi

Ajouter des champs facultatifs est compatible avec `schemaVersion:1`. Changer la signification ou le type d'un champ, une valeur d'énumération ou une entrée obligatoire exige un nouveau `schemaVersion`, `/api/v2` et de nouveaux noms d'outils; v1 reste disponible pendant la migration. Les noms de méthodes C# n'établissent pas le contrat public. Vérifier `tools/list` et les schémas générés par le SDK en CI.

Des améliorations ultérieures peuvent ajouter une authentification OAuth, des permissions par membre, un journal d'événements, une pagination par clé stable et des clés d'idempotence. Elles ne sont pas requises pour v1.
