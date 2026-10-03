using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Infrastructure.Data.Seed;

/// <summary>
/// Creates the Admin and Manager roles and, when Seed:AdminEmail and Seed:AdminPassword are
/// configured (User Secrets or App Service settings), a first admin account. No credentials
/// live in code; the public demo gets its admin from <see cref="DemoDataSeeder"/>.
/// </summary>
public static class AdminSeeder
{
    public static async Task SeedAdminAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var seed = services.GetRequiredService<IOptions<SeedSettings>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminSeeder));
        var db = services.GetRequiredService<ApplicationDbContext>();

        foreach (var role in new[] { "Admin", "Manager" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                LogFailure(logger, $"create role {role}", await roleManager.CreateAsync(new IdentityRole(role)));
        }

        if (!seed.HasAdmin)
            return;

        var existing = await userManager.FindByEmailAsync(seed.AdminEmail!);
        if (existing != null)
        {
            if (!await userManager.IsInRoleAsync(existing, "Admin"))
                LogFailure(logger, "add the seed admin to Admin", await userManager.AddToRoleAsync(existing, "Admin"));
            return;
        }

        var admin = new IdentityUser
        {
            UserName = seed.AdminEmail,
            Email = seed.AdminEmail,
            EmailConfirmed = true
        };

        // A password that fails the policy used to leave the site without an admin and no hint why
        var created = await userManager.CreateAsync(admin, seed.AdminPassword!);
        if (LogFailure(logger, "create the seed admin", created))
            return;

        LogFailure(logger, "add the seed admin to Admin", await userManager.AddToRoleAsync(admin, "Admin"));

        db.UserProfiles.Add(new UserProfile
        {
            UserId = admin.Id,
            DisplayName = "Admin"
        });
        await db.SaveChangesAsync();
    }

    private static bool LogFailure(ILogger logger, string action, IdentityResult result)
    {
        if (result.Succeeded)
            return false;

        logger.LogError("Could not {Action}: {Errors}", action,
            string.Join("; ", result.Errors.Select(e => e.Description)));
        return true;
    }
}
