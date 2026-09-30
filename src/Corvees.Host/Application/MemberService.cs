using Corvees.Host.Contracts;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Application;

public sealed class MemberService(CorveesDbContext db, MemberSession session)
{
    public Task<Page<MemberResource>> ListAsync(PageRequest page, CancellationToken ct) =>
        Page<MemberResource>.ReadAsync(db.Members.Where(x => x.DeletedAt == null)
            .OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
            .Select(x => new MemberResource(x.Id, x.GroupId, x.DisplayName, x.Version, x.CreatedAt, x.UpdatedAt, x.DeletedAt)), page, ct);

    public MemberResource GetMe() => ToResource(session.Member);

    public async Task<MemberResource> UpdateMeAsync(string displayName, long expectedVersion, CancellationToken ct)
    {
        var member = await db.Members.SingleAsync(x => x.Id == session.Member.Id, ct);
        ResourceVersions.Expect(member.Version, expectedVersion);
        member.DisplayName = displayName;
        ResourceVersions.Touch(member);
        await db.SaveChangesAsync(ct);
        return ToResource(member);
    }

    private static MemberResource ToResource(Member member) =>
        new(member.Id, member.GroupId, member.DisplayName, member.Version, member.CreatedAt, member.UpdatedAt, member.DeletedAt);
}
