using System.Text;
using Corvees.Host.Contracts;

namespace Corvees.Host.Mcp;

/// <summary>Readable summaries added after the JSON copy, for clients that do not render the MCP Apps view.</summary>
public static class ToolText
{
    private static readonly Dictionary<string, string> ProjectStatus = new()
    {
        ["planned"] = "planifié",
        ["active"] = "en cours",
        ["complete"] = "terminé",
        ["archived"] = "archivé"
    };

    private static readonly Dictionary<string, string> StepStatus = new() { ["todo"] = "à faire", ["in_progress"] = "en cours" };

    public static string? Summarize(OperationResponse response) => response switch
    {
        PagedOperationResponse { Kind: "list_projects", Data: IReadOnlyList<ProjectResource> projects } page => Projects(projects, page.NextCursor),
        { Kind: "get_project", Data: ProjectResource project } => Project(project),
        _ => null
    };

    private static string Projects(IReadOnlyList<ProjectResource> projects, string? nextCursor)
    {
        if (projects.Count == 0)
            return "Aucun projet.";
        var text = new StringBuilder(projects.Count == 1 ? "1 projet :" : $"{projects.Count} projets :");
        foreach (var project in projects)
            text.Append($"\n- {project.Title} ({ProjectStatus[project.Status]}) — {NextAction(project)}");
        if (nextCursor != null)
            text.Append("\nD'autres projets suivent : rappeler list_projects avec nextCursor.");
        return text.ToString();
    }

    private static string Project(ProjectResource project)
    {
        var text = new StringBuilder($"{project.Title} ({ProjectStatus[project.Status]})");
        if (project.Description != null)
            text.Append($"\n{project.Description}");
        if (project.Location != null)
            text.Append($"\nLieu : {(project.Location.Available ? project.Location.Name : "supprimé")}");
        if (project.Dependencies.Length > 0)
            text.Append($"\nDépend de {project.Dependencies.Length} projet(s) : {string.Join(", ", project.Dependencies)}");
        var next = NextAction(project);
        text.Append($"\n{char.ToUpperInvariant(next[0])}{next[1..]}");
        text.Append("\nLes étapes et leurs dépendances se lisent avec list_steps.");
        return text.ToString();
    }

    // Cycles are rejected, so any unfinished step set has a ready step: no next step means archived, complete or empty.
    private static string NextAction(ProjectResource project) => project switch
    {
        { NextStep: { } step } => $"prochaine étape : {step.Title} ({StepStatus[step.Status]})",
        { Status: "archived" } => "aucune action : projet archivé",
        { Status: "complete" } => "aucune action : toutes les étapes sont faites",
        _ => "aucune étape"
    };
}
