using System.Globalization;
using System.Text.Json.Nodes;
using Corvees.Host.Application;
using Corvees.Host.Contracts;

namespace Corvees.Host.Http;

public static class VersionHeaders
{
    public static void Apply(IHeaderDictionary headers, OperationDefinition operation, JsonObject arguments)
    {
        if (operation.Required.Contains("expectedVersion"))
            arguments["expectedVersion"] = Parse(headers.IfMatch.ToString(), "If-Match");
        if (operation.Required.Contains("expectedListVersion"))
        {
            var header = operation.Required.Contains("expectedVersion") ? "X-Step-List-Version" : "If-Match";
            arguments["expectedListVersion"] = Parse(headers[header].ToString(), header);
        }
    }

    public static long Parse(string value, string header)
    {
        if (!long.TryParse(value.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw DomainException.MissingPrecondition($"A valid {header} version is required");
        return version;
    }
}
