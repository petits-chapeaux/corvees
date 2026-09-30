using System.Text.Json.Serialization;

namespace Corvees.Host.Contracts;

public record OperationResponse(string Kind, object Data)
{
    public int SchemaVersion => 1;

    [JsonIgnore]
    public long? Version => (Data as IVersionedResource)?.Version;
}

public sealed record PagedOperationResponse(string Kind, object Data, string? NextCursor) : OperationResponse(Kind, Data);
