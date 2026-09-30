using System.Collections.Frozen;

namespace Corvees.Host.Contracts;

public static class OperationCatalog
{
    public static IReadOnlyList<OperationDefinition> Definitions { get; } = Array.AsReadOnly<OperationDefinition>(
    [
        new(Operation.GetGroup, "get_group", ResourceKind.Group, [], [], ReadOnly: true),
        new(Operation.UpdateGroup, "update_group", ResourceKind.Group, ["name", "expectedVersion"], []),
        new(Operation.ListMembers, "list_members", ResourceKind.Member, [], ["limit", "cursor"], IsList: true, ReadOnly: true),
        new(Operation.GetMe, "get_me", ResourceKind.Member, [], [], ReadOnly: true),
        new(Operation.UpdateMe, "update_me", ResourceKind.Member, ["displayName", "expectedVersion"], []),
        new(Operation.ListLocations, "list_locations", ResourceKind.Location, [], ["limit", "cursor", "deletedOnly"], IsList: true, ReadOnly: true),
        new(Operation.GetLocation, "get_location", ResourceKind.Location, ["locationId"], ["includeDeleted"], ReadOnly: true),
        new(Operation.CreateLocation, "create_location", ResourceKind.Location, ["name"], ["address"]),
        new(Operation.UpdateLocation, "update_location", ResourceKind.Location, ["locationId", "expectedVersion"], ["name", "address"]),
        new(Operation.DeleteLocation, "delete_location", ResourceKind.Location, ["locationId", "expectedVersion"], [], Destructive: true),
        new(Operation.RestoreLocation, "restore_location", ResourceKind.Location, ["locationId", "expectedVersion"], []),
        new(Operation.ListProjects, "list_projects", ResourceKind.Project, [], ["limit", "cursor", "deletedOnly", "status", "locationId"], IsList: true, ReadOnly: true),
        new(Operation.GetProject, "get_project", ResourceKind.Project, ["projectId"], ["includeDeleted"], ReadOnly: true),
        new(Operation.CreateProject, "create_project", ResourceKind.Project, ["title"], ["description", "locationId"]),
        new(Operation.UpdateProject, "update_project", ResourceKind.Project, ["projectId", "expectedVersion"], ["title", "description", "locationId"]),
        new(Operation.ArchiveProject, "archive_project", ResourceKind.Project, ["projectId", "expectedVersion"], []),
        new(Operation.UnarchiveProject, "unarchive_project", ResourceKind.Project, ["projectId", "expectedVersion"], []),
        new(Operation.DeleteProject, "delete_project", ResourceKind.Project, ["projectId", "expectedVersion"], [], Destructive: true),
        new(Operation.RestoreProject, "restore_project", ResourceKind.Project, ["projectId", "expectedVersion"], []),
        new(Operation.ListSteps, "list_steps", ResourceKind.Step, ["projectId"], ["limit", "cursor", "deletedOnly"], IsList: true, ReadOnly: true),
        new(Operation.GetStep, "get_step", ResourceKind.Step, ["projectId", "stepId"], ["includeDeleted"], ReadOnly: true),
        new(Operation.CreateStep, "create_step", ResourceKind.Step, ["projectId", "title", "expectedListVersion"], ["description"]),
        new(Operation.UpdateStep, "update_step", ResourceKind.Step, ["projectId", "stepId", "expectedVersion"], ["title", "description", "status"]),
        new(Operation.MoveStep, "move_step", ResourceKind.Step, ["projectId", "stepId", "targetStepId", "placement", "expectedListVersion"], []),
        new(Operation.DeleteStep, "delete_step", ResourceKind.Step, ["projectId", "stepId", "expectedVersion", "expectedListVersion"], [], Destructive: true),
        new(Operation.RestoreStep, "restore_step", ResourceKind.Step, ["projectId", "stepId", "expectedVersion", "expectedListVersion"], []),
        new(Operation.AddProjectDependency, "add_project_dependency", ResourceKind.Project, ["projectId", "prerequisiteId", "expectedVersion"], []),
        new(Operation.RemoveProjectDependency, "remove_project_dependency", ResourceKind.Project, ["projectId", "prerequisiteId", "expectedVersion"], [], Destructive: true),
        new(Operation.AddStepDependency, "add_step_dependency", ResourceKind.Step, ["projectId", "stepId", "prerequisiteId", "expectedVersion"], []),
        new(Operation.RemoveStepDependency, "remove_step_dependency", ResourceKind.Step, ["projectId", "stepId", "prerequisiteId", "expectedVersion"], [], Destructive: true)
    ]);

    private static readonly FrozenDictionary<Operation, OperationDefinition> ByOperation = Definitions.ToFrozenDictionary(x => x.Operation);
    private static readonly FrozenDictionary<string, OperationDefinition> ByName = Definitions.ToFrozenDictionary(x => x.Name, StringComparer.Ordinal);

    public static OperationDefinition Get(Operation operation) => ByOperation[operation];
    public static bool TryGet(string name, out OperationDefinition definition) => ByName.TryGetValue(name, out definition!);
}
