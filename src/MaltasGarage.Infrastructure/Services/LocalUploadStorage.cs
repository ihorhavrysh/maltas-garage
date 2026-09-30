using MaltasGarage.Application.Common.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Infrastructure.Services;

/// <summary>
/// Where locally stored uploads live on disk. Their public URLs are always /uploads/...
///
/// By default that is wwwroot/uploads. On Azure App Service, Storage:LocalRootPath points to
/// the persistent /home volume instead (for example /home/data/uploads), because wwwroot is
/// read-only when the app runs from its deployment package and is replaced on every deploy.
/// </summary>
public class LocalUploadStorage
{
    public const string RequestPath = "/uploads";

    public LocalUploadStorage(IOptions<StorageSettings> settings, IWebHostEnvironment environment)
    {
        var configured = settings.Value.LocalRootPath;
        RootPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.WebRootPath, "uploads")
            : Path.GetFullPath(configured);
        IsInsideWebRoot = RootPath.StartsWith(Path.GetFullPath(environment.WebRootPath), StringComparison.OrdinalIgnoreCase);
    }

    public string RootPath { get; }

    /// <summary>False when the root is outside wwwroot and must be served by its own static files middleware.</summary>
    public bool IsInsideWebRoot { get; }

    public string FolderPath(string folder) => Path.Combine(RootPath, folder);

    /// <summary>Maps a public /uploads/... URL back to its file, or null for anything else.</summary>
    public string? PathFromUrl(string url)
    {
        if (!url.StartsWith(RequestPath + "/", StringComparison.OrdinalIgnoreCase))
            return null;

        var relative = url[(RequestPath.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(RootPath, relative));

        // Never resolve outside the uploads root (e.g. "/uploads/../appsettings.json")
        return full.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
