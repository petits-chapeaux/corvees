namespace Corvees.Host.Contracts;

public interface IVersionedResource
{
    long Version { get; }
}

public sealed record GroupResource(Guid Id, string Name, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IVersionedResource;

public sealed record MemberResource(Guid Id, Guid GroupId, string DisplayName, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt) : IVersionedResource;

public sealed record LocationResource(Guid Id, string Name, string? Address, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt) : IVersionedResource;

public sealed record ProjectLocation(Guid Id, bool Available, string? Name);

public sealed record ProjectNextStep(Guid Id, string Title, string Status);

public sealed record ProjectResource(Guid Id, string Title, string? Description, Guid? LocationId,
    ProjectLocation? Location, string Status, Guid[] Dependencies, ProjectNextStep? NextStep, long Version, long StepListVersion,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ArchivedAt, DateTimeOffset? DeletedAt) : IVersionedResource;

public sealed record StepResource(Guid Id, Guid ProjectId, string Title, string? Description, string Status,
    long Position, Guid[] Dependencies, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt) : IVersionedResource;
