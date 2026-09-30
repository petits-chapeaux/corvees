using Corvees.Host.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Corvees.Host.Controllers;

[Route("api/v1/projects")]
public sealed class ProjectsController(BusinessService service) : ResourceController(service)
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => ExecuteAsync(Operation.ListProjects, ct);

    [HttpGet("{projectId:guid}")]
    public Task<IActionResult> Get(CancellationToken ct) => ExecuteAsync(Operation.GetProject, ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct) => ExecuteAsync(Operation.CreateProject, ct);

    [HttpPatch("{projectId:guid}")]
    public Task<IActionResult> Update(CancellationToken ct) => ExecuteAsync(Operation.UpdateProject, ct);

    [HttpDelete("{projectId:guid}")]
    public Task<IActionResult> Delete(CancellationToken ct) => ExecuteAsync(Operation.DeleteProject, ct);

    [HttpPost("{projectId:guid}/restore")]
    public Task<IActionResult> Restore(CancellationToken ct) => ExecuteAsync(Operation.RestoreProject, ct);

    [HttpPost("{projectId:guid}/archive")]
    public Task<IActionResult> Archive(CancellationToken ct) => ExecuteAsync(Operation.ArchiveProject, ct);

    [HttpPost("{projectId:guid}/unarchive")]
    public Task<IActionResult> Unarchive(CancellationToken ct) => ExecuteAsync(Operation.UnarchiveProject, ct);

    [HttpPost("{projectId:guid}/dependencies")]
    public Task<IActionResult> AddDependency(CancellationToken ct) => ExecuteAsync(Operation.AddProjectDependency, ct);

    [HttpDelete("{projectId:guid}/dependencies/{prerequisiteId:guid}")]
    public Task<IActionResult> RemoveDependency(CancellationToken ct) => ExecuteAsync(Operation.RemoveProjectDependency, ct);
}
