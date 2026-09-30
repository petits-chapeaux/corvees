using System.Text.Json.Nodes;
using Corvees.Host.Application;

namespace Corvees.Host.Contracts;

public sealed class OperationArguments(JsonObject values)
{
    public bool Boolean(string key)
    {
        if (values[key] is null)
            return false;
        if (values[key] is not JsonValue raw || !raw.TryGetValue<bool>(out var value))
            throw DomainException.Validation($"{key} must be a boolean");
        return value;
    }

    public Guid Id(string key)
    {
        if (values[key] is not JsonValue raw || !raw.TryGetValue<string>(out var text) || !Guid.TryParse(text, out var id) || id == Guid.Empty)
            throw DomainException.Validation($"{key} must be a UUID");
        return id;
    }

    public Guid? OptionalId(string key) => values[key] is null ? null : Id(key);

    public long PositiveInteger(string key)
    {
        if (values[key] is not JsonValue raw || !raw.TryGetValue<long>(out var number) || number < 1)
            throw DomainException.Validation($"{key} must be a positive integer");
        return number;
    }

    public long Version(string key = "expectedVersion")
    {
        if (values[key] is null)
            throw DomainException.MissingPrecondition($"{key} is required");
        return PositiveInteger(key);
    }

    public string Text(string key, int maxLength = 200)
    {
        var text = OptionalText(key, maxLength);
        if (text is null)
            throw DomainException.Validation($"{key} must be 1 to {maxLength} characters");
        return text;
    }

    public string? OptionalNonEmptyText(string key, int maxLength) => values[key] is null ? null : Text(key, maxLength);

    public string? OptionalText(string key, int maxLength)
    {
        if (values[key] is null)
            return null;
        if (values[key] is not JsonValue raw || !raw.TryGetValue<string>(out var text))
            throw DomainException.Validation($"{key} must be text or null");
        var trimmed = text?.Trim();
        if (trimmed?.Length > maxLength)
            throw DomainException.Validation($"{key} exceeds {maxLength} characters");
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public Patch<string> TextPatch(string key, int maxLength = 200) =>
        values.ContainsKey(key) ? new(true, Text(key, maxLength)) : default;

    public Patch<string?> OptionalTextPatch(string key, int maxLength) =>
        values.ContainsKey(key) ? new(true, OptionalText(key, maxLength)) : default;

    public Patch<Guid?> IdPatch(string key) => values.ContainsKey(key) ? new(true, OptionalId(key)) : default;

    public PageRequest Page() => new(values["limit"] is null ? 50 : PositiveInteger("limit"), Cursor());

    private string? Cursor()
    {
        if (values["cursor"] is null)
            return null;
        if (values["cursor"] is not JsonValue raw || !raw.TryGetValue<string>(out var cursor) || string.IsNullOrEmpty(cursor) || cursor.Length > 4096)
            throw DomainException.Validation("Invalid cursor");
        return cursor;
    }
}
