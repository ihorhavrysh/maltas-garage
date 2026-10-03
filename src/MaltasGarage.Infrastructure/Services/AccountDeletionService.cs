using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class AccountDeletionService : IAccountDeletionService
{
    public const string DeletedMessageBody = "[message deleted]";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IImageService _images;
    private readonly TimeProvider _time;

    public AccountDeletionService(ApplicationDbContext context, UserManager<IdentityUser> userManager,
        IImageService images, TimeProvider time)
    {
        _context = context;
        _userManager = userManager;
        _images = images;
        _time = time;
    }

    public async Task<IReadOnlyList<string>> GetBlockersAsync(Guid profileId)
    {
        var blockers = new List<string>();
        var activeOrderStatuses = new[] { OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Disputed };

        var activeOrderCount = await _context.Orders
            .CountAsync(o => (o.BuyerId == profileId || o.SellerId == profileId)
                          && activeOrderStatuses.Contains(o.Status));
        if (activeOrderCount > 0)
            blockers.Add($"You have {activeOrderCount} active order(s) (Paid/Shipped/Disputed). Please complete or resolve them before deleting your account.");

        var auctionsWithBids = await _context.Listings
            .CountAsync(l => l.SellerId == profileId && l.Status == ListingStatus.AuctionPhase && l.Bids.Any());
        if (auctionsWithBids > 0)
            blockers.Add($"You have {auctionsWithBids} active auction(s) with bids. Please wait for them to complete.");

        // A leading bid is paid for: when the auction closes it becomes an order for this account
        var leadingBids = await _context.Listings
            .Where(l => l.Status == ListingStatus.AuctionPhase && l.Bids.Any())
            .CountAsync(l => l.Bids.OrderByDescending(b => b.Amount).First().BidderId == profileId);
        if (leadingBids > 0)
            blockers.Add($"You are the highest bidder in {leadingBids} running auction(s). Please wait for them to end.");

        return blockers;
    }

    public async Task<bool> DeleteAsync(string identityUserId)
    {
        var filesToDelete = new List<string>();
        var deleted = false;

        // One transaction for the anonymised profile and the Identity user: a failure half way
        // must not leave a login without a profile, or a profile that still holds personal data.
        // The execution strategy retries the whole block on a transient error, so it starts clean
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            filesToDelete.Clear();
            deleted = false;

            var user = await _userManager.FindByIdAsync(identityUserId);
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == identityUserId);
            if (user == null)
                return;

            // Staff accounts have no marketplace profile: only the login goes
            if (profile == null)
            {
                deleted = (await _userManager.DeleteAsync(user)).Succeeded;
                return;
            }

            if ((await GetBlockersAsync(profile.Id)).Count > 0)
                return;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var now = _time.GetUtcNow().UtcDateTime;

            // Listings: open ones are cancelled; photos of listings nobody bought go with them
            var listings = await _context.Listings
                .Include(l => l.Images)
                .Where(l => l.SellerId == profile.Id && l.Status != ListingStatus.Sold)
                .ToListAsync();
            foreach (var listing in listings)
            {
                listing.Status = ListingStatus.Cancelled;
                listing.UpdatedAt = now;
                filesToDelete.AddRange(listing.Images.Select(i => i.Url));
                _context.ListingImages.RemoveRange(listing.Images);
            }

            // Offers the user made or received that are still open
            var offers = await _context.PriceOffers
                .Where(o => (o.BuyerId == profile.Id || o.SellerId == profile.Id) &&
                            (o.Status == PriceOfferStatus.Pending || o.Status == PriceOfferStatus.Accepted))
                .ToListAsync();
            foreach (var offer in offers)
            {
                offer.Status = PriceOfferStatus.Cancelled;
                offer.UpdatedAt = now;
            }

            _context.Favourites.RemoveRange(await _context.Favourites.Where(f => f.UserId == profile.Id).ToListAsync());
            _context.NotificationPreferences.RemoveRange(
                await _context.NotificationPreferences.Where(p => p.UserProfileId == profile.Id).ToListAsync());
            _context.SystemMessages.RemoveRange(await _context.SystemMessages.Where(m => m.UserId == profile.Id).ToListAsync());

            // Chats stay for the other person; only what this user wrote is blanked
            var ownMessages = await _context.ChatMessages
                .Where(m => m.FromUserId == profile.Id && !m.IsSystemNote)
                .ToListAsync();
            foreach (var message in ownMessages)
                message.Body = DeletedMessageBody;

            var attachments = await _context.DisputeAttachments
                .Where(a => a.Dispute.Order.BuyerId == profile.Id)
                .ToListAsync();
            filesToDelete.AddRange(attachments.Select(a => a.Url));
            _context.DisputeAttachments.RemoveRange(attachments);

            if (profile.AvatarUrl != null)
                filesToDelete.Add(profile.AvatarUrl);

            // The profile stays for the other party's orders and reviews, without personal data
            profile.DisplayName = "Deleted User";
            profile.AvatarUrl = null;
            profile.Bio = null;
            profile.Location = null;
            profile.PhoneNumber = null;
            profile.StripeAccountId = null;
            profile.StripeOnboardingComplete = false;
            profile.UserId = $"deleted_{profile.Id}";   // frees the unique UserId index
            profile.UpdatedAt = now;

            await _context.SaveChangesAsync();

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("The account could not be deleted: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));

            await transaction.CommitAsync();
            deleted = true;
        });

        // Files only after the commit: a rollback must not leave rows pointing at deleted files
        if (deleted)
            foreach (var url in filesToDelete)
                await _images.DeleteAsync(url);

        return deleted;
    }
}
