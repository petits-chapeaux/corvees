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
- **La conversation comme interface.** Les données restent structurées, mais on peut interagir avec elles naturellement.
- **Pas de comptabilité morale.** Le temps et les contributions peuvent être visibles sans devenir un système de dette entre membres.

## Piste technique

Le backend serait conçu **MCP-first**. Chaque capacité — projets, séances, décisions, tâches, outils et inventaire — serait exposée comme une ressource ou un outil MCP. Cela permettrait d’expérimenter avec différents clients et agents, tout en explorant cette architecture sur un projet concret.

Rien n’est encore figé : modèle de données, stockage, interface et choix de frameworks restent à discuter.

## Première version possible

1. Créer ou rejoindre un groupe sans avoir à ouvrir de compte.
2. Regrouper les membres, les projets et les outils au sein de ce groupe.
3. Créer et structurer un projet à partir d’une conversation.
4. Afficher les projets et leurs prochaines actions prioritaires.
5. Enregistrer le compte rendu d’une séance et ajuster la suite.
6. Maintenir un inventaire partagé des outils disponibles ou recherchés.

## Questions ouvertes

- Quel niveau de suivi rend service sans devenir lourd?
- Quelle serait la plus petite expérience utile à construire en premier?
- Quels frameworks voudrions-nous essayer?

## Prochaines étapes

- Créer des ADR pour choisir les frameworks :
  - LLM;
  - backend;
  - frontend.
- Créer la structure du repo.
