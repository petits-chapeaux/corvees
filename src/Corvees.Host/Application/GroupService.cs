using Corvees.Host.Contracts;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Application;

public sealed class GroupService(CorveesDbContext db, MemberSession session)
{
    public async Task<GroupResource> GetAsync(CancellationToken ct) => ToResource(await FindAsync(ct));

    public async Task<GroupResource> UpdateAsync(string name, long expectedVersion, CancellationToken ct)
    {
        var group = await FindAsync(ct);
        ResourceVersions.Expect(group.Version, expectedVersion);
        group.Name = name;
        ResourceVersions.Touch(group);
        await db.SaveChangesAsync(ct);
        return ToResource(group);
    }

    private Task<Group> FindAsync(CancellationToken ct) => db.Groups.SingleAsync(x => x.Id == session.GroupId, ct);

    private static GroupResource ToResource(Group group) => new(group.Id, group.Name, group.Version, group.CreatedAt, group.UpdatedAt);
}
