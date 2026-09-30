using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class ProjectApiTests(IntegrationHost host) : IClassFixture<IntegrationHost>
{
    private async Task<(HttpStatusCode status, JsonElement json)> Call(HttpMethod method, string url, object? body = null,
        long? version = null, long? listVersion = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, "/api/v1" + url);
        request.Headers.Authorization = new("Bearer", token ?? host.Token);
        if (version != null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        if (listVersion != null) request.Headers.TryAddWithoutValidation("X-Step-List-Version", $"\"{listVersion}\"");
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await host.Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    [PostgresFact]
    public async Task CompletionAndSoftDeletionFollowLiveSteps()
    {
        var (_, created) = await Call(HttpMethod.Post, "/projects", new { title = "Deck" });
        var id = created.GetProperty("data").GetProperty("id").GetString();
        var path = $"/projects/{id}";
        Assert.Equal("planned", created.GetProperty("data").GetProperty("status").GetString());

        var (_, first) = await Call(HttpMethod.Post, path + "/steps", new { title = "Measure" }, 1);
        var firstId = first.GetProperty("data").GetProperty("id").GetString();
        var (_, done) = await Call(HttpMethod.Patch, path + $"/steps/{firstId}", new { status = "done" }, 1);
        Assert.Equal("done", done.GetProperty("data").GetProperty("status").GetString());
        var (_, project) = await Call(HttpMethod.Get, path);
        Assert.Equal("complete", project.GetProperty("data").GetProperty("status").GetString());

        var (_, second) = await Call(HttpMethod.Post, path + "/steps", new { title = "Replace" }, 2);
        Assert.Equal(2, second.GetProperty("data").GetProperty("position").GetInt32());
        var (_, active) = await Call(HttpMethod.Get, path);
        Assert.Equal("active", active.GetProperty("data").GetProperty("status").GetString());
        var (stale, _) = await Call(HttpMethod.Patch, path, new { title = "Old version" }, 0);
        Assert.Equal(HttpStatusCode.PreconditionRequired, stale);

        var (_, deleted) = await Call(HttpMethod.Delete, path, version: 1);
        Assert.NotEqual(JsonValueKind.Null, deleted.GetProperty("data").GetProperty("deletedAt").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await Call(HttpMethod.Get, path)).status);
        var (_, restored) = await Call(HttpMethod.Post, path + "/restore", version: 2);
        Assert.Equal("active", restored.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Call(HttpMethod.Get, path + $"/steps/{firstId}")).status);
    }

    [PostgresFact]
    public async Task DependenciesAreInformationalAndCannotCycle()
    {
        var (_, p) = await Call(HttpMethod.Post, "/projects", new { title = "Dependency test" });
        var path = "/projects/" + p.GetProperty("data").GetProperty("id").GetString();
        var (_, a) = await Call(HttpMethod.Post, path + "/steps", new { title = "A" }, 1);
        var (_, b) = await Call(HttpMethod.Post, path + "/steps", new { title = "B" }, 2);
        var aId = a.GetProperty("data").GetProperty("id").GetString();
        var bId = b.GetProperty("data").GetProperty("id").GetString();
        var (_, linked) = await Call(HttpMethod.Post, path + $"/steps/{bId}/dependencies", new { prerequisiteId = aId }, 1);
        Assert.Single(linked.GetProperty("data").GetProperty("dependencies").EnumerateArray());
        var (cycle, error) = await Call(HttpMethod.Post, path + $"/steps/{aId}/dependencies", new { prerequisiteId = bId }, 1);
        Assert.Equal(HttpStatusCode.Conflict, cycle);
        Assert.Equal("dependency_cycle", error.GetProperty("code").GetString());
        var (_, done) = await Call(HttpMethod.Patch, path + $"/steps/{bId}", new { status = "done" }, 2);
        Assert.Equal("done", done.GetProperty("data").GetProperty("status").GetString());
    }

    [PostgresFact]
    public async Task NextStepPrefersWorkInProgressAndSkipsStepsWaitingOnPrerequisites()
    {
        var (_, p) = await Call(HttpMethod.Post, "/projects", new { title = "Next step test" });
        Assert.Equal(JsonValueKind.Null, p.GetProperty("data").GetProperty("nextStep").ValueKind);
        var path = "/projects/" + p.GetProperty("data").GetProperty("id").GetString();
        var (_, a) = await Call(HttpMethod.Post, path + "/steps", new { title = "A" }, 1);
        var (_, b) = await Call(HttpMethod.Post, path + "/steps", new { title = "B" }, 2);
        var aId = a.GetProperty("data").GetProperty("id").GetString();
        var bId = b.GetProperty("data").GetProperty("id").GetString();
        async Task<JsonElement> NextStep() => (await Call(HttpMethod.Get, path)).json.GetProperty("data").GetProperty("nextStep");

        Assert.Equal(aId, (await NextStep()).GetProperty("id").GetString());
        await Call(HttpMethod.Post, path + $"/steps/{aId}/dependencies", new { prerequisiteId = bId }, 1);
        Assert.Equal(bId, (await NextStep()).GetProperty("id").GetString());
        await Call(HttpMethod.Patch, path + $"/steps/{bId}", new { status = "in_progress" }, 1);
        Assert.Equal("in_progress", (await NextStep()).GetProperty("status").GetString());
        await Call(HttpMethod.Patch, path + $"/steps/{bId}", new { status = "done" }, 2);
        Assert.Equal(aId, (await NextStep()).GetProperty("id").GetString());
        await Call(HttpMethod.Patch, path + $"/steps/{aId}", new { status = "done" }, 2);
        Assert.Equal(JsonValueKind.Null, (await NextStep()).ValueKind);
    }

    [PostgresFact]
    public async Task DeletedPositionsAndLocationLinksSurviveRestoration()
    {
        var (_, location) = await Call(HttpMethod.Post, "/locations", new { name = "Maison" });
        var locationId = location.GetProperty("data").GetProperty("id").GetString();
        var (_, project) = await Call(HttpMethod.Post, "/projects", new { title = "Garden", locationId });
        var path = "/projects/" + project.GetProperty("data").GetProperty("id").GetString();
        var (_, a) = await Call(HttpMethod.Post, path + "/steps", new { title = "A" }, 1);
        var (_, b) = await Call(HttpMethod.Post, path + "/steps", new { title = "B" }, 2);
        var (_, c) = await Call(HttpMethod.Post, path + "/steps", new { title = "C" }, 3);
        var bId = b.GetProperty("data").GetProperty("id").GetString();
        var cId = c.GetProperty("data").GetProperty("id").GetString();
        var aId = a.GetProperty("data").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await Call(HttpMethod.Delete, path + $"/steps/{bId}", version: 1, listVersion: 4)).status);
        var (_, moved) = await Call(HttpMethod.Post, path + $"/steps/{cId}/move",
            new { targetStepId = aId, placement = "before" }, 5);
        Assert.Equal(1, moved.GetProperty("data").GetProperty("position").GetInt32());
        var (_, restored) = await Call(HttpMethod.Post, path + $"/steps/{bId}/restore", version: 2, listVersion: 6);
        Assert.Equal(3, restored.GetProperty("data").GetProperty("position").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await Call(HttpMethod.Delete, "/locations/" + locationId, version: 1)).status);
        var (_, missingLocation) = await Call(HttpMethod.Get, path);
        Assert.False(missingLocation.GetProperty("data").GetProperty("location").GetProperty("available").GetBoolean());
        Assert.Equal(locationId, missingLocation.GetProperty("data").GetProperty("locationId").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Call(HttpMethod.Post, "/locations/" + locationId + "/restore", version: 2)).status);
    }

    [PostgresFact]
    public async Task ProjectDependencyCyclesAreRejected()
    {
        var (_, a) = await Call(HttpMethod.Post, "/projects", new { title = "A" });
        var (_, b) = await Call(HttpMethod.Post, "/projects", new { title = "B" });
        var aId = a.GetProperty("data").GetProperty("id").GetString();
        var bId = b.GetProperty("data").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await Call(HttpMethod.Post, $"/projects/{bId}/dependencies",
            new { prerequisiteId = aId }, 1)).status);
        var (status, error) = await Call(HttpMethod.Post, $"/projects/{aId}/dependencies", new { prerequisiteId = bId }, 1);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("dependency_cycle", error.GetProperty("code").GetString());
    }

    [PostgresFact]
    public async Task McpAdvertisesAndExecutesTheSameProjectOperation()
    {
        async Task<JsonElement> Mcp(string method, object? parameters = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/m/{host.Token}/mcp")
            {
                Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters })
            };
            request.Headers.Accept.ParseAdd("application/json, text/event-stream");
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            var json = text.Split('\n').First(x => x.StartsWith("data: "))[6..];
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        var listed = await Mcp("tools/list");
        Assert.Equal(30, listed.GetProperty("result").GetProperty("tools").GetArrayLength());
        var created = await Mcp("tools/call", new { name = "create_project", arguments = new { title = "From MCP" } });
        var data = created.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("create_project", data.GetProperty("kind").GetString());
        Assert.Equal("From MCP", data.GetProperty("data").GetProperty("title").GetString());
    }

    [PostgresFact]
    public async Task ConcurrentStepCreationWithTheSameListVersionHasOneWinner()
    {
        var (_, project) = await Call(HttpMethod.Post, "/projects", new { title = "Race" });
        var path = "/projects/" + project.GetProperty("data").GetProperty("id").GetString() + "/steps";
        var results = await Task.WhenAll(
            Call(HttpMethod.Post, path, new { title = "A" }, 1),
            Call(HttpMethod.Post, path, new { title = "B" }, 1));
        Assert.Single(results, x => x.status == HttpStatusCode.OK);
        Assert.Single(results, x => x.status == HttpStatusCode.PreconditionFailed);
    }

    [PostgresFact]
    public async Task MemberTokensIsolateGroupsAndRestNeedsVersions()
    {
        var (_, p) = await Call(HttpMethod.Post, "/projects", new { title = "Private" });
        var path = "/projects/" + p.GetProperty("data").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await Call(HttpMethod.Get, path, token: host.OtherToken)).status);
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await Call(HttpMethod.Patch, path, new { title = "Changed" })).status);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Call(HttpMethod.Patch, path, new { title = "Changed" }, 2)).status);
    }
}
