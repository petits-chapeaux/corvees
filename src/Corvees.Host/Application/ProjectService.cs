using Corvees.Host.Contracts;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Application;

public sealed class ProjectService(CorveesDbContext db, MemberSession session, LocationService locations, ProjectQueries queries)
{
    public async Task<ProjectResource> CreateAsync(string title, string? description, Guid? locationId, CancellationToken ct)
    {
        if (locationId != null)
            await locations.FindAsync(locationId.Value, false, ct);
        var project = new Project { GroupId = session.GroupId, Title = title, Description = description, LocationId = locationId };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(project.Id, false, ct);
    }

    public async Task<ProjectResource> UpdateAsync(Guid id, ProjectUpdate update, long expectedVersion, CancellationToken ct)
    {
        var project = await FindAsync(id, false, ct);
        EnsureEditable(project);
        ResourceVersions.Expect(project.Version, expectedVersion);
        if (update.Title.IsSpecified)
            project.Title = update.Title.Value;
        if (update.Description.IsSpecified)
            project.Description = update.Description.Value;
        if (update.LocationId.IsSpecified)
        {
            if (update.LocationId.Value is { } locationId)
                await locations.FindAsync(locationId, false, ct);
            project.LocationId = update.LocationId.Value;
        }
        ResourceVersions.Touch(project);
        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(project.Id, false, ct);
    }

    public async Task<ProjectResource> SetArchivedAsync(Guid id, bool archived, long expectedVersion, CancellationToken ct)
    {
        var project = await FindAsync(id, false, ct);
        ResourceVersions.Expect(project.Version, expectedVersion);
        if ((project.ArchivedAt != null) == archived)
            throw DomainException.InvalidState("Project is already in that state");
        project.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        ResourceVersions.Touch(project);
        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(project.Id, false, ct);
    }

    public async Task<ProjectResource> DeleteAsync(Guid id, long expectedVersion, CancellationToken ct)
    {
        var project = await FindAsync(id, false, ct);
        ResourceVersions.Expect(project.Version, expectedVersion);
        project.DeletedAt = DateTimeOffset.UtcNow;
        ResourceVersions.Touch(project);
        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(project.Id, true, ct);
    }

    public async Task<ProjectResource> RestoreAsync(Guid id, long expectedVersion, CancellationToken ct)
    {
        var project = await FindAsync(id, true, ct);
        ResourceVersions.Expect(project.Version, expectedVersion);
        if (project.DeletedAt == null)
            throw DomainException.InvalidState("Project is not deleted");
        project.DeletedAt = null;
        ResourceVersions.Touch(project);
        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(project.Id, false, ct);
    }

    internal async Task<Project> FindAsync(Guid id, bool includeDeleted, CancellationToken ct) =>
        await db.Projects.SingleOrDefaultAsync(x => x.Id == id && (includeDeleted || x.DeletedAt == null), ct)
        ?? throw DomainException.NotFound("Project");

    internal static void EnsureEditable(Project project)
    {
        if (project.ArchivedAt != null)
            throw DomainException.InvalidState("Unarchive the project before editing it");
    }
}
