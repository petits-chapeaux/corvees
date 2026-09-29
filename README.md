# Corvées

> Une coopérative de corvées, outillée sans être alourdie.

## L’idée

Une coopérative réunit un nombre variable de membres qui ont des projets, des compétences et des outils à partager. À tour de rôle, les membres se retrouvent chez l’un d’eux pour faire avancer un chantier : sabler un plafond, ajuster un plancher flottant, améliorer une terrasse, ou simplement donner le coup de main qui débloque la suite.

**Corvées** soutient cette pratique collective avec un outil simple : garder la mémoire de ce qui a été fait, rendre visible ce qui vient ensuite et aider à préparer les journées de travail.

## Ce que la plateforme pourrait faire

- recueillir un projet encore flou et le transformer en vision, besoins, étapes et critères de réussite;
- maintenir un backlog commun et aider à choisir la prochaine priorité;
- regrouper les disponibilités des membres et proposer les meilleurs moments pour se réunir;
- préparer une séance de travail : tâches, matériel, outils et décisions à prendre;
- consigner les avancées, décisions et prochaines étapes après chaque séance;
- inventorier les outils de chacun, leur emplacement et leur disponibilité;
- repérer les outils manquants et convenir de leur achat ou de leur contribution.

L’expérience serait d’abord conversationnelle. On pourrait écrire, par exemple :

> On a terminé le sablage aujourd’hui, mais deux planches doivent être remplacées avant de vernir. Qu’est-ce qu’on prépare pour la prochaine fois?

Le système mettrait alors le projet à jour et proposerait une suite concrète.

## Quelques principes

- **Le collectif avant la productivité.** L’outil doit faciliter les moments partagés, pas transformer les corvées en gestion de performance.
- **Assez de structure, pas trop.** Garder une trace utile sans imposer une bureaucratie.
- **Souple par défaut.** Une liste priorisée peut être plus juste qu’un échéancier rigide.
- **Centré sur la progression.** L’outil doit encourager le groupe en soulignant les avancées, sans masquer les suivis urgents.
- **La conversation comme interface.** Les données restent structurées, mais on peut interagir avec elles naturellement.
- **Pas de comptabilité morale.** Le temps et les contributions peuvent être visibles sans devenir un système de dette entre membres.

## Piste technique

Le backend serait conçu **MCP-first**. Chaque capacité — projets, séances, décisions, tâches, outils et inventaire — serait exposée comme une ressource ou un outil MCP. Cela permettrait d’expérimenter avec différents clients et agents, tout en explorant cette architecture sur un projet concret.

La documentation du projet serait en français. Le code serait en anglais et le produit serait conçu pour être internationalisé.

Rien n’est encore figé : modèle de données, stockage, interface et choix de frameworks restent à discuter.

## Première version possible

1. Créer ou rejoindre un groupe sans avoir à ouvrir de compte.
2. Regrouper les membres, les projets et les outils au sein de ce groupe.
3. Créer et structurer un projet à partir d’une conversation.
4. Afficher les projets et leurs prochaines actions prioritaires.
5. Enregistrer le compte rendu d’une séance et ajuster la suite.
6. Maintenir un inventaire partagé des outils disponibles ou recherchés.
7. Regrouper les disponibilités et planifier une séance de travail.

## Questions ouvertes

- Quel niveau de suivi rend service sans devenir lourd?
- Quelle serait la plus petite expérience utile à construire en premier?
- Kotlin, son SDK MCP et une approche fonctionnelle conviendraient-ils au backend?
- L’interface initiale devrait-elle être un chatbot IA existant connecté par MCP plutôt qu’un frontend dédié?

## Développement local

Prérequis : SDK .NET 10, OpenSSL, `psql` et Docker Compose (ou Podman avec une machine démarrée). L'[ADR 0001](docs/adr/0001-stack-backend-mcp.md) choisit la stack; l'[ADR 0002](docs/adr/0002-project-tools-and-schemas.md) définit les groupes, projets, étapes, lieux, dépendances et contrats MCP/REST.

```sh
./scripts/dev.sh
```

Le script démarre Postgres, restaure l'outil EF, applique les migrations et crée un groupe et un premier membre de développement; il affiche leur jeton **une seule fois** et l'URL MCP. Chaque exécution crée un nouveau groupe local. Le serveur écoute sur `http://localhost:5169`. Vérifier `/healthz`, `/readyz` et la racine publique `/api/v1`. Les autres routes REST demandent `Authorization: Bearer <jeton>`.

```sh
export MEMBER_TOKEN='<jeton affiché par le script>'
curl -H "Authorization: Bearer $MEMBER_TOKEN" http://localhost:5169/api/v1/projects
curl -sS -X POST "http://localhost:5169/m/$MEMBER_TOKEN/mcp" \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'
```

Pour provisionner en production, utiliser `scripts/admin.sh create-group NOM PREMIER_MEMBRE`, `add-member GROUPE_UUID NOM`, `rotate-token MEMBRE_UUID`, `delete-member MEMBRE_UUID` et `restore-member MEMBRE_UUID` avec les variables PostgreSQL d'un opérateur. Aucune route d'administration n'est publique. `dotnet test Corvees.slnx` exécute les tests sans base par défaut; définir `CORVEES_TEST_DATABASE_URL` pour les tests d'intégration PostgreSQL. `docker compose down -v` efface les données locales. Le mot de passe et le HTTP en clair sont réservés au loopback. En production, configurer `ConnectionStrings__Corvees`, `AllowedHosts`, TLS et le masquage des URL MCP; jamais journaliser ou partager les jetons.

## Prochaines étapes

- Créer des ADR pour choisir :
  - l’intégration LLM et MCP;
  - le backend, le stockage et l’infrastructure;
  - les interfaces clientes, dont un chatbot IA connecté par MCP ou un frontend dédié.
- Créer la structure du repo.
