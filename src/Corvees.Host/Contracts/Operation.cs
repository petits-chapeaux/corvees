namespace Corvees.Host.Contracts;

public enum Operation
{
    GetGroup,
    UpdateGroup,
    ListMembers,
    GetMe,
    UpdateMe,
    ListLocations,
    GetLocation,
    CreateLocation,
    UpdateLocation,
    DeleteLocation,
    RestoreLocation,
    ListProjects,
    GetProject,
    CreateProject,
    UpdateProject,
    ArchiveProject,
    UnarchiveProject,
    DeleteProject,
    RestoreProject,
    ListSteps,
    GetStep,
    CreateStep,
    UpdateStep,
    MoveStep,
    DeleteStep,
    RestoreStep,
    AddProjectDependency,
    RemoveProjectDependency,
    AddStepDependency,
    RemoveStepDependency
}

public enum ResourceKind { Group, Member, Location, Project, Step }

public sealed record OperationDefinition(Operation Operation, string Name, ResourceKind Resource,
    string[] Required, string[] Optional, bool IsList = false, bool ReadOnly = false, bool Destructive = false);
