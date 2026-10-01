using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Infrastructure.Data.Seed;

/// <summary>
/// Seeds the public demo: demo accounts, permanent showcase listings and a resettable
/// marketplace around them (auctions ending soon, offers in every state, orders at every
/// stage, an open dispute, reviews and a chat). All dates are relative to "now".
/// Idempotent: when the demo seller already exists only the showcase is refreshed.
/// </summary>
public class DemoDataSeeder
{
    // Showcase auctions always read "ends in 2-3 days": when fewer than MinRemaining
    // is left, the end time is pushed back out to RollForwardTo.
    public static readonly TimeSpan ShowcaseMinRemaining = TimeSpan.FromDays(2);
    public static readonly TimeSpan ShowcaseRollForwardTo = TimeSpan.FromDays(3);

    private const string ImageBase = "/images/demo/";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly DemoSettings _settings;
    private readonly ILogger<DemoDataSeeder> _logger;

    private DateTime _now;
    private Dictionary<string, Guid> _categories = new();
    private readonly Dictionary<(Guid Buyer, Guid Seller), Conversation> _conversations = new();

    public DemoDataSeeder(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<DemoSettings> settings,
        ILogger<DemoDataSeeder> logger)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SeedAsync(string reason = "Seed")
    {
        if (await _userManager.FindByEmailAsync(DemoAccounts.SellerEmail) != null)
        {
            await RefreshShowcaseAsync();
            return;
        }

        // The reset already runs this inside its own transaction and execution strategy
        if (_context.Database.CurrentTransaction != null)
        {
            await SeedContentAsync(reason);
            return;
        }

        // One transaction for accounts and content: a failure must not leave demo accounts
        // behind without data, or the next start would think the demo is already seeded.
        // With connection resiliency on, a user transaction must run inside the execution
        // strategy so a transient failure (a serverless database resuming) retries all of it.
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            _conversations.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await SeedContentAsync(reason);
            await transaction.CommitAsync();
        });
        _logger.LogInformation("Demo data seeded");
    }

    private async Task SeedContentAsync(string reason)
    {
        _now = DateTime.UtcNow;
        _categories = await _context.Categories.ToDictionaryAsync(c => c.Slug, c => c.Id);

        var maria = await CreateAccountAsync(DemoAccounts.SellerEmail, "Maria Borg", "Valletta",
            "Clearing out the family home in Valletta. Everything is clean and works.", 4.9m, 12, 14);
        var luca = await CreateAccountAsync(DemoAccounts.BuyerEmail, "Luca Vella", "Sliema",
            "Always looking for bikes and vintage furniture.", 5.0m, 3, 0);
        var joseph = await CreateAccountAsync(DemoAccounts.Seller2Email, "Joseph Camilleri", "Mosta",
            "Gadgets, gaming and sports gear.", 4.6m, 8, 9);
        var anna = await CreateAccountAsync(DemoAccounts.Seller3Email, "Anna Grech", "Victoria, Gozo",
            "Mum of two, selling what the kids have outgrown.", 4.8m, 15, 21);
        await CreateAccountAsync(DemoAccounts.AdminEmail, "Demo Admin", null, null, 0m, 0, 0, role: "Admin");

        SeedShowcase(maria, luca, joseph, anna);
        SeedMarketplace(maria, luca, joseph, anna);
        _context.DemoResets.Add(new DemoReset { Id = Guid.NewGuid(), Reason = reason, CreatedAt = _now });

        await _context.SaveChangesAsync();
    }

    /// <summary>Keeps showcase auctions permanently live. Safe to call as often as needed.</summary>
    public async Task RefreshShowcaseAsync()
    {
        var now = DateTime.UtcNow;
        var threshold = now + ShowcaseMinRemaining;

        var stale = await _context.Listings
            .Where(l => l.IsShowcase && l.Status == ListingStatus.AuctionPhase && l.SellByDate < threshold)
            .ToListAsync();

        foreach (var listing in stale)
            listing.SellByDate = now + ShowcaseRollForwardTo;

        // Showcase offers stay "pending, expires within a day" forever
        var offerThreshold = now.AddHours(2);
        var staleOffers = await _context.PriceOffers
            .Where(o => o.Listing.IsShowcase && o.Status == PriceOfferStatus.Pending && o.ExpiresAt < offerThreshold)
            .ToListAsync();
        var staleBundles = await _context.BundleOffers
            .Where(o => o.Items.Any(i => i.Listing.IsShowcase) && o.Status == BundleOfferStatus.Pending && o.ExpiresAt < offerThreshold)
            .ToListAsync();
        staleOffers.ForEach(o => o.ExpiresAt = now.AddHours(20));
        staleBundles.ForEach(o => o.ExpiresAt = now.AddHours(20));

        if (stale.Count + staleOffers.Count + staleBundles.Count > 0)
            await _context.SaveChangesAsync();
    }

    // ── Accounts ─────────────────────────────────────────────────────────────

    private async Task<UserProfile> CreateAccountAsync(
        string email, string displayName, string? location, string? bio,
        decimal rating, int reviews, int sales, string? role = null)
    {
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(user, DemoAccounts.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Could not create demo account {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        if (role != null)
        {
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));
            await _userManager.AddToRoleAsync(user, role);
        }

        var profile = new UserProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DisplayName = displayName,
            Location = location,
            Bio = bio,
            Rating = rating,
            TotalReviews = reviews,
            TotalSales = sales,
            IsDemoAccount = true,
            // Admins do not sell, everyone else shares the test-mode connected account
            StripeAccountId = role == null ? _settings.SellerStripeAccountId : null,
            StripeOnboardingComplete = role == null,
            CreatedAt = _now.AddMonths(-8)
        };
        _context.UserProfiles.Add(profile);
        return profile;
    }

    // ── Showcase: permanent, read-only, one listing per feature ──────────────

    private void SeedShowcase(UserProfile maria, UserProfile luca, UserProfile joseph, UserProfile anna)
    {
        // Fixed price
        Listing(maria, "home-kitchen", "Vintage Oak Writing Desk", "writing-desk", 180m,
            "Solid oak desk from the 1970s with two drawers. A few marks on the top, drawers run smoothly. " +
            "Collection from Valletta or MaltaPost delivery.",
            auction: false, sellBy: _now.AddDays(60), showcase: true);

        // Live auction with several bids
        var bike = Listing(maria, "sports-leisure", "Aluminium Road Bike, 54cm Frame", "road-bike", 420m,
            "Lightweight road bike, 18 gears, new tyres fitted last month. Ideal for the coast road.",
            minPrice: 200m, status: ListingStatus.AuctionPhase, sellBy: _now + ShowcaseRollForwardTo, showcase: true);
        Bids(bike, (luca, 210m, 30), (joseph, 230m, 22), (anna, 250m, 9), (luca, 270m, 2));

        // Open offer waiting for the seller
        var espresso = Listing(maria, "home-kitchen", "Espresso Machine with Milk Frother", "espresso-machine", 120m,
            "15-bar pump espresso machine, descaled and cleaned. Comes with two baskets and a tamper.",
            minPrice: 60m, sellBy: _now.AddDays(20), showcase: true);
        var espressoChat = Conversation(luca, maria, espresso,
            (luca, "Hi Maria, is the milk frother working properly?", 26),
            (maria, "Yes, it works perfectly. I used it every morning until last week.", 25),
            (luca, "Great, I have sent you an offer.", 24));
        Offer(espresso, luca, 95m, PriceOfferStatus.Pending, hoursAgo: 24, espressoChat, expiresInHours: 20);

        // Accepted offer: the buyer can now purchase at the agreed price
        var guitar = Listing(maria, "other", "Acoustic Guitar with Soft Case", "acoustic-guitar", 150m,
            "Full-size steel-string guitar, recently restrung. Includes soft case and a spare set of strings.",
            minPrice: 70m, sellBy: _now.AddDays(25), showcase: true);
        Offer(guitar, anna, 90m, PriceOfferStatus.Rejected, hoursAgo: 72);
        Offer(guitar, luca, 130m, PriceOfferStatus.Accepted, hoursAgo: 20, Conversation(luca, maria, guitar));

        // Bundle offer covering two listings from the same seller
        var books = Listing(maria, "kids-baby", "Children's Picture Books (Set of 12)", "kids-books", 35m,
            "Twelve hardback picture books for ages 3-6, all in very good condition.",
            minPrice: 15m, sellBy: _now.AddDays(30), showcase: true);
        var train = Listing(maria, "kids-baby", "Wooden Toy Train Set", "toy-train", 45m,
            "Wooden train with 20 track pieces, a bridge and three carriages. Compatible with the big brands.",
            minPrice: 20m, sellBy: _now.AddDays(30), showcase: true);
        BundleOffer(luca, maria, 65m, hoursAgo: 5, Conversation(luca, maria, null), books, train);

        // Sold, with a completed order and reviews on both sides
        var camera = Listing(maria, "electronics", "Canon DSLR Camera with 18-55mm Lens", "dslr-camera", 380m,
            "24MP DSLR with kit lens, two batteries and charger. Shutter count around 8,000.",
            minPrice: 200m, status: ListingStatus.Sold, sellBy: _now.AddDays(-10), showcase: true);
        var cameraOrder = Order(camera, luca, 360m, OrderStatus.Completed, DeliveryMethod.HandToHand, daysAgo: 14);
        Review(cameraOrder, luca, maria, 5, "Camera exactly as described and Maria was very friendly at the handover.", 12);
        Review(cameraOrder, maria, luca, 5, "Quick payment and on time for the meetup. Thanks Luca!", 12);

        // Auction that is live but has no bids yet
        Listing(maria, "garden-outdoor", "Teak Garden Lounge Chair", "lounge-chair", 140m,
            "Weatherproof teak lounge chair with a reclining back. Cushion included.",
            minPrice: 60m, status: ListingStatus.AuctionPhase, sellBy: _now + ShowcaseRollForwardTo, showcase: true);
    }

    // ── Marketplace: regular listings that visitors can use and the reset restores ──

    private void SeedMarketplace(UserProfile maria, UserProfile luca, UserProfile joseph, UserProfile anna)
    {
        // Auctions ending in hours and days
        var laptop = Listing(joseph, "electronics", "13-inch Laptop, 16GB RAM, 512GB SSD", "laptop", 450m,
            "Two years old, battery health 89%. Charger included, no scratches on the screen.",
            minPrice: 250m, status: ListingStatus.AuctionPhase, sellBy: _now.AddHours(5));
        Bids(laptop, (luca, 260m, 40), (anna, 280m, 12));

        var headphones = Listing(joseph, "electronics", "Wireless Noise-Cancelling Headphones", "headphones", 120m,
            "Over-ear, 30-hour battery, carry case and cable included.",
            minPrice: 60m, status: ListingStatus.AuctionPhase, sellBy: _now.AddHours(26));
        Bids(headphones, (maria, 70m, 20), (luca, 80m, 6));

        var stroller = Listing(anna, "kids-baby", "Foldable Baby Stroller", "baby-stroller", 110m,
            "Folds with one hand, fits in a small car boot. Rain cover included.",
            minPrice: 50m, status: ListingStatus.AuctionPhase, sellBy: _now.AddDays(2), city: "Victoria");
        Bids(stroller, (luca, 60m, 10));

        Listing(anna, "electronics", "Vintage Film Camera", "dslr-camera", 90m,
            "Fully mechanical 35mm camera, tested with a roll of film last month.",
            minPrice: 40m, status: ListingStatus.AuctionPhase, sellBy: _now.AddHours(50), city: "Victoria");

        // Fixed price and "auction opens later" listings
        Listing(anna, "home-kitchen", "Three-Seater Fabric Sofa", "sofa", 300m,
            "Green fabric sofa, pet-free and smoke-free home. Buyer collects from Gozo.",
            auction: false, sellBy: _now.AddDays(40), city: "Victoria");
        Listing(joseph, "home-kitchen", "Brass Floor Lamp", "floor-lamp", 60m,
            "Adjustable brass floor lamp with a linen shade, E27 bulb.",
            minPrice: 25m, sellBy: _now.AddDays(12));
        Listing(maria, "garden-outdoor", "Monstera Plant in Ceramic Pot", "monstera-plant", 25m,
            "Healthy monstera, about 80cm tall, in a glazed ceramic pot.",
            auction: false, sellBy: _now.AddDays(30));
        Listing(maria, "home-kitchen", "Electric Kettle, 1.7L", "electric-kettle", 15m,
            "Unwanted gift, still in the box.", auction: false, sellBy: _now.AddDays(30), isNew: true);
        Listing(joseph, "sports-leisure", "Size 5 Football", "football", 10m,
            "Match ball, used for one season.", auction: false, sellBy: _now.AddDays(20));
        Listing(anna, "garden-outdoor", "Two-Person Camping Tent", "camping-tent", 55m,
            "Pitched three times, packs down small. Poles and pegs complete.",
            minPrice: 25m, sellBy: _now.AddDays(15), city: "Victoria");
        Listing(anna, "kids-baby", "Large Teddy Bear", "teddy-bear", 12m,
            "60cm teddy bear, washed and ready for a new home.", auction: false, sellBy: _now.AddDays(25), city: "Victoria");
        Listing(joseph, "other", "Beginner Acoustic Guitar", "acoustic-guitar", 65m,
            "Three-quarter size guitar, good for learners aged 8-12.", minPrice: 30m, sellBy: _now.AddDays(18));
        var coffee = Listing(joseph, "home-kitchen", "Capsule Coffee Machine", "espresso-machine", 45m,
            "Compact capsule machine, descaled. Includes a box of capsules.", minPrice: 20m, sellBy: _now.AddDays(22));
        Offer(coffee, maria, 30m, PriceOfferStatus.Pending, hoursAgo: 3, Conversation(maria, joseph, coffee), expiresInHours: 21);

        // Offers that expired or were declined
        var jacket = Listing(anna, "clothing", "Men's Denim Jacket, Size M", "denim-jacket", 30m,
            "Classic blue denim jacket, worn a handful of times.", auction: false, sellBy: _now.AddDays(28), city: "Victoria");
        Offer(jacket, luca, 18m, PriceOfferStatus.Expired, hoursAgo: 50);

        // Expired listings (their sellers can relist them)
        Listing(joseph, "clothing", "Running Sneakers, EU 42", "sneakers", 40m,
            "Worn twice, too small for me.", minPrice: 20m, status: ListingStatus.Expired, sellBy: _now.AddDays(-3));
        Listing(maria, "kids-baby", "Wooden Train Extension Pack", "toy-train", 20m,
            "Extra track pieces and a tunnel.", auction: false, status: ListingStatus.Expired, sellBy: _now.AddDays(-6));

        // Orders at every stage, all bought by the demo buyer
        var paid = Listing(joseph, "electronics", "Bluetooth Speaker Headphones Combo", "headphones", 55m,
            "Headphones plus a small Bluetooth speaker.", auction: false, status: ListingStatus.Sold, sellBy: _now.AddDays(10));
        Order(paid, luca, 55m, OrderStatus.Paid, DeliveryMethod.MaltaPost, daysAgo: 1);

        var shipped = Listing(anna, "sports-leisure", "Kids' Bike, 16-inch", "road-bike", 70m,
            "Suits ages 5-8, stabilisers included.", auction: false, status: ListingStatus.Sold, sellBy: _now.AddDays(10), city: "Victoria");
        Order(shipped, luca, 70m, OrderStatus.Shipped, DeliveryMethod.MaltaPost, daysAgo: 3, shippedDaysAgo: 2);

        var disputed = Listing(joseph, "home-kitchen", "Two-Seater Sofa", "sofa", 150m,
            "Compact two-seater, grey fabric.", auction: false, status: ListingStatus.Sold, sellBy: _now.AddDays(10));
        var disputedOrder = Order(disputed, luca, 150m, OrderStatus.Disputed, DeliveryMethod.MaltaPost, daysAgo: 6, shippedDaysAgo: 5);
        _context.Disputes.Add(new Dispute
        {
            Id = Guid.NewGuid(),
            OrderId = disputedOrder.Id,
            OpenedById = luca.Id,
            Reason = DisputeReason.ItemNotAsDescribed,
            Description = "The listing said no damage, but one armrest has a large tear that was not in the photos.",
            Status = "Open",
            PreviousOrderStatus = OrderStatus.Shipped,
            CreatedAt = _now.AddDays(-1)
        });

        var completed = Listing(anna, "garden-outdoor", "Four-Person Camping Tent", "camping-tent", 95m,
            "Family tent with two rooms, used for one summer.", auction: false, status: ListingStatus.Sold,
            sellBy: _now.AddDays(-5), city: "Victoria");
        var completedOrder = Order(completed, luca, 95m, OrderStatus.Completed, DeliveryMethod.MaltaPost, daysAgo: 20, shippedDaysAgo: 19);
        Review(completedOrder, luca, anna, 4, "Tent was complete and clean, delivery took a couple of days longer than expected.", 15);
        Review(completedOrder, anna, luca, 5, "Great buyer, paid straight away.", 15);

        // A few favourites and notifications so the bell and favourites pages have content
        _context.Favourites.Add(new Favourite { Id = Guid.NewGuid(), UserId = luca.Id, ListingId = laptop.Id, CreatedAt = _now.AddDays(-2) });
        _context.Favourites.Add(new Favourite { Id = Guid.NewGuid(), UserId = luca.Id, ListingId = stroller.Id, CreatedAt = _now.AddDays(-1) });
        Notify(luca, SystemMessageType.BidOutbid, $"You've been outbid on \"{laptop.Title}\". New high bid: €280.", $"/Listing/{laptop.Id}", hoursAgo: 12);
        Notify(luca, SystemMessageType.OrderShipped, $"Your order \"{shipped.Title}\" has been shipped.", null, hoursAgo: 48);
        Notify(joseph, SystemMessageType.DisputeOpened, $"A dispute was opened on your order \"{disputed.Title}\".", null, hoursAgo: 24);
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private Listing Listing(
        UserProfile seller, string categorySlug, string title, string image, decimal price, string description,
        decimal? minPrice = null, bool auction = true, ListingStatus status = ListingStatus.Active,
        DateTime? sellBy = null, bool showcase = false, string? city = null, bool isNew = false)
    {
        var sellByDate = sellBy ?? _now.AddDays(30);
        var published = status is ListingStatus.Expired or ListingStatus.Sold
            ? sellByDate.AddDays(-21)
            : _now.AddDays(-5);

        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = seller.Id,
            CategoryId = _categories[categorySlug],
            Title = title,
            Description = description,
            IsNew = isNew,
            DesiredPrice = price,
            MinPrice = auction ? minPrice ?? Math.Round(price * 0.5m) : price,
            CurrentPrice = status == ListingStatus.AuctionPhase ? minPrice ?? price : price,
            AuctionEnabled = auction,
            // Same rule as the create page: the auction phase covers the last 7 days
            AuctionStartDate = auction ? sellByDate.AddDays(-7) : null,
            SellByDate = sellByDate,
            Status = status,
            IsShowcase = showcase,
            PickupCity = city ?? seller.Location,
            ViewCount = Random.Shared.Next(15, 240),
            PublishedAt = published,
            CreatedAt = published
        };
        _context.Listings.Add(listing);
        _context.ListingImages.Add(new ListingImage
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            Url = ImageBase + image + ".svg",
            ThumbnailUrl = ImageBase + image + ".svg",
            SortOrder = 0
        });
        return listing;
    }

    private void Bids(Listing listing, params (UserProfile Bidder, decimal Amount, int HoursAgo)[] bids)
    {
        foreach (var (bidder, amount, hoursAgo) in bids)
        {
            _context.Bids.Add(new Bid
            {
                Id = Guid.NewGuid(),
                ListingId = listing.Id,
                BidderId = bidder.Id,
                Amount = amount,
                CreatedAt = _now.AddHours(-hoursAgo)
            });
        }
        listing.CurrentPrice = bids.Max(b => b.Amount);
    }

    /// <summary>A buyer and a seller share one conversation (unique index), so it is reused.</summary>
    private Conversation Conversation(UserProfile buyer, UserProfile seller, Listing? listing,
        params (UserProfile From, string Body, int HoursAgo)[] messages)
    {
        if (!_conversations.TryGetValue((buyer.Id, seller.Id), out var conversation))
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                BuyerId = buyer.Id,
                SellerId = seller.Id,
                ListingId = listing?.Id,
                CreatedAt = _now.AddDays(-2),
                UpdatedAt = _now.AddHours(-1)
            };
            _context.Conversations.Add(conversation);
            _conversations[(buyer.Id, seller.Id)] = conversation;
        }

        foreach (var (from, body, hoursAgo) in messages)
        {
            _context.ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                FromUserId = from.Id,
                Body = body,
                IsRead = true,
                CreatedAt = _now.AddHours(-hoursAgo)
            });
        }
        return conversation;
    }

    private void Offer(Listing listing, UserProfile buyer, decimal amount, PriceOfferStatus status, int hoursAgo,
        Conversation? conversation = null, int expiresInHours = -1)
    {
        var offer = new PriceOffer
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = listing.SellerId,
            Amount = amount,
            Status = status,
            ExpiresAt = expiresInHours > 0 ? _now.AddHours(expiresInHours) : _now.AddHours(24 - hoursAgo),
            ConversationId = conversation?.Id,
            CreatedAt = _now.AddHours(-hoursAgo)
        };
        _context.PriceOffers.Add(offer);

        if (conversation != null)
        {
            _context.ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                FromUserId = buyer.Id,
                Body = $"Offer of €{amount:N0} for \"{listing.Title}\"",
                IsSystemNote = true,
                OfferRef = offer.Id,
                IsRead = true,
                CreatedAt = offer.CreatedAt
            });
        }
    }

    private void BundleOffer(UserProfile buyer, UserProfile seller, decimal amount, int hoursAgo,
        Conversation conversation, params Listing[] listings)
    {
        var offer = new BundleOffer
        {
            Id = Guid.NewGuid(),
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            OfferAmount = amount,
            TotalListedPrice = listings.Sum(l => l.DesiredPrice),
            Status = BundleOfferStatus.Pending,
            ExpiresAt = _now.AddHours(24 - hoursAgo),
            ConversationId = conversation.Id,
            CreatedAt = _now.AddHours(-hoursAgo)
        };
        _context.BundleOffers.Add(offer);

        foreach (var listing in listings)
        {
            _context.BundleOfferItems.Add(new BundleOfferItem
            {
                Id = Guid.NewGuid(),
                BundleOfferId = offer.Id,
                ListingId = listing.Id,
                ListedPrice = listing.DesiredPrice
            });
        }

        _context.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            FromUserId = buyer.Id,
            Body = $"Bundle offer of €{amount:N0} for {listings.Length} items",
            IsSystemNote = true,
            BundleOfferRef = offer.Id,
            IsRead = true,
            CreatedAt = offer.CreatedAt
        });
    }

    /// <summary>
    /// Seeded orders carry no Stripe PaymentIntent: they exist to be looked at. Purchases a
    /// visitor makes with a test card go through Stripe for real.
    /// </summary>
    private Order Order(Listing listing, UserProfile buyer, decimal price, OrderStatus status,
        DeliveryMethod delivery, int daysAgo, int? shippedDaysAgo = null)
    {
        var platformFee = PlatformFee.Calculate(price);
        var paidAt = _now.AddDays(-daysAgo);
        var done = status == OrderStatus.Completed;
        listing.CurrentPrice = price; // what it actually sold for, as OrderService does

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = listing.SellerId,
            FinalPrice = price,
            PlatformFee = platformFee,
            SellerPayout = price - platformFee,
            Status = status,
            DeliveryMethod = delivery,
            PaidAt = paidAt,
            CompletedAt = done ? paidAt.AddDays(2) : null,
            CreatedAt = paidAt
        };
        _context.Orders.Add(order);

        _context.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Amount = price,
            Status = done ? PaymentStatus.Released : PaymentStatus.Captured,
            CapturedAt = paidAt,
            ReleasedAt = done ? paidAt.AddDays(2) : null,
            CreatedAt = paidAt
        });

        if (shippedDaysAgo.HasValue)
        {
            var shippedAt = _now.AddDays(-shippedDaysAgo.Value);
            _context.Shipments.Add(new Shipment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Method = delivery,
                Carrier = "MaltaPost",
                TrackingNumber = $"RR{Random.Shared.Next(100000000, 999999999)}MT",
                Status = done ? ShipmentStatus.Delivered : ShipmentStatus.InTransit,
                ShippedAt = shippedAt,
                DeliveredAt = done ? shippedAt.AddDays(1) : null,
                DeliveryDeadline = shippedAt.AddDays(7),
                CreatedAt = shippedAt
            });
        }

        return order;
    }

    private void Review(Order order, UserProfile from, UserProfile to, int rating, string comment, int daysAgo)
    {
        _context.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromUserId = from.Id,
            ToUserId = to.Id,
            Rating = rating,
            Comment = comment,
            CreatedAt = _now.AddDays(-daysAgo)
        });
    }

    private void Notify(UserProfile user, SystemMessageType type, string body, string? link, int hoursAgo)
    {
        _context.SystemMessages.Add(new SystemMessage
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = type,
            Body = body,
            Link = link,
            IsRead = false,
            CreatedAt = _now.AddHours(-hoursAgo)
        });
    }
}
