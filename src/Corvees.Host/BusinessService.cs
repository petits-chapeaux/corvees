using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host;

public sealed class DomainException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class BusinessService(CorveesDbContext db, MemberSession session)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Guid GroupId => session.Member.GroupId;

    public async Task<object> ExecuteAsync(string operation, JsonObject args, CancellationToken ct)
    {
        object result = operation switch
        {
            "get_group" => await GetGroup(ct),
            "update_group" => await UpdateGroup(args, ct),
            "list_members" => Page(await db.Members.Where(x => x.DeletedAt == null).OrderBy(x => x.DisplayName).ThenBy(x => x.Id).ToListAsync(ct), args, MemberView),
            "get_me" => MemberView(session.Member),
            "update_me" => await UpdateMe(args, ct),
            "list_locations" => await ListLocations(args, ct),
            "get_location" => LocationView(await Location(Id(args, "locationId"), IncludeDeleted(args), ct)),
            "create_location" => await CreateLocation(args, ct),
            "update_location" => await UpdateLocation(args, ct),
            "delete_location" => await DeleteLocation(args, ct),
            "restore_location" => await RestoreLocation(args, ct),
            "list_projects" => await ListProjects(args, ct),
            "get_project" => await ProjectView(await Project(Id(args, "projectId"), IncludeDeleted(args), ct), ct),
            "create_project" => await CreateProject(args, ct),
            "update_project" => await UpdateProject(args, ct),
            "archive_project" => await ArchiveProject(args, true, ct),
            "unarchive_project" => await ArchiveProject(args, false, ct),
            "delete_project" => await DeleteProject(args, ct),
            "restore_project" => await RestoreProject(args, ct),
            "list_steps" => await ListSteps(args, ct),
            "get_step" => await StepView(await Step(Id(args, "projectId"), Id(args, "stepId"), IncludeDeleted(args), ct), ct),
            "create_step" => await CreateStep(args, ct),
            "update_step" => await UpdateStep(args, ct),
            "move_step" => await MoveStep(args, ct),
            "delete_step" => await DeleteStep(args, ct),
            "restore_step" => await RestoreStep(args, ct),
            "add_project_dependency" => await ProjectDependency(args, true, ct),
            "remove_project_dependency" => await ProjectDependency(args, false, ct),
            "add_step_dependency" => await StepDependency(args, true, ct),
            "remove_step_dependency" => await StepDependency(args, false, ct),
            _ => throw Error("not_found", 404, "Unknown operation")
        };
        return result is PageResult page
            ? new { schemaVersion = 1, kind = operation, data = page.Items, nextCursor = page.NextCursor }
            : (object)new { schemaVersion = 1, kind = operation, data = result };
    }

    private static DomainException Error(string code, int status, string message) => new(code, status, message);
    private static bool Flag(JsonObject a, string key)
    {
        if (a[key] is null) return false;
        if (a[key] is not JsonValue v || !v.TryGetValue<bool>(out var value))
            throw Error("validation_error", 400, $"{key} must be a boolean");
        return value;
    }
    private static bool IncludeDeleted(JsonObject a) => Flag(a, "includeDeleted");
    private static Guid Id(JsonObject a, string key)
    {
        if (a[key] is not JsonValue value || !Guid.TryParse(value.ToString(), out var id) || id == Guid.Empty)
            throw Error("validation_error", 400, $"{key} must be a UUID");
        return id;
    }
    private static Guid? OptionalId(JsonObject a, string key) => a[key] is null ? null : Id(a, key);
    private static long Number(JsonObject a, string key)
    {
        if (a[key] is null)
            throw key.StartsWith("expected", StringComparison.Ordinal)
                ? Error("missing_precondition", 428, $"{key} is required")
                : Error("validation_error", 400, $"{key} is required");
        if (a[key] is not JsonValue v || !long.TryParse(v.ToString(), out var number) || number < 1)
            throw Error("validation_error", 400, $"{key} must be a positive integer");
        return number;
    }
    private static void Expect(long actual, long expected)
    {
        if (actual != expected) throw Error("version_conflict", 412, "The resource changed. Read it again before writing.");
    }
    private static string Text(JsonObject a, string key, int length)
    {
        if (a[key] is not JsonValue raw || !raw.TryGetValue<string>(out var text))
            throw Error("validation_error", 400, $"{key} must be text");
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > length)
            throw Error("validation_error", 400, $"{key} must be 1 to {length} characters");
        return value;
    }
    private static string? OptionalText(JsonObject a, string key, int length)
    {
        if (a[key] is null) return null;
        if (a[key] is not JsonValue raw || !raw.TryGetValue<string>(out var text))
            throw Error("validation_error", 400, $"{key} must be text or null");
        var value = text?.Trim();
        if (value?.Length > length) throw Error("validation_error", 400, $"{key} exceeds {length} characters");
        return string.IsNullOrEmpty(value) ? null : value;
    }
    private static void Editable(Project p)
    {
        if (p.ArchivedAt != null) throw Error("invalid_state", 409, "Unarchive the project before editing it");
    }
    private static object MemberView(Member m) => new { m.Id, m.GroupId, m.DisplayName, m.Version, m.CreatedAt, m.UpdatedAt, m.DeletedAt };
    private static object LocationView(Location l) => new { l.Id, l.Name, l.Address, l.Version, l.CreatedAt, l.UpdatedAt, l.DeletedAt };
    private static object GroupView(Group g) => new { g.Id, g.Name, g.Version, g.CreatedAt, g.UpdatedAt };

    private sealed record PageResult(object[] Items, string? NextCursor);

    private static PageResult Page<T>(IReadOnlyList<T> source, JsonObject a, Func<T, object> view)
    {
        var limit = a["limit"] is null ? 50 : (int)Number(a, "limit");
        if (limit > 100) throw Error("validation_error", 400, "limit must be at most 100");
        var offset = 0;
        if (a["cursor"] is JsonValue c)
        {
            try { offset = int.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(c.GetValue<string>()))); }
            catch (Exception e) when (e is FormatException or OverflowException) { throw Error("validation_error", 400, "Invalid cursor"); }
        }
        if (offset < 0) throw Error("validation_error", 400, "Invalid cursor");
        var items = source.Skip(offset).Take(limit).Select(view).ToArray();
        return new PageResult(items, offset + items.Length < source.Count
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes((offset + items.Length).ToString())) : null);
    }

    private async Task<Group> Group(CancellationToken ct) => await db.Groups.SingleAsync(x => x.Id == GroupId, ct);
    private async Task<object> GetGroup(CancellationToken ct) => GroupView(await Group(ct));
    private async Task<object> UpdateGroup(JsonObject a, CancellationToken ct)
    {
        var group = await Group(ct);
        Expect(group.Version, Number(a, "expectedVersion"));
        group.Name = Text(a, "name", 200);
        Touch(group);
        await db.SaveChangesAsync(ct);
        return GroupView(group);
    }
    private async Task<object> UpdateMe(JsonObject a, CancellationToken ct)
    {
        var member = await db.Members.SingleAsync(x => x.Id == session.Member.Id, ct);
        Expect(member.Version, Number(a, "expectedVersion"));
        member.DisplayName = Text(a, "displayName", 200);
        member.Version++;
        member.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return MemberView(member);
    }
    private static void Touch(Group g) { g.Version++; g.UpdatedAt = DateTimeOffset.UtcNow; }
    private static void Touch(Location l) { l.Version++; l.UpdatedAt = DateTimeOffset.UtcNow; }
    private static void Touch(Project p) { p.Version++; p.UpdatedAt = DateTimeOffset.UtcNow; }
    private static void Touch(Step s) { s.Version++; s.UpdatedAt = DateTimeOffset.UtcNow; }
    private static void TouchList(Project p) { p.StepListVersion++; p.UpdatedAt = DateTimeOffset.UtcNow; }

    private async Task<object> ListLocations(JsonObject a, CancellationToken ct)
    {
        var deletedOnly = Flag(a, "deletedOnly");
        return Page(await db.Locations.Where(x => (x.DeletedAt != null) == deletedOnly)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct), a, LocationView);
    }

    private async Task<Location> Location(Guid id, bool includeDeleted, CancellationToken ct) =>
        await db.Locations.SingleOrDefaultAsync(x => x.Id == id && (includeDeleted || x.DeletedAt == null), ct)
        ?? throw Error("not_found", 404, "Location not found");
    private async Task<Project> Project(Guid id, bool includeDeleted, CancellationToken ct) =>
        await db.Projects.SingleOrDefaultAsync(x => x.Id == id && (includeDeleted || x.DeletedAt == null), ct)
        ?? throw Error("not_found", 404, "Project not found");
    private async Task<Step> Step(Guid projectId, Guid id, bool includeDeleted, CancellationToken ct)
    {
        await Project(projectId, false, ct);
        return await db.Steps.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == id && (includeDeleted || x.DeletedAt == null), ct)
            ?? throw Error("not_found", 404, "Step not found");
    }
    private async Task<object> CreateLocation(JsonObject a, CancellationToken ct)
    {
        var l = new Location { GroupId = GroupId, Name = Text(a, "name", 200), Address = OptionalText(a, "address", 500) };
        db.Locations.Add(l);
        await db.SaveChangesAsync(ct);
        return LocationView(l);
    }
    private async Task<object> UpdateLocation(JsonObject a, CancellationToken ct)
    {
        var l = await Location(Id(a, "locationId"), false, ct);
        Expect(l.Version, Number(a, "expectedVersion"));
        if (a.ContainsKey("name")) l.Name = Text(a, "name", 200);
        if (a.ContainsKey("address")) l.Address = OptionalText(a, "address", 500);
        Touch(l);
        await db.SaveChangesAsync(ct);
        return LocationView(l);
    }
    private async Task<object> DeleteLocation(JsonObject a, CancellationToken ct)
    {
        var l = await Location(Id(a, "locationId"), false, ct);
        Expect(l.Version, Number(a, "expectedVersion"));
        l.DeletedAt = DateTimeOffset.UtcNow;
        Touch(l);
        await db.SaveChangesAsync(ct);
        return LocationView(l);
    }
    private async Task<object> RestoreLocation(JsonObject a, CancellationToken ct)
    {
        var l = await Location(Id(a, "locationId"), true, ct);
        Expect(l.Version, Number(a, "expectedVersion"));
        if (l.DeletedAt == null) throw Error("invalid_state", 409, "Location is not deleted");
        l.DeletedAt = null;
        Touch(l);
        await db.SaveChangesAsync(ct);
        return LocationView(l);
    }

    private async Task<object> ProjectView(Project p, CancellationToken ct)
    {
        var steps = await db.Steps.Where(x => x.ProjectId == p.Id && x.DeletedAt == null).Select(x => x.Status).ToListAsync(ct);
        var status = p.ArchivedAt != null ? "archived" : steps.Count > 0 && steps.All(x => x == "done")
            ? "complete" : steps.Any(x => x != "todo") ? "active" : "planned";
        var location = p.LocationId == null ? null : await db.Locations.SingleAsync(x => x.Id == p.LocationId, ct);
        var links = await db.ProjectDependencies.Where(x => x.ProjectId == p.Id).Select(x => x.PrerequisiteId).ToListAsync(ct);
        var active = await db.Projects.Where(x => links.Contains(x.Id) && x.DeletedAt == null).Select(x => x.Id).ToListAsync(ct);
        return new { p.Id, p.Title, p.Description, p.LocationId,
            location = location == null ? null : new { location.Id, available = location.DeletedAt == null, name = location.DeletedAt == null ? location.Name : null },
            status, dependencies = active, p.Version, p.StepListVersion, p.CreatedAt, p.UpdatedAt, p.ArchivedAt, p.DeletedAt };
    }
    private async Task<object> ListProjects(JsonObject a, CancellationToken ct)
    {
        var deletedOnly = Flag(a, "deletedOnly");
        var projects = await db.Projects.Where(x => (x.DeletedAt != null) == deletedOnly)
            .OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        var locationId = OptionalId(a, "locationId");
        if (locationId != null) projects = projects.Where(x => x.LocationId == locationId).ToList();
        var status = a["status"] is null ? null : Text(a, "status", 20);
        if (status != null && status is not ("planned" or "active" or "complete" or "archived"))
            throw Error("validation_error", 400, "Unknown project status");
        var views = new List<object>();
        foreach (var p in projects)
        {
            var view = await ProjectView(p, ct);
            if (status == null || JsonSerializer.SerializeToNode(view, JsonOptions)?["status"]?.ToString() == status) views.Add(view);
        }
        return Page(views, a, x => x);
    }
    private async Task<object> CreateProject(JsonObject a, CancellationToken ct)
    {
        var locationId = OptionalId(a, "locationId");
        if (locationId != null) await Location(locationId.Value, false, ct);
        var p = new Project { GroupId = GroupId, Title = Text(a, "title", 200), Description = OptionalText(a, "description", 10000), LocationId = locationId };
        db.Projects.Add(p);
        await db.SaveChangesAsync(ct);
        return await ProjectView(p, ct);
    }
    private async Task<object> UpdateProject(JsonObject a, CancellationToken ct)
    {
        var p = await Project(Id(a, "projectId"), false, ct);
        Editable(p);
        Expect(p.Version, Number(a, "expectedVersion"));
        if (a.ContainsKey("title")) p.Title = Text(a, "title", 200);
        if (a.ContainsKey("description")) p.Description = OptionalText(a, "description", 10000);
        if (a.ContainsKey("locationId"))
        {
            p.LocationId = OptionalId(a, "locationId");
            if (p.LocationId != null) await Location(p.LocationId.Value, false, ct);
        }
        Touch(p);
        await db.SaveChangesAsync(ct);
        return await ProjectView(p, ct);
    }
    private async Task<object> ArchiveProject(JsonObject a, bool archive, CancellationToken ct)
    {
        var p = await Project(Id(a, "projectId"), false, ct);
        Expect(p.Version, Number(a, "expectedVersion"));
        if ((p.ArchivedAt != null) == archive) throw Error("invalid_state", 409, "Project is already in that state");
        p.ArchivedAt = archive ? DateTimeOffset.UtcNow : null;
        Touch(p);
        await db.SaveChangesAsync(ct);
        return await ProjectView(p, ct);
    }
    private async Task<object> DeleteProject(JsonObject a, CancellationToken ct)
    {
        var p = await Project(Id(a, "projectId"), false, ct);
        Expect(p.Version, Number(a, "expectedVersion"));
        p.DeletedAt = DateTimeOffset.UtcNow;
        Touch(p);
        await db.SaveChangesAsync(ct);
        return await ProjectView(p, ct);
    }
    private async Task<object> RestoreProject(JsonObject a, CancellationToken ct)
    {
        var p = await Project(Id(a, "projectId"), true, ct);
        Expect(p.Version, Number(a, "expectedVersion"));
        if (p.DeletedAt == null) throw Error("invalid_state", 409, "Project is not deleted");
        p.DeletedAt = null;
        Touch(p);
        await db.SaveChangesAsync(ct);
        return await ProjectView(p, ct);
    }

    private async Task<object> StepView(Step s, CancellationToken ct)
    {
        var links = await db.StepDependencies.Where(x => x.ProjectId == s.ProjectId && x.StepId == s.Id)
            .Select(x => x.PrerequisiteId).ToListAsync(ct);
        var active = await db.Steps.Where(x => links.Contains(x.Id) && x.DeletedAt == null).Select(x => x.Id).ToListAsync(ct);
        return new { s.Id, s.ProjectId, s.Title, s.Description, s.Status, s.Position,
            dependencies = active, s.Version, s.CreatedAt, s.UpdatedAt, s.DeletedAt };
    }
    private async Task<object> ListSteps(JsonObject a, CancellationToken ct)
    {
        var projectId = Id(a, "projectId");
        await Project(projectId, false, ct);
        var deletedOnly = Flag(a, "deletedOnly");
        var steps = await db.Steps.Where(x => x.ProjectId == projectId && (x.DeletedAt != null) == deletedOnly)
            .OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync(ct);
        var views = new List<object>();
        foreach (var step in steps) views.Add(await StepView(step, ct));
        return Page(views, a, x => x);
    }
    private async Task<Project> ListWriteProject(JsonObject a, CancellationToken ct)
    {
        var p = await Project(Id(a, "projectId"), false, ct);
        Editable(p);
        Expect(p.StepListVersion, Number(a, "expectedListVersion"));
        return p;
    }
    private async Task<object> CreateStep(JsonObject a, CancellationToken ct)
    {
        var p = await ListWriteProject(a, ct);
        var max = await db.Steps.Where(x => x.ProjectId == p.Id).MaxAsync(x => (long?)x.Position, ct) ?? 0;
        var s = new Step { GroupId = GroupId, ProjectId = p.Id, Title = Text(a, "title", 200),
            Description = OptionalText(a, "description", 10000), Position = max + 1 };
        db.Steps.Add(s);
        TouchList(p);
        await db.SaveChangesAsync(ct);
        return await StepView(s, ct);
    }
    private async Task<object> UpdateStep(JsonObject a, CancellationToken ct)
    {
        var p = await Project(Id(a, "projectId"), false, ct);
        Editable(p);
        var s = await Step(p.Id, Id(a, "stepId"), false, ct);
        Expect(s.Version, Number(a, "expectedVersion"));
        if (a.ContainsKey("title")) s.Title = Text(a, "title", 200);
        if (a.ContainsKey("description")) s.Description = OptionalText(a, "description", 10000);
        if (a.ContainsKey("status"))
        {
            var status = Text(a, "status", 20);
            if (status is not ("todo" or "in_progress" or "done")) throw Error("validation_error", 400, "Unknown step status");
            s.Status = status;
        }
        Touch(s);
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await StepView(s, ct);
    }
    private async Task<object> MoveStep(JsonObject a, CancellationToken ct)
    {
        var p = await ListWriteProject(a, ct);
        var s = await Step(p.Id, Id(a, "stepId"), false, ct);
        var target = await Step(p.Id, Id(a, "targetStepId"), false, ct);
        if (s.Id == target.Id) throw Error("validation_error", 400, "A step cannot move relative to itself");
        var placement = Text(a, "placement", 10);
        if (placement is not ("before" or "after")) throw Error("validation_error", 400, "placement must be before or after");
        var all = await db.Steps.Where(x => x.ProjectId == p.Id).OrderBy(x => x.Position).ToListAsync(ct);
        var ordered = all.Where(x => x.Id != s.Id).ToList();
        var index = ordered.FindIndex(x => x.Id == target.Id);
        ordered.Insert(index + (placement == "after" ? 1 : 0), s);
        for (var i = 0; i < ordered.Count; i++) ordered[i].Position = i + 1;
        TouchList(p);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS uq_steps_project_position DEFERRED", ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await StepView(s, ct);
    }
    private async Task<object> DeleteStep(JsonObject a, CancellationToken ct)
    {
        var p = await ListWriteProject(a, ct);
        var s = await Step(p.Id, Id(a, "stepId"), false, ct);
        Expect(s.Version, Number(a, "expectedVersion"));
        s.DeletedAt = DateTimeOffset.UtcNow;
        Touch(s);
        TouchList(p);
        await db.SaveChangesAsync(ct);
        return await StepView(s, ct);
    }
    private async Task<object> RestoreStep(JsonObject a, CancellationToken ct)
    {
        var p = await ListWriteProject(a, ct);
        var s = await Step(p.Id, Id(a, "stepId"), true, ct);
        Expect(s.Version, Number(a, "expectedVersion"));
        if (s.DeletedAt == null) throw Error("invalid_state", 409, "Step is not deleted");
        s.DeletedAt = null;
        Touch(s);
        TouchList(p);
        await db.SaveChangesAsync(ct);
        return await StepView(s, ct);
    }

    private async Task LockDependencies(CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({GroupId.ToString()}, 0))", ct);
    private static bool Reaches(Guid from, Guid target, IReadOnlyCollection<(Guid from, Guid to)> edges)
    {
        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(from);
        while (pending.TryPop(out var current))
        {
            if (current == target) return true;
            if (!seen.Add(current)) continue;
            foreach (var edge in edges.Where(x => x.from == current)) pending.Push(edge.to);
        }
        return false;
    }
    private async Task<object> ProjectDependency(JsonObject a, bool add, CancellationToken ct)
    {
        var p = await Project(Id(a, "projectId"), false, ct);
        Editable(p);
        Expect(p.Version, Number(a, "expectedVersion"));
        var prerequisite = await Project(Id(a, "prerequisiteId"), !add, ct);
        if (p.Id == prerequisite.Id) throw Error("dependency_cycle", 409, "A project cannot depend on itself");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockDependencies(ct);
        var link = await db.ProjectDependencies.SingleOrDefaultAsync(x => x.ProjectId == p.Id && x.PrerequisiteId == prerequisite.Id, ct);
        if (add)
        {
            if (link != null) throw Error("duplicate_dependency", 409, "Dependency already exists");
            var links = await db.ProjectDependencies.Select(x => new { x.ProjectId, x.PrerequisiteId }).ToListAsync(ct);
            if (Reaches(prerequisite.Id, p.Id, links.Select(x => (x.ProjectId, x.PrerequisiteId)).ToList()))
                throw Error("dependency_cycle", 409, "Dependency would form a cycle");
            db.ProjectDependencies.Add(new ProjectDependency { GroupId = GroupId, ProjectId = p.Id, PrerequisiteId = prerequisite.Id });
        }
        else
        {
            if (link == null) throw Error("not_found", 404, "Dependency not found");
            db.ProjectDependencies.Remove(link);
        }
        Touch(p);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ProjectView(p, ct);
    }
    private async Task<object> StepDependency(JsonObject a, bool add, CancellationToken ct)
    {
        var p = await Project(Id(a, "projectId"), false, ct);
        Editable(p);
        var s = await Step(p.Id, Id(a, "stepId"), false, ct);
        Expect(s.Version, Number(a, "expectedVersion"));
        var prerequisite = await Step(p.Id, Id(a, "prerequisiteId"), !add, ct);
        if (s.Id == prerequisite.Id) throw Error("dependency_cycle", 409, "A step cannot depend on itself");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockDependencies(ct);
        var link = await db.StepDependencies.SingleOrDefaultAsync(x => x.ProjectId == p.Id && x.StepId == s.Id && x.PrerequisiteId == prerequisite.Id, ct);
        if (add)
        {
            if (link != null) throw Error("duplicate_dependency", 409, "Dependency already exists");
            var links = await db.StepDependencies.Where(x => x.ProjectId == p.Id)
                .Select(x => new { x.StepId, x.PrerequisiteId }).ToListAsync(ct);
            if (Reaches(prerequisite.Id, s.Id, links.Select(x => (x.StepId, x.PrerequisiteId)).ToList()))
                throw Error("dependency_cycle", 409, "Dependency would form a cycle");
            db.StepDependencies.Add(new StepDependency { GroupId = GroupId, ProjectId = p.Id, StepId = s.Id, PrerequisiteId = prerequisite.Id });
        }
        else
        {
            if (link == null) throw Error("not_found", 404, "Dependency not found");
            db.StepDependencies.Remove(link);
        }
        Touch(s);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await StepView(s, ct);
    }
}

public sealed record MemberSession(Member Member);
