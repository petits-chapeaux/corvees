using Corvees.Infrastructure;

namespace Corvees.Host.Application;

public sealed class MemberSession
{
    private Member? _member;

    public Member Member => _member ?? throw new InvalidOperationException("The member session is not authenticated.");
    public Guid GroupId => Member.GroupId;

    public void Authenticate(Member member) => _member = member;
}
