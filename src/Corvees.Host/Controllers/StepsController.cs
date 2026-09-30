using Corvees.Host.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Corvees.Host.Controllers;

[Route("api/v1/projects/{projectId:guid}/steps")]
public sealed class StepsController(BusinessService service) : ResourceController(service)
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => ExecuteAsync(Operation.ListSteps, ct);

    [HttpGet("{stepId:guid}")]
    public Task<IActionResult> Get(CancellationToken ct) => ExecuteAsync(Operation.GetStep, ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct) => ExecuteAsync(Operation.CreateStep, ct);

    [HttpPatch("{stepId:guid}")]
    public Task<IActionResult> Update(CancellationToken ct) => ExecuteAsync(Operation.UpdateStep, ct);

    [HttpDelete("{stepId:guid}")]
    public Task<IActionResult> Delete(CancellationToken ct) => ExecuteAsync(Operation.DeleteStep, ct);

    [HttpPost("{stepId:guid}/restore")]
    public Task<IActionResult> Restore(CancellationToken ct) => ExecuteAsync(Operation.RestoreStep, ct);

    [HttpPost("{stepId:guid}/move")]
    public Task<IActionResult> Move(CancellationToken ct) => ExecuteAsync(Operation.MoveStep, ct);

    [HttpPost("{stepId:guid}/dependencies")]
    public Task<IActionResult> AddDependency(CancellationToken ct) => ExecuteAsync(Operation.AddStepDependency, ct);

    [HttpDelete("{stepId:guid}/dependencies/{prerequisiteId:guid}")]
    public Task<IActionResult> RemoveDependency(CancellationToken ct) => ExecuteAsync(Operation.RemoveStepDependency, ct);
}
