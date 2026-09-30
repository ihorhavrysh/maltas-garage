using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Infrastructure.Services;

/// <summary>
/// Puts the public demo back to its seeded state: every marketplace row and every account
/// (demo accounts included, they are recreated) is deleted, visitor uploads are removed from
/// disk and <see cref="DemoDataSeeder"/> runs again, all inside one transaction.
/// Categories, roles and real (non-demo) admin accounts are kept.
/// </summary>
public class DemoResetService
{
    private static readonly string[] UploadFolders = { "listings", "avatars", "disputes" };

    private readonly ApplicationDbContext _context;
    private readonly DemoDataSeeder _seeder;
    private readonly LocalUploadStorage _uploads;
    private readonly DemoSettings _settings;
    private readonly ILogger<DemoResetService> _logger;

    public DemoResetService(
        ApplicationDbContext context,
        DemoDataSeeder seeder,
        LocalUploadStorage uploads,
        IOptions<DemoSettings> settings,
        ILogger<DemoResetService> logger)
    {
        _context = context;
        _seeder = seeder;
        _uploads = uploads;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Resets when the newest seed is older than Demo:ResetIntervalDays.</summary>
    public async Task<bool> ResetIfDueAsync()
    {
        var lastSeed = await _context.DemoResets.MaxAsync(r => (DateTime?)r.CreatedAt);
        if (lastSeed == null)
        {
            // Seeded before resets were recorded: start the clock now instead of never resetting
            _context.DemoResets.Add(new DemoReset { Id = Guid.NewGuid(), Reason = "Baseline" });
            await _context.SaveChangesAsync();
            return false;
        }

        if (DateTime.UtcNow - lastSeed.Value < TimeSpan.FromDays(_settings.ResetIntervalDays))
            return false;

        await ResetAsync("Scheduled");
        return true;
    }

    public async Task ResetAsync(string reason)
    {
        // The whole reset is one retriable unit: with connection resiliency on, a transient
        // failure rolls back and the execution strategy runs it again from the start
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync();

            // Real (non-demo) admins survive, e.g. the one from Seed:AdminEmail pressing the button
            var adminRoleId = await _context.Roles.Where(r => r.Name == "Admin").Select(r => r.Id).FirstOrDefaultAsync();
            var demoUserIds = _context.UserProfiles.Where(p => p.IsDemoAccount).Select(p => p.UserId);
            var keep = await _context.UserRoles
                .Where(ur => ur.RoleId == adminRoleId && !demoUserIds.Contains(ur.UserId))
                .Select(ur => ur.UserId)
                .ToListAsync();

            // Children before parents, so no foreign key is ever violated
            await _context.ChatMessages.ExecuteDeleteAsync();
            await _context.SystemMessages.ExecuteDeleteAsync();
            await _context.BundleOfferItems.ExecuteDeleteAsync();
            await _context.BundleOffers.ExecuteDeleteAsync();
            await _context.PriceOffers.ExecuteDeleteAsync();
            await _context.Conversations.ExecuteDeleteAsync();
            await _context.Reviews.ExecuteDeleteAsync();
            await _context.DisputeAttachments.ExecuteDeleteAsync();
            await _context.Disputes.ExecuteDeleteAsync();
            await _context.HandoverCodes.ExecuteDeleteAsync();
            await _context.Shipments.ExecuteDeleteAsync();
            await _context.Payments.ExecuteDeleteAsync();
            await _context.OrderItems.ExecuteDeleteAsync();
            await _context.Orders.ExecuteDeleteAsync();
            await _context.Bids.ExecuteDeleteAsync();
            await _context.Favourites.ExecuteDeleteAsync();
            await _context.ListingImages.ExecuteDeleteAsync();
            await _context.Listings.ExecuteDeleteAsync();
            await _context.NotificationPreferences.Where(n => !keep.Contains(n.UserProfile.UserId)).ExecuteDeleteAsync();
            await _context.UserProfiles.Where(p => !keep.Contains(p.UserId)).ExecuteDeleteAsync();
            await _context.UserRoles.Where(x => !keep.Contains(x.UserId)).ExecuteDeleteAsync();
            await _context.UserClaims.Where(x => !keep.Contains(x.UserId)).ExecuteDeleteAsync();
            await _context.UserLogins.Where(x => !keep.Contains(x.UserId)).ExecuteDeleteAsync();
            await _context.UserTokens.Where(x => !keep.Contains(x.UserId)).ExecuteDeleteAsync();
            await _context.Users.Where(u => !keep.Contains(u.Id)).ExecuteDeleteAsync();
            await _context.DemoResets.ExecuteDeleteAsync();

            // ExecuteDelete bypasses the change tracker; drop anything it still holds
            _context.ChangeTracker.Clear();

            await _seeder.SeedAsync(reason);
            await transaction.CommitAsync();
        });

        // Files last: if the database part failed, visitor images still match their rows
        DeleteVisitorUploads();
        _logger.LogInformation("Demo reset completed ({Reason})", reason);
    }

    private void DeleteVisitorUploads()
    {
        foreach (var folder in UploadFolders)
        {
            var path = _uploads.FolderPath(folder);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path))
            {
                try { File.Delete(file); }
                catch (IOException ex) { _logger.LogWarning(ex, "Could not delete upload {File}", file); }
            }
        }
    }
}
