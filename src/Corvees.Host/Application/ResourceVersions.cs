using Corvees.Infrastructure;

namespace Corvees.Host.Application;

internal static class ResourceVersions
{
    public static void Expect(long actual, long expected)
    {
        if (actual != expected)
            throw DomainException.VersionConflict("The resource changed. Read it again before writing.");
    }

    public static void Touch(Group group)
    {
        group.Version++;
        group.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static void Touch(Member member)
    {
        member.Version++;
        member.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static void Touch(Location location)
    {
        location.Version++;
        location.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static void Touch(Project project)
    {
        project.Version++;
        project.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static void Touch(Step step)
    {
        step.Version++;
        step.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static void TouchList(Project project)
    {
        project.StepListVersion++;
        project.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
