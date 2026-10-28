using Microsoft.AspNetCore.Mvc;
using transactions_ingest.Core.Import;

namespace transactions_ingest.Server.Controllers;

[ApiController]
[Route("api/import")]
public class ImportController(ImportService importService, IHostApplicationLifetime lifetime) : ControllerBase
{
    /// <summary>Runs one import synchronously (D11). The lock lives in the service; this only maps it to 409 (D8).</summary>
    [HttpPost]
    [ProducesResponseType<ImportRunSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Run()
    {
        // Not tied to the request: a caller that disconnects should not abort a half-done import. Shutdown still cancels.
        var result = await importService.RunAsync(lifetime.ApplicationStopping);

        if (result.IsAlreadyRunning)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "An import is already running.",
                detail: "Try again when the current import has finished.");
        }

        return Ok(result.Summary);
    }
}
