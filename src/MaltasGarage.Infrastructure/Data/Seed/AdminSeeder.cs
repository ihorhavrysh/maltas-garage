using MaltasGarage.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        var configuration = services.GetRequiredService<IConfiguration>();
        var db = services.GetRequiredService<ApplicationDbContext>();

        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new IdentityRole("Admin"));

        if (!await roleManager.RoleExistsAsync("Manager"))
            await roleManager.CreateAsync(new IdentityRole("Manager"));

        var adminEmail = configuration["Seed:AdminEmail"];
        var adminPassword = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            return;

        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing != null)
        {
            if (!await userManager.IsInRoleAsync(existing, "Admin"))
                await userManager.AddToRoleAsync(existing, "Admin");
            return;
        }

        var admin = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (!result.Succeeded) return;

        await userManager.AddToRoleAsync(admin, "Admin");

        db.UserProfiles.Add(new UserProfile
        {
            UserId = admin.Id,
            DisplayName = "Admin"
        });
        await db.SaveChangesAsync();
    }
}
