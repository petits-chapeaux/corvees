namespace Corvees.Host.Application;

public readonly record struct Patch<T>(bool IsSpecified, T Value);

public sealed record LocationUpdate(Patch<string> Name, Patch<string?> Address);

public sealed record ProjectUpdate(Patch<string> Title, Patch<string?> Description, Patch<Guid?> LocationId);

public sealed record StepUpdate(Patch<string> Title, Patch<string?> Description, Patch<string> Status);
