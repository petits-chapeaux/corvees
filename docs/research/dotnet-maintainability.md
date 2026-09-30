# Maintenabilité du backend .NET

Recherche et application à la [revue de la PR 7](https://github.com/petits-chapeaux/corvees/pull/7#pullrequestreview-5360053865), le 2026-09-29.

## Structure retenue

Conserver les deux projets de production. Aucun besoin actuel de MediatR, de dépôt générique ou d'une multiplication des couches. Les contrôleurs sont un choix d'organisation demandé en revue, pas une exigence de performance : ASP.NET Core prend en charge les contrôleurs et les Minimal APIs [1].

```text
Corvees.Host/
  Program.cs          Composition et ordre du pipeline
  Hosting/            Enregistrement DI, configuration, sondes
  Authentication/     Authentification du membre et initialisation du groupe
  Controllers/        Routes REST par ressource
  Http/               Lecture HTTP, préconditions, rendu des erreurs
  Mcp/                Gestionnaire MCP et schémas explicites
  Contracts/          Opérations publiques, validation JSON, réponses typées
  BusinessService.cs  Adaptation commune des opérations vers les services
  Application/        Groupes, membres, lieux, projets, étapes, dépendances
Corvees.Infrastructure/
  CorveesDbContext.cs  Mappings, filtres de groupe, contraintes
  Migrations/         Historique du stockage
```

`BusinessService` ne contient plus de règles métier ni de requêtes. Les deux transports utilisent sa validation commune; les services reçoivent des valeurs typées et gardent les transactions et invariants. Le catalogue donne explicitement le nom public, la ressource et les champs de chaque opération; les schémas ne déduisent plus la ressource de fragments du nom.

## Choix étayés

- **Présence distincte de null.** Les annotations nullable ne distinguent pas une propriété absente d'un `null` explicite [2]. `OperationArguments` conserve cette distinction dans `Patch<T>` : absent ne change rien, `null` efface un champ optionnel. Les entités EF et les secrets ne sont jamais sérialisés.
- **Une portée par requête.** Le middleware conventionnel reçoit `DbContext` et `MemberSession` dans `InvokeAsync`, pas dans son constructeur [3]. Le gestionnaire MCP résout le service depuis la requête HTTP courante, jamais depuis un singleton. Le routage précède l'authentification pour rendre le jeton MCP disponible.
- **Requêtes bornées.** EF recommande de filtrer, projeter et limiter avant matérialisation [4]. Les listes appliquent le tri, `Skip` et `Take(limit + 1)` en SQL. Les projections de projets et d'étapes remplacent les requêtes par élément. Le statut calculé est filtré avant pagination. Le curseur à décalage reste compatible avec l'ADR 0002; aucune promesse de stabilité entre mutations.
- **Concurrence préservée.** Les opérations asynchrones sur un même `DbContext` restent séquentielles [5]. Les jetons de concurrence EF, le verrou consultatif PostgreSQL des dépendances et la contrainte différable des positions restent en place.
- **Contrats explicites.** Le SDK MCP 2.2.0 permet les sorties structurées et les schémas typés [6]. Le catalogue manuel reste ici le choix prudent pour conserver les noms, constantes, champs nullable obligatoires et annotations v1. REST et MCP rendent séparément les mêmes erreurs métier; aucun passage implicite à ProblemDetails.
- **Tests avec le vrai moteur.** EF déconseille de supposer qu'InMemory ou SQLite reproduisent les règles du fournisseur réel [7]. Les tests HTTP/MCP utilisent `WebApplicationFactory` [8] et PostgreSQL pour la concurrence, les filtres et les contraintes. Sans configuration PostgreSQL, ils sont signalés comme ignorés, jamais comme réussis.

## Garde-fous livrés

Tests des enveloppes et schémas, de la parité REST/MCP, des versions/ETag, des PATCH, des groupes isolés et jetons révoqués, des cycles concurrents et de la pagination SQL sans requêtes par élément. Les filtres mal formés et curseurs hors plage retournent une erreur de validation; les identifiants d'URL et versions d'en-tête restent prioritaires sur le corps.

`.editorconfig`, les avertissements traités comme erreurs et `dotnet format --verify-no-changes` en CI rendent le style vérifiable [9]. `NuGet.Config` limite la restauration à nuget.org pour ne pas dépendre des sources privées configurées sur une machine.

Les ADR existantes restent inchangées. Une séparation Domain/Application/Infrastructure supplémentaire, la génération des schémas depuis les DTO et une pagination par clé pourront être évaluées lorsqu'un besoin concret justifiera leur coût. Les tests de nombre de requêtes sont des garde-fous, pas un benchmark de performance.

## Sources primaires

1. [API avec contrôleurs ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0)
2. [System.Text.Json : nullabilité et propriétés absentes](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations)
3. [Middleware et dépendances scoped](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/write?view=aspnetcore-10.0)
4. [EF Core : requêtes efficaces](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
5. [EF Core : programmation asynchrone](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async)
6. [SDK MCP C# 2.2.0 : outils](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/tools/tools.md)
7. [EF Core : stratégie de tests](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)
8. [Tests d'intégration ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
9. [Configuration des analyseurs .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files)
