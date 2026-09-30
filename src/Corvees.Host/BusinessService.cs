using System.Text.Json.Nodes;
using Corvees.Host.Application;
using Corvees.Host.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Corvees.Host;

public sealed class BusinessService(GroupService groups, MemberService members, LocationService locations,
    ProjectService projects, ProjectQueries projectQueries, StepService steps, DependencyService dependencies)
{
    public async Task<OperationResponse> ExecuteAsync(Operation operation, JsonObject values, CancellationToken ct)
    {
        var definition = OperationCatalog.Get(operation);
        var args = new OperationArguments(values);
        try
        {
            return operation switch
            {
                Operation.GetGroup => Resource(await groups.GetAsync(ct)),
                Operation.UpdateGroup => Resource(await groups.UpdateAsync(args.Text("name"), args.Version(), ct)),
                Operation.ListMembers => List(await members.ListAsync(args.Page(), ct)),
                Operation.GetMe => Resource(members.GetMe()),
                Operation.UpdateMe => Resource(await members.UpdateMeAsync(args.Text("displayName"), args.Version(), ct)),
                Operation.ListLocations => List(await locations.ListAsync(args.Page(), args.Boolean("deletedOnly"), ct)),
                Operation.GetLocation => Resource(await locations.GetAsync(args.Id("locationId"), args.Boolean("includeDeleted"), ct)),
                Operation.CreateLocation => Resource(await locations.CreateAsync(args.Text("name"), args.OptionalText("address", 500), ct)),
                Operation.UpdateLocation => Resource(await locations.UpdateAsync(args.Id("locationId"),
                    new(args.TextPatch("name"), args.OptionalTextPatch("address", 500)), args.Version(), ct)),
                Operation.DeleteLocation => Resource(await locations.DeleteAsync(args.Id("locationId"), args.Version(), ct)),
                Operation.RestoreLocation => Resource(await locations.RestoreAsync(args.Id("locationId"), args.Version(), ct)),
                Operation.ListProjects => List(await projectQueries.ListAsync(args.Page(), args.Boolean("deletedOnly"),
                    args.OptionalNonEmptyText("status", 20), args.OptionalId("locationId"), ct)),
                Operation.GetProject => Resource(await projectQueries.GetAsync(args.Id("projectId"), args.Boolean("includeDeleted"), ct)),
                Operation.CreateProject => Resource(await projects.CreateAsync(args.Text("title"), args.OptionalText("description", 10000), args.OptionalId("locationId"), ct)),
                Operation.UpdateProject => Resource(await projects.UpdateAsync(args.Id("projectId"),
                    new(args.TextPatch("title"), args.OptionalTextPatch("description", 10000), args.IdPatch("locationId")), args.Version(), ct)),
                Operation.ArchiveProject => Resource(await projects.SetArchivedAsync(args.Id("projectId"), true, args.Version(), ct)),
                Operation.UnarchiveProject => Resource(await projects.SetArchivedAsync(args.Id("projectId"), false, args.Version(), ct)),
                Operation.DeleteProject => Resource(await projects.DeleteAsync(args.Id("projectId"), args.Version(), ct)),
                Operation.RestoreProject => Resource(await projects.RestoreAsync(args.Id("projectId"), args.Version(), ct)),
                Operation.ListSteps => List(await steps.ListAsync(args.Id("projectId"), args.Page(), args.Boolean("deletedOnly"), ct)),
                Operation.GetStep => Resource(await steps.GetAsync(args.Id("projectId"), args.Id("stepId"), args.Boolean("includeDeleted"), ct)),
                Operation.CreateStep => Resource(await steps.CreateAsync(args.Id("projectId"), args.Text("title"),
                    args.OptionalText("description", 10000), args.Version("expectedListVersion"), ct)),
                Operation.UpdateStep => Resource(await steps.UpdateAsync(args.Id("projectId"), args.Id("stepId"),
                    new(args.TextPatch("title"), args.OptionalTextPatch("description", 10000), args.TextPatch("status", 20)), args.Version(), ct)),
                Operation.MoveStep => Resource(await steps.MoveAsync(args.Id("projectId"), args.Id("stepId"),
                    args.Id("targetStepId"), args.Text("placement", 10), args.Version("expectedListVersion"), ct)),
                Operation.DeleteStep => Resource(await steps.DeleteAsync(args.Id("projectId"), args.Id("stepId"), args.Version(), args.Version("expectedListVersion"), ct)),
                Operation.RestoreStep => Resource(await steps.RestoreAsync(args.Id("projectId"), args.Id("stepId"), args.Version(), args.Version("expectedListVersion"), ct)),
                Operation.AddProjectDependency => Resource(await dependencies.ChangeProjectAsync(args.Id("projectId"), args.Id("prerequisiteId"), args.Version(), true, ct)),
                Operation.RemoveProjectDependency => Resource(await dependencies.ChangeProjectAsync(args.Id("projectId"), args.Id("prerequisiteId"), args.Version(), false, ct)),
                Operation.AddStepDependency => Resource(await dependencies.ChangeStepAsync(args.Id("projectId"), args.Id("stepId"), args.Id("prerequisiteId"), args.Version(), true, ct)),
                Operation.RemoveStepDependency => Resource(await dependencies.ChangeStepAsync(args.Id("projectId"), args.Id("stepId"), args.Id("prerequisiteId"), args.Version(), false, ct)),
                _ => throw DomainException.NotFound("Operation")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainException.VersionConflict();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "uq_steps_project_position" })
        {
            throw DomainException.VersionConflict("The step list changed");
        }

        OperationResponse Resource(IVersionedResource data) => new(definition.Name, data);
        OperationResponse List<T>(Page<T> page) => new PagedOperationResponse(definition.Name, page.Items, page.NextCursor);
    }
}
