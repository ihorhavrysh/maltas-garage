using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MaltasGarage.Web.Services;

namespace MaltasGarage.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    public new int StatusCode { get; set; }

    /// <summary>
    /// The database could not be reached even after EF's retries: on the free tier this is the
    /// serverless database resuming from auto-pause. The page then renders without the layout
    /// (whose header queries the database) and reloads itself.
    /// </summary>
    public bool DatabaseWakingUp { get; set; }

    /// <summary>
    /// The free database used its monthly allowance and is paused until the 1st. The page then
    /// explains that instead of reloading itself, which would only waste the CPU quota.
    /// </summary>
    public bool DatabasePausedForMonth { get; set; }
    public DateTime DatabaseResumesAtUtc { get; set; }

    private readonly ILogger<ErrorModel> _logger;

    public ErrorModel(ILogger<ErrorModel> logger)
    {
        _logger = logger;
    }

    public void OnGet(int? statusCode)
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        StatusCode = statusCode ?? 500;

        var error = HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (DatabaseErrors.IsFreeLimitReached(error))
        {
            DatabasePausedForMonth = true;
            DatabaseResumesAtUtc = DatabaseErrors.ResumesAtUtc(DateTime.UtcNow);
            StatusCode = StatusCodes.Status503ServiceUnavailable;
            Response.StatusCode = StatusCode;
            _logger.LogWarning("Database paused until {ResumesAt} (monthly free allowance used)", DatabaseResumesAtUtc);
        }
        else if (error != null && DatabaseErrors.IsUnavailable(error))
        {
            DatabaseWakingUp = true;
            StatusCode = StatusCodes.Status503ServiceUnavailable;
            Response.StatusCode = StatusCode;
            Response.Headers.RetryAfter = "20";
            _logger.LogWarning(error, "Database unavailable, showing the wake-up page");
        }
    }

    // The exception handler re-executes with the original method, so failed POSTs land here
    public void OnPost(int? statusCode) => OnGet(statusCode);
}
