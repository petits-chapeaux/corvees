using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class TransportContractTests(IntegrationHost host) : IClassFixture<IntegrationHost>
{
    [PostgresFact]
    public async Task GroupAndMemberControllersPreserveVersionsAndHideCredentials()
    {
        var group = await RestAsync(HttpMethod.Get, "/group");
        Assert.Equal("get_group", group.Json["kind"]!.GetValue<string>());
        var renamed = await RestAsync(HttpMethod.Patch, "/group", new { name = "Renamed" }, Version(group));
        Assert.Equal("Renamed", renamed.Json["data"]!["name"]!.GetValue<string>());
        Assert.Equal((Version(group) + 1).ToString(), renamed.ETag?.Trim('"'));

        var me = await RestAsync(HttpMethod.Get, "/me");
        var updated = await RestAsync(HttpMethod.Patch, "/me", new { displayName = "Member name" }, Version(me));
        Assert.Equal("Member name", updated.Json["data"]!["displayName"]!.GetValue<string>());
        var members = await RestAsync(HttpMethod.Get, "/members");
        Assert.Contains(members.Json["data"]!.AsArray(), member => member!["id"]!.GetValue<string>() == Id(me));
        Assert.DoesNotContain("token", members.Json.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", me.Json.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task RestAndMcpReadTheSameResourcesAndListEnvelopes()
    {
        var created = await McpAsync("create_project", new { title = "MCP contract", description = "Keep me" });
        var content = created["structuredContent"]!;
        Assert.True(JsonNode.DeepEquals(content, JsonNode.Parse(created["content"]![0]!["text"]!.GetValue<string>())));
        Assert.False(content.AsObject().ContainsKey("nextCursor"));
        var id = content["data"]!["id"]!.GetValue<string>();
        var rest = await RestAsync(HttpMethod.Get, $"/projects/{id}");
        var mcp = await McpAsync("get_project", new { projectId = id });
        Assert.True(JsonNode.DeepEquals(rest.Json, mcp["structuredContent"]));
        Assert.Equal("\"1\"", rest.ETag);

        var restList = await RestAsync(HttpMethod.Get, "/projects?limit=1");
        var mcpList = await McpAsync("list_projects", new { limit = 1 });
        Assert.True(JsonNode.DeepEquals(restList.Json, mcpList["structuredContent"]));
        Assert.True(mcpList["structuredContent"]!.AsObject().ContainsKey("nextCursor"));
        Assert.Null(restList.ETag);
    }

    [PostgresFact]
    public async Task PatchOmissionPreservesFieldsAndExplicitNullClearsThemOnBothTransports()
    {
        var location = await RestAsync(HttpMethod.Post, "/locations", new { name = "Home", address = "Address" });
        var project = await RestAsync(HttpMethod.Post, "/projects", new { title = "Original", description = "Description", locationId = Id(location) });
        var id = Id(project);
        var updated = await RestAsync(HttpMethod.Patch, $"/projects/{id}", new { title = "Changed" }, 1);
        Assert.Equal("Description", updated.Json["data"]!["description"]!.GetValue<string>());
        Assert.Equal(Id(location), updated.Json["data"]!["locationId"]!.GetValue<string>());
        var cleared = await McpAsync("update_project", new { projectId = id, description = (string?)null, locationId = (string?)null, expectedVersion = 2 });
        var data = cleared["structuredContent"]!["data"]!;
        Assert.Null(data["description"]);
        Assert.Null(data["locationId"]);
        Assert.Equal("Changed", data["title"]!.GetValue<string>());
        var invalid = await RestAsync(HttpMethod.Patch, $"/projects/{id}", new { title = (string?)null }, 3);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);

        var renamed = await RestAsync(HttpMethod.Patch, $"/locations/{Id(location)}", new { name = "Renamed" }, 1);
        Assert.Equal("Address", renamed.Json["data"]!["address"]!.GetValue<string>());
        var noAddress = await RestAsync(HttpMethod.Patch, $"/locations/{Id(location)}", new { address = (string?)null }, 2);
        Assert.Null(noAddress.Json["data"]!["address"]);
    }

    [PostgresFact]
    public async Task RoutesAndHeadersCannotBeOverriddenByBodyOrQuery()
    {
        var first = await RestAsync(HttpMethod.Post, "/locations", new { name = "First" });
        var second = await RestAsync(HttpMethod.Post, "/locations", new { name = "Second" });
        var read = await RestAsync(HttpMethod.Get, $"/locations/{Id(first)}?locationId={Id(second)}");
        Assert.Equal(Id(first), Id(read));
        var changed = await RestAsync(HttpMethod.Patch, $"/locations/{Id(first)}",
            new { locationId = Id(second), name = "Changed", expectedVersion = 999 }, 1);
        Assert.Equal(Id(first), Id(changed));
        Assert.Equal(HttpStatusCode.OK, changed.Status);
        var unchanged = await RestAsync(HttpMethod.Get, $"/locations/{Id(second)}");
        Assert.Equal("Second", unchanged.Json["data"]!["name"]!.GetValue<string>());
        var missingHeader = await RestAsync(HttpMethod.Patch, $"/locations/{Id(first)}", new { name = "Ignored", expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.PreconditionRequired, missingHeader.Status);
    }

    [PostgresFact]
    public async Task InvalidInputHasStableRestAndMcpErrors()
    {
        foreach (var query in new[] { "limit=oops", "limit=4294967297", "deletedOnly=oops", "cursor=not-base64", "limit=1&limit=2", "status=" })
        {
            var result = await RestAsync(HttpMethod.Get, "/projects?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, result.Status);
            Assert.Equal("validation_error", result.Json["code"]!.GetValue<string>());
        }
        var malformed = await RestAsync(HttpMethod.Post, "/projects", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.Status);
        var array = await RestAsync(HttpMethod.Post, "/projects", new StringContent("[]", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, array.Status);
        var unsupported = await RestAsync(HttpMethod.Post, "/projects", new StringContent("title", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.Status);
        var noBody = await RestAsync(HttpMethod.Post, "/projects");
        Assert.Equal(HttpStatusCode.BadRequest, noBody.Status);
        var invalidRoute = await RestAsync(HttpMethod.Get, "/projects/not-a-guid");
        Assert.Equal(HttpStatusCode.NotFound, invalidRoute.Status);

        var mcp = await McpAsync("list_projects", new { cursor = 42 });
        Assert.True(mcp["isError"]!.GetValue<bool>());
        Assert.Equal("validation_error", Error(mcp)["code"]!.GetValue<string>());
        var missing = await McpAsync("update_group", new { name = "No version" });
        Assert.Equal("missing_precondition", Error(missing)["code"]!.GetValue<string>());
        var unknown = await McpAsync("unknown_tool", new { });
        Assert.Equal("not_found", Error(unknown)["code"]!.GetValue<string>());
    }

    [PostgresFact]
    public async Task ArchivedProjectsRejectEditsAndDependencyRemovalKeepsVersioning()
    {
        var first = await RestAsync(HttpMethod.Post, "/projects", new { title = "Dependent" });
        var second = await RestAsync(HttpMethod.Post, "/projects", new { title = "Prerequisite" });
        var path = $"/projects/{Id(first)}";
        var linked = await RestAsync(HttpMethod.Post, path + "/dependencies", new { prerequisiteId = Id(second) }, 1);
        Assert.Single(linked.Json["data"]!["dependencies"]!.AsArray());
        var duplicate = await McpAsync("add_project_dependency", new { projectId = Id(first), prerequisiteId = Id(second), expectedVersion = 2 });
        Assert.Equal("duplicate_dependency", Error(duplicate)["code"]!.GetValue<string>());
        var removed = await RestAsync(HttpMethod.Delete, path + $"/dependencies/{Id(second)}", version: 2);
        Assert.Empty(removed.Json["data"]!["dependencies"]!.AsArray());
        var archived = await RestAsync(HttpMethod.Post, path + "/archive", version: 3);
        Assert.Equal("archived", archived.Json["data"]!["status"]!.GetValue<string>());
        var blocked = await RestAsync(HttpMethod.Patch, path, new { title = "Blocked" }, 4);
        Assert.Equal(HttpStatusCode.Conflict, blocked.Status);
        Assert.Equal("invalid_state", blocked.Json["code"]!.GetValue<string>());
        var unarchived = await RestAsync(HttpMethod.Post, path + "/unarchive", version: 4);
        Assert.Equal("planned", unarchived.Json["data"]!["status"]!.GetValue<string>());
    }

    [PostgresFact]
    public async Task StepDependencyRemovalAndPatchAreSharedWithMcp()
    {
        var project = await RestAsync(HttpMethod.Post, "/projects", new { title = "Steps" });
        var path = $"/projects/{Id(project)}/steps";
        var first = await RestAsync(HttpMethod.Post, path, new { title = "First", description = "Details" }, 1);
        var second = await RestAsync(HttpMethod.Post, path, new { title = "Second" }, 2);
        await RestAsync(HttpMethod.Post, path + $"/{Id(first)}/dependencies", new { prerequisiteId = Id(second) }, 1);
        var removed = await RestAsync(HttpMethod.Delete, path + $"/{Id(first)}/dependencies/{Id(second)}", version: 2);
        Assert.Empty(removed.Json["data"]!["dependencies"]!.AsArray());
        var updated = await McpAsync("update_step", new { projectId = Id(project), stepId = Id(first), title = "Renamed", expectedVersion = 3 });
        Assert.Equal("Details", updated["structuredContent"]!["data"]!["description"]!.GetValue<string>());
        var cleared = await McpAsync("update_step", new { projectId = Id(project), stepId = Id(first), description = (string?)null, expectedVersion = 4 });
        Assert.Null(cleared["structuredContent"]!["data"]!["description"]);
        var rest = await RestAsync(HttpMethod.Get, path + $"/{Id(first)}");
        Assert.Equal("\"5\"", rest.ETag);
        var stale = await McpAsync("update_step", new { projectId = Id(project), stepId = Id(first), title = "Stale", expectedVersion = 4 });
        Assert.Equal("version_conflict", Error(stale)["code"]!.GetValue<string>());
        var missingListVersion = await RestAsync(HttpMethod.Delete, path + $"/{Id(first)}", version: 5);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missingListVersion.Status);
        Assert.Contains("X-Step-List-Version", missingListVersion.Json["message"]!.GetValue<string>());
    }

    [PostgresFact]
    public async Task PaginationAndStatusFilteringRunInBoundedDatabaseQueries()
    {
        var location = await RestAsync(HttpMethod.Post, "/locations", new { name = "Pagination" });
        for (var i = 0; i < 4; i++)
        {
            var project = await RestAsync(HttpMethod.Post, "/projects", new { title = $"Page {i}", locationId = Id(location) });
            var path = $"/projects/{Id(project)}";
            var step = await RestAsync(HttpMethod.Post, path + "/steps", new { title = "Task" }, 1);
            if (i < 3)
                await RestAsync(HttpMethod.Patch, path + $"/steps/{Id(step)}", new { status = "done" }, 1);
        }
        var url = $"/projects?locationId={Id(location)}&status=complete&limit=2";
        host.Queries.Start();
        var first = await RestAsync(HttpMethod.Get, url);
        var commands = host.Queries.Stop();
        Assert.Equal(2, first.Json["data"]!.AsArray().Count);
        Assert.InRange(commands.Length, 2, 3);
        Assert.Contains(commands, command => command.Contains("LIMIT", StringComparison.Ordinal));
        var cursor = first.Json["nextCursor"]!.GetValue<string>();
        var second = await RestAsync(HttpMethod.Get, url + "&cursor=" + Uri.EscapeDataString(cursor));
        Assert.Single(second.Json["data"]!.AsArray());
        Assert.Null(second.Json["nextCursor"]);
        var ids = first.Json["data"]!.AsArray().Concat(second.Json["data"]!.AsArray()).Select(x => x!["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(3, ids.Distinct().Count());
        Assert.All(first.Json["data"]!.AsArray(), item => Assert.Equal("complete", item!["status"]!.GetValue<string>()));
    }

    [PostgresFact]
    public async Task StepAndLocationPaginationKeepOrderAndDeletionFilters()
    {
        var project = await RestAsync(HttpMethod.Post, "/projects", new { title = "Ordered pages" });
        var path = $"/projects/{Id(project)}/steps";
        for (var i = 0; i < 3; i++)
            await RestAsync(HttpMethod.Post, path, new { title = $"Step {i}" }, i + 1);
        host.Queries.Start();
        var first = await RestAsync(HttpMethod.Get, path + "?limit=2");
        var commands = host.Queries.Stop();
        Assert.InRange(commands.Length, 3, 4);
        Assert.Contains(commands, command => command.Contains("LIMIT", StringComparison.Ordinal));
        Assert.Equal(new long[] { 1, 2 }, first.Json["data"]!.AsArray().Select(x => x!["position"]!.GetValue<long>()));
        var next = await RestAsync(HttpMethod.Get, path + "?limit=2&cursor=" + Uri.EscapeDataString(first.Json["nextCursor"]!.GetValue<string>()));
        Assert.Equal(3, next.Json["data"]![0]!["position"]!.GetValue<long>());
        Assert.Null(next.Json["nextCursor"]);

        var location = await RestAsync(HttpMethod.Post, "/locations", new { name = "Deleted page" });
        await RestAsync(HttpMethod.Delete, $"/locations/{Id(location)}", version: 1);
        var deleted = await RestAsync(HttpMethod.Get, "/locations?deletedOnly=true");
        Assert.Contains(deleted.Json["data"]!.AsArray(), item => item!["id"]!.GetValue<string>() == Id(location));
        var included = await RestAsync(HttpMethod.Get, $"/locations/{Id(location)}?includeDeleted=true");
        Assert.Equal(Id(location), Id(included));
        Assert.Equal(HttpStatusCode.OK, (await RestAsync(HttpMethod.Post, $"/locations/{Id(location)}/restore", version: 2)).Status);
    }

    [PostgresFact]
    public async Task AuthenticationIsRequestScopedAndDeletedMembersAreRejected()
    {
        var project = await RestAsync(HttpMethod.Post, "/projects", new { title = "Private project" });
        var other = await McpAsync("get_project", new { projectId = Id(project) }, host.OtherToken);
        Assert.Equal("not_found", Error(other)["code"]!.GetValue<string>());
        var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            RestAsync(HttpMethod.Get, $"/projects/{Id(project)}", token: index % 2 == 0 ? host.Token : host.OtherToken)));
        Assert.Equal(4, parallel.Count(x => x.Status == HttpStatusCode.OK));
        Assert.Equal(4, parallel.Count(x => x.Status == HttpStatusCode.NotFound));

        var revoked = IntegrationHost.NewToken();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CorveesDbContext>();
            var hash = IntegrationHost.Hash(host.Token);
            var member = await db.Members.IgnoreQueryFilters().SingleAsync(x => x.TokenHash == hash);
            db.Members.Add(new Member { GroupId = member.GroupId, DisplayName = "Revoked", TokenHash = IntegrationHost.Hash(revoked), DeletedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await RestAsync(HttpMethod.Get, "/me", token: revoked)).Status);
        using var rejected = await host.Client.PostAsJsonAsync($"/m/{revoked}/mcp", new { jsonrpc = "2.0", method = "tools/list", id = 1 });
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentDependenciesCannotCreateACycle()
    {
        var first = await RestAsync(HttpMethod.Post, "/projects", new { title = "First concurrent dependency" });
        var second = await RestAsync(HttpMethod.Post, "/projects", new { title = "Second concurrent dependency" });
        var results = await Task.WhenAll(
            RestAsync(HttpMethod.Post, $"/projects/{Id(first)}/dependencies", new { prerequisiteId = Id(second) }, 1),
            RestAsync(HttpMethod.Post, $"/projects/{Id(second)}/dependencies", new { prerequisiteId = Id(first) }, 1));
        Assert.Single(results, result => result.Status == HttpStatusCode.OK);
        var rejected = Assert.Single(results, result => result.Status == HttpStatusCode.Conflict);
        Assert.Equal("dependency_cycle", rejected.Json["code"]!.GetValue<string>());
    }

    [PostgresFact]
    public async Task StatusFiltersMatchProjectedStatusIncludingArchivedAndEmptyProjects()
    {
        var location = await RestAsync(HttpMethod.Post, "/locations", new { name = "Statuses" });
        foreach (var status in new[] { "planned", "active", "complete", "archived" })
        {
            var project = await RestAsync(HttpMethod.Post, "/projects", new { title = status, locationId = Id(location) });
            var path = $"/projects/{Id(project)}";
            if (status is "active" or "complete")
            {
                var step = await RestAsync(HttpMethod.Post, path + "/steps", new { title = "Task" }, 1);
                await RestAsync(HttpMethod.Patch, path + $"/steps/{Id(step)}", new { status = status == "active" ? "in_progress" : "done" }, 1);
            }
            if (status == "archived")
                await RestAsync(HttpMethod.Post, path + "/archive", version: 1);
            var filtered = await RestAsync(HttpMethod.Get, $"/projects?locationId={Id(location)}&status={status}");
            var item = Assert.Single(filtered.Json["data"]!.AsArray())!;
            Assert.Equal(Id(project), item["id"]!.GetValue<string>());
            Assert.Equal(status, item["status"]!.GetValue<string>());
        }
    }

    [PostgresFact]
    public async Task ProjectBoardViewIsServedAsAnMcpAppResource()
    {
        var listed = (await McpRequestAsync("resources/list", new { }))["result"]!["resources"]!.AsArray();
        var resource = Assert.Single(listed)!;
        Assert.Equal("ui://corvees/project-board", resource["uri"]!.GetValue<string>());
        Assert.Equal("text/html;profile=mcp-app", resource["mimeType"]!.GetValue<string>());

        var read = await McpRequestAsync("resources/read", new { uri = "ui://corvees/project-board" });
        var contents = Assert.Single(read["result"]!["contents"]!.AsArray())!;
        Assert.Equal("text/html;profile=mcp-app", contents["mimeType"]!.GetValue<string>());
        Assert.Contains("ui/initialize", contents["text"]!.GetValue<string>());

        var missing = await McpRequestAsync("resources/read", new { uri = "ui://corvees/missing" });
        Assert.Equal(-32002, missing["error"]!["code"]!.GetValue<int>());
    }

    [PostgresFact]
    public async Task ProjectReadsAddAReadableSummaryAfterTheJsonCopy()
    {
        var created = await McpAsync("create_project", new { title = "Summary contract" });
        Assert.Single(created["content"]!.AsArray());
        var id = created["structuredContent"]!["data"]!["id"]!.GetValue<string>();
        await McpAsync("create_step", new { projectId = id, title = "First step", expectedListVersion = 1 });

        var read = await McpAsync("get_project", new { projectId = id });
        var blocks = read["content"]!.AsArray();
        Assert.Equal(2, blocks.Count);
        Assert.True(JsonNode.DeepEquals(read["structuredContent"], JsonNode.Parse(blocks[0]!["text"]!.GetValue<string>())));
        Assert.Contains("Prochaine étape : First step", blocks[1]!["text"]!.GetValue<string>());

        var list = await McpAsync("list_projects", new { limit = 100 });
        Assert.Contains("- Summary contract (planifié) — prochaine étape : First step", list["content"]![1]!["text"]!.GetValue<string>());
    }

    private async Task<RestResponse> RestAsync(HttpMethod method, string path, object? body = null, long? version = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, "/api/v1" + path);
        request.Headers.Authorization = new("Bearer", token ?? host.Token);
        if (version != null)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        if (body != null)
            request.Content = body as HttpContent ?? JsonContent.Create(body);
        using var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new RestResponse(response.StatusCode, string.IsNullOrEmpty(text) ? new JsonObject() : JsonNode.Parse(text)!, response.Headers.ETag?.ToString());
    }

    private async Task<JsonNode> McpAsync(string name, object arguments, string? token = null) =>
        (await McpRequestAsync("tools/call", new { name, arguments }, token))["result"]!;

    private async Task<JsonNode> McpRequestAsync(string method, object parameters, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/m/{token ?? host.Token}/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters })
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').First(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..] : text;
        return JsonNode.Parse(json)!;
    }

    private static JsonNode Error(JsonNode result)
    {
        Assert.True(result["isError"]!.GetValue<bool>());
        return JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
    }

    private static string Id(RestResponse response) => response.Json["data"]!["id"]!.GetValue<string>();
    private static long Version(RestResponse response) => response.Json["data"]!["version"]!.GetValue<long>();
    private sealed record RestResponse(HttpStatusCode Status, JsonNode Json, string? ETag);
}
