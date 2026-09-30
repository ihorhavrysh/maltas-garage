using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MaltasGarage.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Infrastructure.Services;

public class EmailNotificationService : IEmailNotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IEmailService _email;
    private readonly string _baseUrl;
    private readonly string _supportEmail;
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly EmailTemplate _template;

    public EmailNotificationService(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        IEmailService email,
        IOptions<AppSettings> appSettings,
        EmailTemplate template,
        ILogger<EmailNotificationService> logger)
    {
        _template = template;
        _context = context;
        _userManager = userManager;
        _email = email;
        _baseUrl = appSettings.Value.BaseUrl.TrimEnd('/');
        _supportEmail = appSettings.Value.SupportEmail;
        _logger = logger;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string email, string name)?> GetContactAsync(Guid profileId)
    {
        var profile = await _context.UserProfiles.FindAsync(profileId);
        if (profile == null) return null;
        var user = await _userManager.FindByIdAsync(profile.UserId);
        if (user?.Email == null) return null;
        return (user.Email, profile.DisplayName ?? "there");
    }

    private async Task<NotificationPreferences> GetOrCreatePrefsAsync(Guid profileId)
    {
        var prefs = await _context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserProfileId == profileId);
        if (prefs != null) return prefs;

        prefs = new NotificationPreferences { UserProfileId = profileId, CreatedAt = DateTime.UtcNow };
        _context.NotificationPreferences.Add(prefs);
        await _context.SaveChangesAsync();
        return prefs;
    }

    // ── Preference-based ─────────────────────────────────────────────────────

    public async Task NotifyBidOutbidAsync(Guid userProfileId, string listingTitle, decimal newBid, Guid listingId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(userProfileId);
            if (!prefs.BidOutbid) return;
            var contact = await GetContactAsync(userProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "You've been outbid",
                $"Hi {contact.Value.name},<br><br>Someone placed a higher bid of <strong>€{newBid:N0}</strong> on <strong>{listingTitle}</strong>.<br><br>Don't give up - place a new bid to stay in the running!",
                "Place a Bid", $"{_baseUrl}/Listing/{listingId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"You've been outbid on \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send outbid email"); }
    }

    public async Task NotifyNewBidOnListingAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid listingId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(sellerProfileId);
            if (!prefs.NewBidOnListing) return;
            var contact = await GetContactAsync(sellerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "New bid on your listing",
                $"Hi {contact.Value.name},<br><br>A new bid of <strong>€{amount:N0}</strong> has been placed on your listing <strong>{listingTitle}</strong>.",
                "View Listing", $"{_baseUrl}/Listing/{listingId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"New bid on \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send new bid email"); }
    }

    public async Task NotifyAuctionWonAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid orderId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(buyerProfileId);
            if (!prefs.AuctionWon) return;
            var contact = await GetContactAsync(buyerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "You won the auction!",
                $"Hi {contact.Value.name},<br><br>Congratulations! You won the auction for <strong>{listingTitle}</strong> with a bid of <strong>€{amount:N0}</strong>.<br><br>View your order for next steps.",
                "View Order", $"{_baseUrl}/Orders/Details/{orderId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"You won the auction for \"{listingTitle}\"!", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send auction won email"); }
    }

    public async Task NotifyNewOrderAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid orderId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(sellerProfileId);
            if (!prefs.NewOrder) return;
            var contact = await GetContactAsync(sellerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "You have a new order",
                $"Hi {contact.Value.name},<br><br>Your item <strong>{listingTitle}</strong> has been purchased for <strong>€{amount:N0}</strong>.<br><br>Please ship or arrange handover as soon as possible.",
                "View Order", $"{_baseUrl}/Orders/Details/{orderId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"New order: \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send new order email"); }
    }

    public async Task SendWelcomeEmailAsync(string email, string displayName, string confirmationUrl)
    {
        try
        {
            var html = _template.Build(
                "Welcome to Malta's Garage!",
                $"Hi {displayName},<br><br>" +
                $"Welcome to Malta's Garage - Malta's local marketplace for buying and selling second-hand items.<br><br>" +
                $"Please confirm your email address to secure your account. Without a confirmed email, you won't be able to reset your password if you ever lose access.<br>" +
                $"Once confirmed, we recommend completing your profile to build trust with buyers and sellers:<br><br>" +
                $"<ul>" +
                $"<li>Add a <strong>profile photo</strong></li>" +
                $"<li>Set a <strong>display name</strong></li>" +
                $"<li>Add your <strong>location</strong> (town or city)</li>" +
                $"<li>Write a short <strong>bio</strong></li>" +
                $"</ul>" +
                $"A complete profile gets more responses and builds confidence with other users.<br><br>" +
                $"Sellers also need to connect a <strong>Stripe account</strong> to receive payouts before listing items (you can do this from your profile page).",
                "Confirm Email", confirmationUrl);
            await _email.SendAsync(email, displayName, "Welcome to Malta's Garage - confirm your email", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send welcome email to {Email}", email); }
    }

    public async Task NotifyAuctionExpiredAsync(Guid sellerProfileId, string listingTitle)
    {
        try
        {
            var contact = await GetContactAsync(sellerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Your auction has ended",
                $"Hi {contact.Value.name},<br><br>Your auction for <strong>{listingTitle}</strong> has ended with no bids. The listing has been marked as expired.<br><br>You can re-list the item at any time.",
                "My Listings", $"{_baseUrl}/Account/MyListings");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Auction ended with no bids: \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send auction expired email"); }
    }

    public async Task NotifyOrderShippedAsync(Guid buyerProfileId, string listingTitle, string trackingNumber, Guid orderId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(buyerProfileId);
            if (!prefs.OrderShipped) return;
            var contact = await GetContactAsync(buyerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Your order has been shipped",
                $"Hi {contact.Value.name},<br><br>Good news! Your order <strong>{listingTitle}</strong> is on its way.<br><br>Tracking number: <strong>{trackingNumber}</strong><br><br>Once you receive your item, please confirm delivery in the app.",
                "Track Order", $"{_baseUrl}/Orders/Details/{orderId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Your order \"{listingTitle}\" has been shipped", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send order shipped email"); }
    }

    public async Task NotifyOrderCompletedSellerAsync(Guid sellerProfileId, string listingTitle, Guid orderId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(sellerProfileId);
            if (!prefs.OrderCompleted) return;
            var contact = await GetContactAsync(sellerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Order completed - payment released",
                $"Hi {contact.Value.name},<br><br>The order for <strong>{listingTitle}</strong> has been completed and payment has been released to your Stripe account.",
                "View Order", $"{_baseUrl}/Orders/Details/{orderId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Payment released: \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send order completed (seller) email"); }
    }

    public async Task NotifyOrderCompletedBuyerAsync(Guid buyerProfileId, string listingTitle, Guid orderId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(buyerProfileId);
            if (!prefs.OrderCompleted) return;
            var contact = await GetContactAsync(buyerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Your order is complete",
                $"Hi {contact.Value.name},<br><br>Your order for <strong>{listingTitle}</strong> is now complete. We hope you enjoy your purchase!<br><br>If you're happy with the item, consider leaving a review for the seller.",
                "View Order", $"{_baseUrl}/Orders/Details/{orderId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Order complete: \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send order completed (buyer) email"); }
    }

    public async Task NotifyNewChatMessageAsync(Guid recipientProfileId, string senderName, string preview, Guid conversationId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(recipientProfileId);
            if (!prefs.NewChatMessage) return;
            var contact = await GetContactAsync(recipientProfileId);
            if (contact == null) return;
            var previewText = preview.Length > 100 ? preview[..100] + "…" : preview;
            var html = _template.Build(
                $"New message from {senderName}",
                $"Hi {contact.Value.name},<br><br>You have a new message from <strong>{senderName}</strong>:<br><br><span style=\"padding:10px;background:#f8f9fa;display:block;border-left:3px solid #0d6efd;border-radius:2px;\"><em>{previewText}</em></span>",
                "View Message", $"{_baseUrl}/Messages?c={conversationId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"New message from {senderName}", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send chat message email"); }
    }

    public async Task NotifyOfferReceivedAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid listingId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(sellerProfileId);
            if (!prefs.OfferUpdate) return;
            var contact = await GetContactAsync(sellerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "New offer on your listing",
                $"Hi {contact.Value.name},<br><br>A buyer has made an offer of <strong>€{amount:N0}</strong> on your listing <strong>{listingTitle}</strong>.<br><br>You have 24 hours to accept or decline.",
                "View Offer", $"{_baseUrl}/Listing/{listingId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"New offer on \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send offer received email"); }
    }

    public async Task NotifyOfferAcceptedAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid listingId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(buyerProfileId);
            if (!prefs.OfferUpdate) return;
            var contact = await GetContactAsync(buyerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Your offer was accepted!",
                $"Hi {contact.Value.name},<br><br>Great news! The seller accepted your offer of <strong>€{amount:N0}</strong> for <strong>{listingTitle}</strong>.<br><br>Open the chat to complete your purchase.",
                "Complete Purchase", $"{_baseUrl}/Listing/{listingId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Offer accepted: \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send offer accepted email"); }
    }

    public async Task NotifyOfferRejectedAsync(Guid buyerProfileId, string listingTitle)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(buyerProfileId);
            if (!prefs.OfferUpdate) return;
            var contact = await GetContactAsync(buyerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Your offer was not accepted",
                $"Hi {contact.Value.name},<br><br>Unfortunately your offer on <strong>{listingTitle}</strong> was not accepted. You can try a different price or browse other listings.",
                "Browse Listings", $"{_baseUrl}/Listings");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Offer update: \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send offer rejected email"); }
    }

    public async Task NotifyBundleOfferReceivedAsync(Guid sellerProfileId, int itemCount, decimal amount, Guid bundleOfferId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(sellerProfileId);
            if (!prefs.OfferUpdate) return;
            var contact = await GetContactAsync(sellerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "New bundle offer",
                $"Hi {contact.Value.name},<br><br>A buyer has made a bundle offer of <strong>€{amount:N0}</strong> for <strong>{itemCount} items</strong>.<br><br>You have 24 hours to accept or decline.",
                "View Offer", $"{_baseUrl}/Messages");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"New bundle offer: €{amount:N0} for {itemCount} items", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send bundle offer received email"); }
    }

    public async Task NotifyBundleOfferAcceptedAsync(Guid buyerProfileId, int itemCount, decimal amount, Guid bundleOfferId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(buyerProfileId);
            if (!prefs.OfferUpdate) return;
            var contact = await GetContactAsync(buyerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Your bundle offer was accepted!",
                $"Hi {contact.Value.name},<br><br>Great news! The seller accepted your bundle offer of <strong>€{amount:N0}</strong> for <strong>{itemCount} items</strong>.<br><br>Open the chat to complete your purchase.",
                "Open Chat", $"{_baseUrl}/Messages");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Bundle offer accepted: €{amount:N0} for {itemCount} items", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send bundle offer accepted email"); }
    }

    public async Task NotifyBundleOfferRejectedAsync(Guid buyerProfileId, int itemCount, decimal amount)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(buyerProfileId);
            if (!prefs.OfferUpdate) return;
            var contact = await GetContactAsync(buyerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Your bundle offer was not accepted",
                $"Hi {contact.Value.name},<br><br>Unfortunately your bundle offer of <strong>€{amount:N0}</strong> for <strong>{itemCount} items</strong> was not accepted. You can try a different price or browse other listings.",
                "Browse Listings", $"{_baseUrl}/Listings");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Bundle offer update: €{amount:N0} for {itemCount} items", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send bundle offer rejected email"); }
    }

    public async Task NotifyDisputeUpdateAsync(Guid userProfileId, string listingTitle, string updateText, Guid orderId)
    {
        try
        {
            var prefs = await GetOrCreatePrefsAsync(userProfileId);
            if (!prefs.DisputeUpdate) return;
            var contact = await GetContactAsync(userProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Dispute update",
                $"Hi {contact.Value.name},<br><br>{updateText}<br><br>Item: <strong>{listingTitle}</strong>",
                "View Dispute", $"{_baseUrl}/Orders/DisputeView/{orderId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Dispute update for \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send dispute update email"); }
    }

    // ── Transactional ────────────────────────────────────────────────────────

    public async Task SendOrderConfirmationAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid orderId)
    {
        try
        {
            var contact = await GetContactAsync(buyerProfileId);
            if (contact == null) return;
            var html = _template.Build(
                "Order confirmed",
                $"Hi {contact.Value.name},<br><br>Thank you for your purchase! Your payment of <strong>€{amount:N0}</strong> for <strong>{listingTitle}</strong> has been received and is held in escrow.<br><br>Your funds will be released to the seller once you confirm receipt of the item.",
                "View Order", $"{_baseUrl}/Orders/Details/{orderId}");
            await _email.SendAsync(contact.Value.email, contact.Value.name, $"Order confirmed: \"{listingTitle}\"", html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send order confirmation email"); }
    }

    public async Task SendContactFormAsync(string fromName, string fromEmail, string subject, string message)
    {
        try
        {
            var html = _template.Build(
                $"Contact Form: {subject}",
                $"<strong>From:</strong> {fromName} ({fromEmail})<br><br>" +
                $"<strong>Subject:</strong> {subject}<br><br>" +
                $"<strong>Message:</strong><br><br>{message.Replace("\n", "<br>")}");
            await _email.SendAsync(_supportEmail, "Support", $"[Contact] {subject}", html, replyTo: $"{fromName} <{fromEmail}>");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send contact form email"); }
    }

    public async Task SendAdminMessageAsync(string recipientUserId, string subject, string messageBody, Guid orderId)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(recipientUserId);
            if (user?.Email == null) return;

            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == recipientUserId);
            var displayName = profile?.DisplayName ?? user.UserName ?? "there";

            var html = _template.Build(
                subject,
                $"Hi {displayName},<br><br>{messageBody.Replace("\n", "<br>")}",
                "View Dispute", $"{_baseUrl}/Orders/DisputeView/{orderId}");

            await _email.SendAsync(user.Email, displayName, subject, html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send admin message to user {UserId}", recipientUserId); }
    }

    public async Task NotifyDisputeOpenedToStaffAsync(string listingTitle, Guid orderId, string buyerName, string sellerName, string reason)
    {
        try
        {
            var admins  = await _userManager.GetUsersInRoleAsync("Admin");
            var managers = await _userManager.GetUsersInRoleAsync("Manager");
            var recipients = admins.Concat(managers).DistinctBy(u => u.Id).ToList();

            if (recipients.Count == 0) return;

            var html = _template.Build(
                "New dispute opened",
                $"A dispute has been opened and requires attention.<br><br>" +
                $"<strong>Item:</strong> {listingTitle}<br>" +
                $"<strong>Buyer:</strong> {buyerName}<br>" +
                $"<strong>Seller:</strong> {sellerName}<br>" +
                $"<strong>Reason:</strong> {reason}",
                "View Dispute", $"{_baseUrl}/Orders/DisputeView/{orderId}");

            foreach (var staff in recipients)
            {
                if (staff.Email == null) continue;
                await _email.SendAsync(staff.Email, staff.UserName ?? "Admin", $"[Dispute] New dispute: \"{listingTitle}\"", html);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send dispute opened notification to staff"); }
    }
}
