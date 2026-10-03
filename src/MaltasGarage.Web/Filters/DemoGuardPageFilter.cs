using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MaltasGarage.Web.Filters;

/// <summary>
/// Keeps the public demo usable for the next visitor. Registered only when Demo:Enabled.
/// <list type="bullet">
/// <item>Demo accounts (published credentials) cannot change password or email, edit their
/// profile or be deleted.</item>
/// <item>Nobody can open Stripe Connect onboarding or the Express dashboard, because every
/// demo seller shares one test connected account.</item>
/// <item>Admins cannot ban, delete, demote or promote demo accounts.</item>
/// <item>Demo accounts cannot change staff at all (the demo admin's password is public).</item>
/// </list>
/// </summary>
public class DemoGuardPageFilter : IAsyncPageFilter
{
    /// <summary>TempData key rendered by _DemoBanner on every page.</summary>
    public const string NoticeKey = "DemoNotice";

    private static readonly string[] DemoAccountSelfServicePaths =
    {
        "/Identity/Account/Manage",
        "/Account/ChangeEmail",
        "/Account/ChangePassword",
        "/Account/DeleteAccount"
    };

    private static readonly string[] AdminUserManagementPaths =
    {
        "/Admin/Users",
        "/Admin/Staff"
    };

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        var path = http.Request.Path;
        var isPost = HttpMethods.IsPost(http.Request.Method);
        var handler = context.HandlerMethod?.Name;

        // Shared Stripe test account: no onboarding, no Express dashboard login links
        if (path.StartsWithSegments("/Account/ConnectStripe") ||
            (isPost && path.StartsWithSegments("/Account/Profile") && handler == "StripeDashboard"))
        {
            context.Result = Block(context, "Stripe account settings are disabled in the demo. Payments use a shared Stripe test account.", "/Account/Profile");
            return;
        }

        var db = http.RequestServices.GetRequiredService<ApplicationDbContext>();

        // Demo accounts manage nothing about themselves
        var selfService = DemoAccountSelfServicePaths.Any(p => path.StartsWithSegments(p)) ||
                          (isPost && path.StartsWithSegments("/Account/Profile"));
        if (selfService && await IsDemoAccountAsync(db, http.User.FindFirstValue(ClaimTypes.NameIdentifier)))
        {
            context.Result = Block(context, "Demo accounts are shared, so their password, email and profile cannot be changed. Register your own account to try this.", "/Account/Settings");
            return;
        }

        // The demo admin password is public, so staff management is read-only for demo accounts:
        // otherwise any visitor could create managers, promote their own account or delete admins
        if (isPost && path.StartsWithSegments("/Admin/Staff") &&
            await IsDemoAccountAsync(db, http.User.FindFirstValue(ClaimTypes.NameIdentifier)))
        {
            context.Result = Block(context, "Staff management is read-only in the demo.", path.Value!);
            return;
        }

        // Admin tools must not lock other visitors out of the demo accounts
        if (isPost && AdminUserManagementPaths.Any(p => path.StartsWithSegments(p)) &&
            http.Request.HasFormContentType &&
            await IsDemoAccountAsync(db, http.Request.Form["userId"].FirstOrDefault()))
        {
            context.Result = Block(context, "Demo accounts cannot be banned, deleted or have their roles changed.", path.Value!);
            return;
        }

        await next();
    }

    private static async Task<bool> IsDemoAccountAsync(ApplicationDbContext db, string? userId) =>
        userId != null && await db.UserProfiles.AnyAsync(p => p.UserId == userId && p.IsDemoAccount);

    private static IActionResult Block(PageHandlerExecutingContext context, string message, string redirectTo)
    {
        if (context.HandlerInstance is PageModel page)
            page.TempData[NoticeKey] = message;
        return new LocalRedirectResult(redirectTo);
    }
}
