using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corvees.Host.Application;
using Corvees.Host.Contracts;

namespace Corvees.Host.Http;

public static class ApiRequestReader
{
    public static async Task<JsonObject> ReadAsync(HttpRequest request, OperationDefinition operation, CancellationToken ct)
    {
        var arguments = await ReadBodyAsync(request, ct);
        if (HttpMethods.IsGet(request.Method))
            ReadQuery(request, operation, arguments);
        foreach (var (key, value) in request.RouteValues)
        {
            if (key is "projectId" or "stepId" or "locationId" or "prerequisiteId")
                arguments[key] = value?.ToString();
        }
        VersionHeaders.Apply(request.Headers, operation, arguments);
        return arguments;
    }

    private static async Task<JsonObject> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.Method is not ("POST" or "PATCH") || request.ContentLength == 0)
            return new JsonObject();
        if (!request.HasJsonContentType())
        {
            if (request.ContentLength > 0 || request.Headers.TransferEncoding.Count > 0)
                throw DomainException.Validation("Content-Type must be application/json");
            return new JsonObject();
        }
        try
        {
            return await request.ReadFromJsonAsync<JsonObject>(ct) ?? new JsonObject();
        }
        catch (JsonException)
        {
            throw DomainException.Validation("Invalid JSON");
        }
    }

    private static void ReadQuery(HttpRequest request, OperationDefinition operation, JsonObject arguments)
    {
        foreach (var (key, values) in request.Query)
        {
            if (!operation.Optional.Contains(key))
                continue;
            if (values.Count != 1)
                throw DomainException.Validation($"{key} must have one value");
            var value = values.ToString();
            arguments[key] = key switch
            {
                "limit" => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
                    ? JsonValue.Create(limit) : throw DomainException.Validation("limit must be a positive integer"),
                "deletedOnly" or "includeDeleted" => bool.TryParse(value, out var flag)
                    ? JsonValue.Create(flag) : throw DomainException.Validation($"{key} must be a boolean"),
                "cursor" or "status" or "locationId" => JsonValue.Create(value),
                _ => throw DomainException.Validation($"Unknown filter {key}")
            };
        }
    }
}
