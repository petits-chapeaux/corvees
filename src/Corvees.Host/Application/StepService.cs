using Corvees.Host.Contracts;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Application;

public sealed class StepService(CorveesDbContext db, MemberSession session, ProjectService projects)
{
    public async Task<Page<StepResource>> ListAsync(Guid projectId, PageRequest page, bool deletedOnly, CancellationToken ct)
    {
        await projects.FindAsync(projectId, false, ct);
        return await Page<StepResource>.ReadAsync(Project(db.Steps
            .Where(x => x.ProjectId == projectId && (x.DeletedAt != null) == deletedOnly)
            .OrderBy(x => x.Position).ThenBy(x => x.Id)), page, ct);
    }

    public async Task<StepResource> GetAsync(Guid projectId, Guid id, bool includeDeleted, CancellationToken ct)
    {
        await projects.FindAsync(projectId, false, ct);
        return await Project(db.Steps.Where(x => x.ProjectId == projectId && x.Id == id && (includeDeleted || x.DeletedAt == null)))
            .SingleOrDefaultAsync(ct) ?? throw DomainException.NotFound("Step");
    }

    public async Task<StepResource> CreateAsync(Guid projectId, string title, string? description, long expectedListVersion, CancellationToken ct)
    {
        var project = await ListWriteProjectAsync(projectId, expectedListVersion, ct);
        var max = await db.Steps.Where(x => x.ProjectId == project.Id).MaxAsync(x => (long?)x.Position, ct) ?? 0;
        var step = new Step
        {
            GroupId = session.GroupId,
            ProjectId = project.Id,
            Title = title,
            Description = description,
            Position = max + 1
        };
        db.Steps.Add(step);
        ResourceVersions.TouchList(project);
        await db.SaveChangesAsync(ct);
        return await GetAsync(project.Id, step.Id, false, ct);
    }

    public async Task<StepResource> UpdateAsync(Guid projectId, Guid id, StepUpdate update, long expectedVersion, CancellationToken ct)
    {
        var project = await projects.FindAsync(projectId, false, ct);
        ProjectService.EnsureEditable(project);
        var step = await FindAsync(project.Id, id, false, ct);
        ResourceVersions.Expect(step.Version, expectedVersion);
        if (update.Title.IsSpecified)
            step.Title = update.Title.Value;
        if (update.Description.IsSpecified)
            step.Description = update.Description.Value;
        if (update.Status.IsSpecified)
        {
            if (update.Status.Value is not ("todo" or "in_progress" or "done"))
                throw DomainException.Validation("Unknown step status");
            step.Status = update.Status.Value;
        }
        ResourceVersions.Touch(step);
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(project.Id, step.Id, false, ct);
    }

    public async Task<StepResource> MoveAsync(Guid projectId, Guid id, Guid targetId, string placement, long expectedListVersion, CancellationToken ct)
    {
        var project = await ListWriteProjectAsync(projectId, expectedListVersion, ct);
        var step = await FindAsync(project.Id, id, false, ct);
        var target = await FindAsync(project.Id, targetId, false, ct);
        if (step.Id == target.Id)
            throw DomainException.Validation("A step cannot move relative to itself");
        if (placement is not ("before" or "after"))
            throw DomainException.Validation("placement must be before or after");
        var all = await db.Steps.Where(x => x.ProjectId == project.Id).OrderBy(x => x.Position).ToListAsync(ct);
        var ordered = all.Where(x => x.Id != step.Id).ToList();
        var index = ordered.FindIndex(x => x.Id == target.Id);
        ordered.Insert(index + (placement == "after" ? 1 : 0), step);
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Position = i + 1;
        ResourceVersions.TouchList(project);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS uq_steps_project_position DEFERRED", ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(project.Id, step.Id, false, ct);
    }

    public async Task<StepResource> DeleteAsync(Guid projectId, Guid id, long expectedVersion, long expectedListVersion, CancellationToken ct)
    {
        var project = await ListWriteProjectAsync(projectId, expectedListVersion, ct);
        var step = await FindAsync(project.Id, id, false, ct);
        ResourceVersions.Expect(step.Version, expectedVersion);
        step.DeletedAt = DateTimeOffset.UtcNow;
        ResourceVersions.Touch(step);
        ResourceVersions.TouchList(project);
        await db.SaveChangesAsync(ct);
        return await GetAsync(project.Id, step.Id, true, ct);
    }

    public async Task<StepResource> RestoreAsync(Guid projectId, Guid id, long expectedVersion, long expectedListVersion, CancellationToken ct)
    {
        var project = await ListWriteProjectAsync(projectId, expectedListVersion, ct);
        var step = await FindAsync(project.Id, id, true, ct);
        ResourceVersions.Expect(step.Version, expectedVersion);
        if (step.DeletedAt == null)
            throw DomainException.InvalidState("Step is not deleted");
        step.DeletedAt = null;
        ResourceVersions.Touch(step);
        ResourceVersions.TouchList(project);
        await db.SaveChangesAsync(ct);
        return await GetAsync(project.Id, step.Id, false, ct);
    }

    internal async Task<Step> FindAsync(Guid projectId, Guid id, bool includeDeleted, CancellationToken ct) =>
        await db.Steps.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == id && (includeDeleted || x.DeletedAt == null), ct)
        ?? throw DomainException.NotFound("Step");

    private async Task<Project> ListWriteProjectAsync(Guid projectId, long expectedListVersion, CancellationToken ct)
    {
        var project = await projects.FindAsync(projectId, false, ct);
        ProjectService.EnsureEditable(project);
        ResourceVersions.Expect(project.StepListVersion, expectedListVersion);
        return project;
    }

    private IQueryable<StepResource> Project(IQueryable<Step> steps) => steps.Select(step => new StepResource(
        step.Id, step.ProjectId, step.Title, step.Description, step.Status, step.Position,
        db.StepDependencies.Where(link => link.ProjectId == step.ProjectId && link.StepId == step.Id &&
                db.Steps.Any(prerequisite => prerequisite.Id == link.PrerequisiteId && prerequisite.DeletedAt == null))
            .OrderBy(link => link.PrerequisiteId).Select(link => link.PrerequisiteId).ToArray(),
        step.Version, step.CreatedAt, step.UpdatedAt, step.DeletedAt));
}
