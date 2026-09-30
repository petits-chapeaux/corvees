using Corvees.Host.Contracts;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Application;

public sealed class LocationService(CorveesDbContext db, MemberSession session)
{
    public Task<Page<LocationResource>> ListAsync(PageRequest page, bool deletedOnly, CancellationToken ct) =>
        Page<LocationResource>.ReadAsync(db.Locations.Where(x => (x.DeletedAt != null) == deletedOnly)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new LocationResource(x.Id, x.Name, x.Address, x.Version, x.CreatedAt, x.UpdatedAt, x.DeletedAt)), page, ct);

    public async Task<LocationResource> GetAsync(Guid id, bool includeDeleted, CancellationToken ct) =>
        ToResource(await FindAsync(id, includeDeleted, ct));

    public async Task<LocationResource> CreateAsync(string name, string? address, CancellationToken ct)
    {
        var location = new Location { GroupId = session.GroupId, Name = name, Address = address };
        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return ToResource(location);
    }

    public async Task<LocationResource> UpdateAsync(Guid id, LocationUpdate update, long expectedVersion, CancellationToken ct)
    {
        var location = await FindAsync(id, false, ct);
        ResourceVersions.Expect(location.Version, expectedVersion);
        if (update.Name.IsSpecified)
            location.Name = update.Name.Value;
        if (update.Address.IsSpecified)
            location.Address = update.Address.Value;
        ResourceVersions.Touch(location);
        await db.SaveChangesAsync(ct);
        return ToResource(location);
    }

    public async Task<LocationResource> DeleteAsync(Guid id, long expectedVersion, CancellationToken ct)
    {
        var location = await FindAsync(id, false, ct);
        ResourceVersions.Expect(location.Version, expectedVersion);
        location.DeletedAt = DateTimeOffset.UtcNow;
        ResourceVersions.Touch(location);
        await db.SaveChangesAsync(ct);
        return ToResource(location);
    }

    public async Task<LocationResource> RestoreAsync(Guid id, long expectedVersion, CancellationToken ct)
    {
        var location = await FindAsync(id, true, ct);
        ResourceVersions.Expect(location.Version, expectedVersion);
        if (location.DeletedAt == null)
            throw DomainException.InvalidState("Location is not deleted");
        location.DeletedAt = null;
        ResourceVersions.Touch(location);
        await db.SaveChangesAsync(ct);
        return ToResource(location);
    }

    internal async Task<Location> FindAsync(Guid id, bool includeDeleted, CancellationToken ct) =>
        await db.Locations.SingleOrDefaultAsync(x => x.Id == id && (includeDeleted || x.DeletedAt == null), ct)
        ?? throw DomainException.NotFound("Location");

    private static LocationResource ToResource(Location location) =>
        new(location.Id, location.Name, location.Address, location.Version, location.CreatedAt, location.UpdatedAt, location.DeletedAt);
}
