# Prototype MCP Apps

Ce prototype vérifie si des composants [MCP Apps](https://modelcontextprotocol.io/extensions/apps) dans un client MCP compatible suffisent à la place du frontend REST ([#8](https://github.com/petits-chapeaux/corvees/issues/8)) et de son chat ([#9](https://github.com/petits-chapeaux/corvees/issues/9)). Constats au 2026-09-30, spécification stable `2026-01-26` [1].

## Ce qui est livré

| Élément | Emplacement |
|---|---|
| Ressource `ui://corvees/project-board`, `text/html;profile=mcp-app`, un seul fichier HTML sans dépendance ni build | [Mcp/Apps/project-board.html](../../src/Corvees.Host/Mcp/Apps/project-board.html), servie par [AppCatalog.cs](../../src/Corvees.Host/Mcp/AppCatalog.cs) |
| `_meta.ui.resourceUri` (et la clé plate `ui/resourceUri`, dépréciée mais encore lue par d'anciens hôtes) sur `list_projects` et `get_project` | [ToolCatalog.cs](../../src/Corvees.Host/Mcp/ToolCatalog.cs) |
| Champ calculé `nextStep` (`{id, title, status}` ou `null`) sur chaque projet, REST et MCP | [ProjectQueries.cs](../../src/Corvees.Host/Application/ProjectQueries.cs) |
| Résumé lisible ajouté après la copie JSON pour `list_projects` et `get_project` | [ToolText.cs](../../src/Corvees.Host/Mcp/ToolText.cs) |

`nextStep` est l'étape non faite et non supprimée dont tous les prérequis actifs sont faits, en préférant une étape `in_progress`, puis la plus petite `position`. Comme les cycles sont refusés, un projet non archivé qui a des étapes restantes a toujours une prochaine étape; `null` veut donc dire archivé, terminé ou sans étape. L'ajout d'un champ est compatible avec `schemaVersion:1` (ADR 0002).

## Expérience obtenue

- **Liste.** Quand l'agent appelle `list_projects`, la vue affiche chaque projet avec son statut, sa prochaine étape, sa description et son lieu. Le composant est alimenté par `structuredContent` reçu avec `ui/notifications/tool-result`, puis appelle lui-même `list_projects` pour actualiser ou paginer.
- **Détail.** Quand l'agent appelle `get_project`, ou quand on choisit un projet dans la liste, la vue affiche les projets prérequis et leur statut, et la prochaine étape. Elle affiche aussi les étapes dans l'ordre, avec leurs prérequis (« Après : … ») et un badge « En attente » tant qu'un prérequis n'est pas fait. On peut changer le statut d'une étape, la renommer, la réordonner, la supprimer (après confirmation) ou en ajouter une. Chaque écriture transmet les versions attendues; un `version_conflict` recharge le projet. La vue informe l'agent des changements par `ui/update-model-context`.
- **Sans MCP Apps.** Les mêmes outils répondent comme avant : `structuredContent` et sa copie JSON en premier bloc (le contrat ADR 0002 ne change pas). Un second bloc texte résume, par exemple `- Renover cuisine (planifié) — prochaine étape : Choisir comptoir, évier et robinet (à faire)`. Dans Claude Code, qui n'affiche pas les vues, l'agent présente ce résumé.

Vérifié automatiquement : `tools/list`, `resources/list`, `resources/read`, `nextStep` et les résumés (tests d'intégration PostgreSQL), et les réponses du serveur local sur des données réelles. **Reste à vérifier à l'œil** : le rendu dans Claude Desktop et dans l'hôte de référence `basic-host` [8], qui accepte un serveur HTTP local. Les deux critères d'acceptation sur le rendu restent ouverts d'ici là.

## Effort et limites techniques

L'effort est modeste : environ 550 lignes de HTML/JS/CSS écrites à la main, dont environ 80 pour le pont JSON-RPC `postMessage`, et environ 150 lignes C#. Il n'y a ni build ni paquet npm.

- **SDK C#.** Nous passons `Tool.Meta` et `_meta` de ressource en `JsonObject`, ce qui suffit. Le paquet officiel `ModelContextProtocol.Extensions.Apps` 2.2.0, compatible avec notre SDK 2.2.0, fournit `[McpAppUi]`, `WithMcpApps()`, des modèles CSP typés et la détection de la capacité client. Ses API sont expérimentales (`MCPEXP003`) et il n'écrit pas la clé `ui/resourceUri` [4]. À adopter quand il sera stable.
- **Déploiement.** La vue est une ressource embarquée dans l'assembly et servie par `resources/read`. Aucun hébergement statique ni domaine supplémentaire n'est nécessaire.
- **Sécurité et CSP.**
  - La vue tourne dans une iframe isolée. Sans `_meta.ui.csp`, l'hôte applique `connect-src 'none'` et n'autorise que les scripts et styles inline [1].
  - Aucun CDN n'est donc utilisable sans déclarer ses domaines, d'où le fichier autonome.
  - La vue ne voit jamais le jeton : elle passe par `tools/call` de l'hôte, limité aux outils du même serveur dont `visibility` inclut `"app"`, ce qui est le défaut.
  - Le consentement aux appels initiés par la vue dépend de l'hôte (« MAY »). La vue peut donc appeler `delete_step`; elle demande elle-même une confirmation.
  - Le texte est toujours inséré par `textContent`.
- **Connexion.**
  - claude.ai et les connecteurs de Claude Desktop se connectent depuis les serveurs d'Anthropic : il faut une URL HTTPS publique, donc un jeton de membre exposé sur Internet dans l'URL [2].
  - En local, Claude Desktop n'accepte que stdio; `npx mcp-remote <url> --allow-http` fait le pont.
- **Cycle de vie.**
  - Chaque appel d'outil associé ouvre une nouvelle vue, sans état conservé entre deux rendus [1].
  - `ui/update-model-context` remplace le contexte précédent au lieu de l'accumuler.
  - Le détail fait un appel `get_project` par projet prérequis et pagine `list_steps` : correct pour de petits groupes, à regrouper si les données grandissent.
- **Taille.** claude.ai déplace les résultats de plus d'environ 150 000 caractères hors de la vue quand l'exécution de code est active [2]; Claude Code limite à 25 000 jetons. Nos pages de 50 projets restent loin de ces limites.

## Compatibilité des clients

| Client | Vue MCP Apps | Remarques |
|---|---|---|
| claude.ai (web) | oui | connecteur distant HTTPS public seulement [2] |
| Claude Desktop | oui | serveur local en stdio (`mcp-remote`) ou connecteur distant; le mode Cowork n'annonçait pas l'extension [3] |
| Claude mobile | oui, limité | WebView, connecteur ajouté depuis le web ou le bureau [2] |
| Claude Code (CLI) | non | texte seulement, demande ouverte [5] |
| Claude Code (VS Code) | non | constaté dans ce projet : seul le JSON est reçu |
| ChatGPT | oui | pont `ui/*` standard, extensions `window.openai` facultatives [6] |
| VS Code GitHub Copilot | oui | stable depuis 2026 [7] |
| Cursor, Goose, MCPJam, Postman, M365 Copilot | oui | matrice communautaire [3] |
| MCP Inspector | partiel | client web v2 seulement [3] |
| ext-apps `basic-host` | oui | hôte de référence, HTTP local [8] |
| Windsurf, Zed, JetBrains AI | non vérifié | absents de la matrice |

## Ce qui reste impossible sans frontend dédié

- Consulter ou cocher une étape sans passer par un assistant IA, son abonnement et son quota.
- Un lien direct vers un projet à partager dans le groupe, ou une page d'accueil permanente : la vue n'existe que dans une conversation.
- Notifications, rappels et travail hors ligne : la spécification n'en prévoit pas.
- Une expérience mobile soignée : WebView en superposition, sans permissions d'appareil.
- Un accès sans jeton dans l'URL avant l'arrivée d'OAuth, pour les clients web qui exigent un connecteur public.
- Téléversement ou téléchargement de fichiers de façon portable : `ui/download-file` n'est qu'au brouillon.

## Recommandation

- **#9 (chat dans le frontend) : remplacer.** Claude, ChatGPT, VS Code et Cursor fournissent déjà le chat, l'agent et, avec MCP Apps, les vues lisibles. Reconstruire un chat reviendrait à maintenir un client IA, ses clés et ses coûts.
- **#8 (frontend REST) : reporter.** Les vues MCP Apps couvrent la lecture et l'édition courante dans les clients compatibles. Rouvrir #8 quand un besoin de la liste précédente devient réel : un membre sans assistant IA, des rappels, un lien partageable ou une utilisation surtout mobile. L'API REST reste en place et partage les mêmes services; ce frontend pourra reprendre `nextStep` et la structure de la vue.
- **Suite de ce prototype.**
  1. Vérifier le rendu dans Claude Desktop et `basic-host`.
  2. Adopter `ModelContextProtocol.Extensions.Apps` quand il sera stable.
  3. Ajouter l'édition des dépendances dans la vue si l'usage le demande.

## Références

1. Spécification MCP Apps 2026-01-26 : https://github.com/modelcontextprotocol/ext-apps/blob/main/specification/2026-01-26/apps.mdx
2. Claude, MCP Apps : https://claude.com/docs/connectors/building/mcp-apps/quickstart et https://claude.com/docs/connectors/building/mcp-apps/troubleshooting
3. Matrice des clients MCP : https://modelcontextprotocol.io/extensions/client-matrix
4. SDK C#, MCP Apps : https://csharp.sdk.modelcontextprotocol.io/v2/concepts/apps/apps.html et https://www.nuget.org/packages/ModelContextProtocol.Extensions.Apps
5. Claude Code, demande de support : https://github.com/anthropics/claude-code/issues/95149
6. ChatGPT, interface des apps : https://developers.openai.com/plugins/build/chatgpt-ui
7. VS Code, support MCP Apps : https://code.visualstudio.com/blogs/2026/01/26/mcp-apps-support
8. ext-apps `basic-host` : https://github.com/modelcontextprotocol/ext-apps/tree/main/examples/basic-host
