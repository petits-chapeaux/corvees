using Corvees.Host.Contracts;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Application;

public sealed class DependencyService(CorveesDbContext db, MemberSession session,
    ProjectService projects, ProjectQueries projectQueries, StepService steps)
{
    public async Task<ProjectResource> ChangeProjectAsync(Guid projectId, Guid prerequisiteId, long expectedVersion, bool add, CancellationToken ct)
    {
        var project = await projects.FindAsync(projectId, false, ct);
        ProjectService.EnsureEditable(project);
        ResourceVersions.Expect(project.Version, expectedVersion);
        var prerequisite = await projects.FindAsync(prerequisiteId, !add, ct);
        if (project.Id == prerequisite.Id)
            throw new DomainException("dependency_cycle", 409, "A project cannot depend on itself");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var link = await db.ProjectDependencies.SingleOrDefaultAsync(x => x.ProjectId == project.Id && x.PrerequisiteId == prerequisite.Id, ct);
        if (add)
        {
            if (link != null)
                throw new DomainException("duplicate_dependency", 409, "Dependency already exists");
            var links = await db.ProjectDependencies.Select(x => new { x.ProjectId, x.PrerequisiteId }).ToListAsync(ct);
            if (DependencyGraph.Reaches(prerequisite.Id, project.Id, links.Select(x => (x.ProjectId, x.PrerequisiteId))))
                throw new DomainException("dependency_cycle", 409, "Dependency would form a cycle");
            db.ProjectDependencies.Add(new ProjectDependency { GroupId = session.GroupId, ProjectId = project.Id, PrerequisiteId = prerequisite.Id });
        }
        else
        {
            if (link == null)
                throw DomainException.NotFound("Dependency");
            db.ProjectDependencies.Remove(link);
        }
        ResourceVersions.Touch(project);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await projectQueries.GetAsync(project.Id, false, ct);
    }

    public async Task<StepResource> ChangeStepAsync(Guid projectId, Guid stepId, Guid prerequisiteId, long expectedVersion, bool add, CancellationToken ct)
    {
        var project = await projects.FindAsync(projectId, false, ct);
        ProjectService.EnsureEditable(project);
        var step = await steps.FindAsync(project.Id, stepId, false, ct);
        ResourceVersions.Expect(step.Version, expectedVersion);
        var prerequisite = await steps.FindAsync(project.Id, prerequisiteId, !add, ct);
        if (step.Id == prerequisite.Id)
            throw new DomainException("dependency_cycle", 409, "A step cannot depend on itself");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var link = await db.StepDependencies.SingleOrDefaultAsync(x => x.ProjectId == project.Id && x.StepId == step.Id && x.PrerequisiteId == prerequisite.Id, ct);
        if (add)
        {
            if (link != null)
                throw new DomainException("duplicate_dependency", 409, "Dependency already exists");
            var links = await db.StepDependencies.Where(x => x.ProjectId == project.Id)
                .Select(x => new { x.StepId, x.PrerequisiteId }).ToListAsync(ct);
            if (DependencyGraph.Reaches(prerequisite.Id, step.Id, links.Select(x => (x.StepId, x.PrerequisiteId))))
                throw new DomainException("dependency_cycle", 409, "Dependency would form a cycle");
            db.StepDependencies.Add(new StepDependency { GroupId = session.GroupId, ProjectId = project.Id, StepId = step.Id, PrerequisiteId = prerequisite.Id });
        }
        else
        {
            if (link == null)
                throw DomainException.NotFound("Dependency");
            db.StepDependencies.Remove(link);
        }
        ResourceVersions.Touch(step);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await steps.GetAsync(project.Id, step.Id, false, ct);
    }

    private Task<int> LockAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({session.GroupId.ToString()}, 0))", ct);
}
