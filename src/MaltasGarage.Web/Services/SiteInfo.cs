using MaltasGarage.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Web.Services;

/// <summary>
/// Absolute URLs (canonical links, Open Graph, JSON-LD, sitemap, robots.txt) and the support
/// address, all from configuration, so the same build runs on any domain.
/// Available in every view as <c>Site</c>.
/// </summary>
public class SiteInfo
{
    public SiteInfo(IOptions<AppSettings> appSettings)
    {
        BaseUrl = appSettings.Value.BaseUrl.TrimEnd('/');
        SupportEmail = appSettings.Value.SupportEmail;
    }

    /// <summary>Site root without a trailing slash, e.g. https://example.azurewebsites.net</summary>
    public string BaseUrl { get; }

    public string SupportEmail { get; }

    /// <summary>Turns a site-relative path into an absolute URL; absolute URLs pass through.</summary>
    public string Absolute(string pathOrUrl) =>
        Uri.IsWellFormedUriString(pathOrUrl, UriKind.Absolute)
            ? pathOrUrl
            : $"{BaseUrl}/{pathOrUrl.TrimStart('/')}";
}
