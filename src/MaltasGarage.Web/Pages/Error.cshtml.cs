using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

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
        if (error != null && IsDatabaseUnavailable(error))
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

    private static bool IsDatabaseUnavailable(Exception error)
    {
        for (var e = error; e != null; e = e.InnerException)
        {
            if (e is SqlException or TimeoutException)
                return true;
        }
        return false;
    }
}
