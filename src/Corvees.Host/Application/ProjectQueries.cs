using Corvees.Host.Contracts;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Application;

public sealed class ProjectQueries(CorveesDbContext db)
{
    public Task<Page<ProjectResource>> ListAsync(PageRequest page, bool deletedOnly, string? status, Guid? locationId, CancellationToken ct)
    {
        var projects = db.Projects.Where(x => (x.DeletedAt != null) == deletedOnly);
        if (locationId != null)
            projects = projects.Where(x => x.LocationId == locationId);

        projects = status switch
        {
            null => projects,
            "archived" => projects.Where(x => x.ArchivedAt != null),
            "planned" => projects.Where(x => x.ArchivedAt == null &&
                !db.Steps.Any(s => s.ProjectId == x.Id && s.DeletedAt == null && s.Status != "todo")),
            "complete" => projects.Where(x => x.ArchivedAt == null &&
                db.Steps.Any(s => s.ProjectId == x.Id && s.DeletedAt == null) &&
                !db.Steps.Any(s => s.ProjectId == x.Id && s.DeletedAt == null && s.Status != "done")),
            "active" => projects.Where(x => x.ArchivedAt == null &&
                db.Steps.Any(s => s.ProjectId == x.Id && s.DeletedAt == null && s.Status != "todo") &&
                db.Steps.Any(s => s.ProjectId == x.Id && s.DeletedAt == null && s.Status != "done")),
            _ => throw DomainException.Validation("Unknown project status")
        };

        return Page<ProjectResource>.ReadAsync(Project(projects.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)), page, ct);
    }

    public async Task<ProjectResource> GetAsync(Guid id, bool includeDeleted, CancellationToken ct) =>
        await Project(db.Projects.Where(x => x.Id == id && (includeDeleted || x.DeletedAt == null))).SingleOrDefaultAsync(ct)
        ?? throw DomainException.NotFound("Project");

    private IQueryable<ProjectResource> Project(IQueryable<Project> projects) => projects.Select(project => new ProjectResource(
        project.Id, project.Title, project.Description, project.LocationId,
        db.Locations.Where(location => location.Id == project.LocationId)
            .Select(location => new ProjectLocation(location.Id, location.DeletedAt == null, location.DeletedAt == null ? location.Name : null))
            .FirstOrDefault(),
        project.ArchivedAt != null ? "archived" :
            db.Steps.Any(step => step.ProjectId == project.Id && step.DeletedAt == null) &&
            !db.Steps.Any(step => step.ProjectId == project.Id && step.DeletedAt == null && step.Status != "done") ? "complete" :
            db.Steps.Any(step => step.ProjectId == project.Id && step.DeletedAt == null && step.Status != "todo") ? "active" : "planned",
        db.ProjectDependencies.Where(link => link.ProjectId == project.Id &&
                db.Projects.Any(prerequisite => prerequisite.Id == link.PrerequisiteId && prerequisite.DeletedAt == null))
            .OrderBy(link => link.PrerequisiteId).Select(link => link.PrerequisiteId).ToArray(),
        db.Steps.Where(step => project.ArchivedAt == null && step.ProjectId == project.Id && step.DeletedAt == null && step.Status != "done" &&
                !db.StepDependencies.Any(link => link.StepId == step.Id &&
                    db.Steps.Any(prerequisite => prerequisite.Id == link.PrerequisiteId && prerequisite.DeletedAt == null && prerequisite.Status != "done")))
            .OrderBy(step => step.Status == "in_progress" ? 0 : 1).ThenBy(step => step.Position)
            .Select(step => new ProjectNextStep(step.Id, step.Title, step.Status))
            .FirstOrDefault(),
        project.Version, project.StepListVersion, project.CreatedAt, project.UpdatedAt, project.ArchivedAt, project.DeletedAt));
}
