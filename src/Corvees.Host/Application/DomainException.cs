namespace Corvees.Host.Application;

public sealed class DomainException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;

    public static DomainException Validation(string message) => new("validation_error", 400, message);
    public static DomainException NotFound(string resource) => new("not_found", 404, $"{resource} not found");
    public static DomainException InvalidState(string message) => new("invalid_state", 409, message);
    public static DomainException VersionConflict(string message = "Read the resource again") => new("version_conflict", 412, message);
    public static DomainException MissingPrecondition(string message) => new("missing_precondition", 428, message);
}
