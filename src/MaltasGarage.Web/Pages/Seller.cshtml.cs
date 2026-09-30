using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages;

public class SellerModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public SellerModel(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public SellerViewModel? Seller { get; set; }
    public List<ListingViewModel> Listings { get; set; } = new();
    public List<ReviewViewModel> Reviews { get; set; } = new();
    public int ActiveListings { get; set; }
    public bool IsOwnProfile { get; set; }

    public class SellerViewModel
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string? Location { get; set; }
        public string? Bio { get; set; }
        public decimal Rating { get; set; }
        public int TotalReviews { get; set; }
        public int TotalSales { get; set; }
        public string JoinedDate { get; set; } = string.Empty;
    }

    public class ListingViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public decimal Price { get; set; }
        public ListingStatus Status { get; set; }
        public DateTime SellByDate { get; set; }
        public DateTime? AuctionStartDate { get; set; }
        public decimal MinPrice { get; set; }
        public decimal? CurrentBid { get; set; }
        public string? PickupCity { get; set; }
        public string? CategoryName { get; set; }
    }

    public class ReviewViewModel
    {
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public string FromUserName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var user = await _context.UserProfiles
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        if (!string.IsNullOrEmpty(_currentUser.UserId))
            IsOwnProfile = user.UserId == _currentUser.UserId;

        Seller = new SellerViewModel
        {
            Id = user.Id,
            DisplayName = user.DisplayName ?? "User",
            AvatarUrl = user.AvatarUrl,
            Location = user.Location,
            Bio = user.Bio,
            Rating = user.Rating,
            TotalReviews = user.TotalReviews,
            TotalSales = user.TotalSales,
            JoinedDate = user.CreatedAt.ToString("MMMM yyyy")
        };

        Listings = await _context.Listings
            .Include(l => l.Images)
            .Include(l => l.Bids)
            .Include(l => l.Category)
            .Where(l => l.SellerId == id)
            .Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase)
            .OrderByDescending(l => l.CreatedAt)
            .Take(12)
            .Select(l => new ListingViewModel
            {
                Id = l.Id,
                Title = l.Title,
                ImageUrl = l.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault(),
                Price = l.CurrentPrice,
                Status = l.Status,
                SellByDate = l.SellByDate,
                AuctionStartDate = l.AuctionStartDate,
                MinPrice = l.MinPrice,
                CurrentBid = l.Bids.Any() ? l.Bids.Max(b => b.Amount) : (decimal?)null,
                PickupCity = l.PickupCity,
                CategoryName = l.Category != null ? l.Category.Name : null
            })
            .ToListAsync();

        ActiveListings = Listings.Count;

        Reviews = await _context.Reviews
            .Include(r => r.FromUser)
            .Where(r => r.ToUserId == id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .Select(r => new ReviewViewModel
            {
                Rating = r.Rating,
                Comment = r.Comment,
                FromUserName = r.FromUser.DisplayName ?? "User",
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return Page();
    }
}
