using System.Text.Json;
using System.Text.Json.Nodes;
using Corvees.Host.Application;
using Corvees.Host.Contracts;
using Corvees.Host.Http;
using Corvees.Host.Mcp;
using Microsoft.AspNetCore.Http;

public sealed class ContractTests
{
    [Fact]
    public void PatchesDistinguishAbsentNullAndValue()
    {
        var absent = new OperationArguments(new JsonObject());
        Assert.False(absent.OptionalTextPatch("description", 10000).IsSpecified);
        Assert.False(absent.IdPatch("locationId").IsSpecified);

        var cleared = Arguments("""{"description":null,"locationId":null}""");
        Assert.Equal(new Patch<string?>(true, null), cleared.OptionalTextPatch("description", 10000));
        Assert.Equal(new Patch<Guid?>(true, null), cleared.IdPatch("locationId"));
        Assert.Equal(new Patch<string>(true, "Title"), Arguments("""{"title":"  Title  "}""").TextPatch("title"));
        Assert.Throws<DomainException>(() => Arguments("""{"title":null}""").TextPatch("title"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    [InlineData("{}")]
    public void VersionsRequirePositiveJsonIntegers(string value)
    {
        var error = Assert.Throws<DomainException>(() => Arguments($"{{\"expectedVersion\":{value}}}").Version());
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public void MissingVersionIsAPreconditionError()
    {
        var error = Assert.Throws<DomainException>(() => Arguments("{}").Version());
        Assert.Equal(428, error.Status);
        Assert.Equal("missing_precondition", error.Code);
        Assert.Equal(1, Arguments("""{"expectedVersion":1}""").Version());
    }

    [Theory]
    [InlineData("\"not-base64\"")]
    [InlineData("\"LTE=\"")]
    [InlineData("\"MjE0NzQ4MzY0Nw==\"")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void InvalidCursorsAreValidationErrors(string cursor)
    {
        var error = Assert.Throws<DomainException>(() => Arguments($"{{\"cursor\":{cursor}}}").Page());
        Assert.Equal("validation_error", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(4294967297)]
    [InlineData(long.MaxValue)]
    public void PageLimitIsCheckedBeforeNarrowing(long limit) =>
        Assert.Throws<DomainException>(() => new PageRequest(limit));

    [Fact]
    public void PageDefaultsAndCursorRemainCompatible()
    {
        Assert.Equal(50, Arguments("{}").Page().Limit);
        Assert.Equal(2, new PageRequest(cursor: "Mg==").Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("W/\"1\"")]
    [InlineData("\"1\",\"2\"")]
    [InlineData("*")]
    public void InvalidHeadersArePreconditionErrors(string value)
    {
        var error = Assert.Throws<DomainException>(() => VersionHeaders.Parse(value, "X-Step-List-Version"));
        Assert.Equal(428, error.Status);
        Assert.Contains("X-Step-List-Version", error.Message);
    }

    [Fact]
    public void VersionHeadersFollowEachOperationsPreconditions()
    {
        foreach (var definition in OperationCatalog.Definitions)
        {
            var values = new JsonObject();
            var headers = new HeaderDictionary { ["If-Match"] = "\"3\"", ["X-Step-List-Version"] = "\"7\"" };
            VersionHeaders.Apply(headers, definition, values);
            Assert.Equal(definition.Required.Contains("expectedVersion"), values.ContainsKey("expectedVersion"));
            Assert.Equal(definition.Required.Contains("expectedListVersion"), values.ContainsKey("expectedListVersion"));
            if (values.ContainsKey("expectedVersion"))
                Assert.Equal(3, values["expectedVersion"]!.GetValue<long>());
            if (values.ContainsKey("expectedListVersion"))
                Assert.Equal(definition.Required.Contains("expectedVersion") ? 7 : 3, values["expectedListVersion"]!.GetValue<long>());
        }
    }

    [Fact]
    public void DependencyTraversalHandlesTransitiveAndExistingCycles()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        (Guid, Guid)[] edges = [(a, b), (b, c), (c, a)];
        Assert.True(DependencyGraph.Reaches(a, c, edges));
        Assert.True(DependencyGraph.Reaches(a, a, []));
        Assert.False(DependencyGraph.Reaches(a, Guid.NewGuid(), edges));
    }

    [Fact]
    public void CatalogNamesAreUniqueAndCoverEveryOperation()
    {
        Assert.Equal(Enum.GetValues<Operation>().Length, OperationCatalog.Definitions.Count);
        Assert.Equal(30, ToolCatalog.Tools.Select(x => x.Name).Distinct().Count());
        foreach (var operation in Enum.GetValues<Operation>())
        {
            var definition = OperationCatalog.Get(operation);
            Assert.True(OperationCatalog.TryGet(definition.Name, out var byName));
            Assert.Same(definition, byName);
        }
    }

    [Fact]
    public void EveryOutputSchemaMatchesItsSerializedResourceAndEnvelope()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        foreach (var definition in OperationCatalog.Definitions)
        {
            IVersionedResource resource = definition.Resource switch
            {
                ResourceKind.Group => new GroupResource(id, "Group", 1, now, now),
                ResourceKind.Member => new MemberResource(id, id, "Member", 1, now, now, null),
                ResourceKind.Location => new LocationResource(id, "Location", null, 1, now, now, null),
                ResourceKind.Project => new ProjectResource(id, "Project", null, null, null, "planned", [], null, 1, 1, now, now, null, null),
                ResourceKind.Step => new StepResource(id, id, "Step", null, "todo", 1, [], 1, now, now, null),
                _ => throw new InvalidOperationException()
            };
            OperationResponse response = definition.IsList
                ? new PagedOperationResponse(definition.Name, new object[] { resource }, null)
                : new OperationResponse(definition.Name, resource);
            var json = JsonSerializer.SerializeToElement(response, response.GetType(), JsonSerializerOptions.Web);
            var tool = Assert.Single(ToolCatalog.Tools, x => x.Name == definition.Name);
            var schema = tool.OutputSchema!.Value;
            Assert.Equal(schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order(),
                json.EnumerateObject().Select(x => x.Name).Order());
            var resourceSchema = schema.GetProperty("properties").GetProperty("data");
            var data = json.GetProperty("data");
            if (definition.IsList)
            {
                resourceSchema = resourceSchema.GetProperty("items");
                data = data[0];
                Assert.Equal(JsonValueKind.Null, json.GetProperty("nextCursor").ValueKind);
            }
            Assert.Equal(resourceSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order(),
                data.EnumerateObject().Select(x => x.Name).Order());
            Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(definition.Name, json.GetProperty("kind").GetString());
        }
    }

    private static OperationArguments Arguments(string json) => new(JsonNode.Parse(json)!.AsObject());
}
