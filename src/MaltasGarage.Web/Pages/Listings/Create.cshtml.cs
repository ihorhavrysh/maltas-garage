using System.ComponentModel.DataAnnotations;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Exceptions;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Listings;

[Authorize]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IImageService _imageService;

    public CreateModel(ApplicationDbContext context, ICurrentUserService currentUser, IImageService imageService)
    {
        _context = context;
        _currentUser = currentUser;
        _imageService = imageService;
    }

    [BindProperty]
    public int CurrentStep { get; set; } = 1;

    [BindProperty]
    public bool IsRelist { get; set; }

    [BindProperty]
    public List<Guid> RemoveImageIds { get; set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public SelectList? Categories { get; set; }
    public List<ListingImage> ExistingImages { get; set; } = new();

    public class InputModel
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Title is required")]
        [StringLength(200, MinimumLength = 5)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Category is required")]
        public Guid CategoryId { get; set; }

        public bool IsNew { get; set; }

        [Required(ErrorMessage = "Description is required")]
        [StringLength(4000, MinimumLength = 20)]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Desired price is required")]
        [Range(1, 100000, ErrorMessage = "Desired price must be between €1 and €100,000")]
        public decimal DesiredPrice { get; set; }

        [Range(0, 100000)]
        public decimal MinPrice { get; set; }

        public bool AuctionEnabled { get; set; } = true;

        [Required(ErrorMessage = "Sell by date is required")]
        [DataType(DataType.Date)]
        public DateTime SellByDate { get; set; } = DateTime.Now.AddDays(14);

        public List<IFormFile>? Photos { get; set; }

        [Required(ErrorMessage = "City is required")]
        public string PickupCity { get; set; } = string.Empty;

        public string? PickupAddress { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid? id, bool relist = false)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null || !userProfile.StripeOnboardingComplete)
        {
            TempData["Error"] = "You need to connect Stripe before you can list items.";
            return RedirectToPage("/Account/ConnectStripe");
        }

        await LoadCategoriesAsync();

        if (id.HasValue)
        {
            var listing = await _context.Listings
                .Include(l => l.Images)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (listing == null) return NotFound();
            if (listing.SellerId != userProfile.Id) return Forbid();
            if (listing.IsShowcase)
            {
                TempData["Error"] = ShowcaseListingException.DefaultMessage;
                return RedirectToPage("/Account/MyListings");
            }

            var blockedStatuses = relist
                ? new[] { ListingStatus.Sold, ListingStatus.Cancelled, ListingStatus.AuctionPhase, ListingStatus.Active }
                : new[] { ListingStatus.Sold, ListingStatus.Cancelled, ListingStatus.AuctionPhase, ListingStatus.Expired };

            if (blockedStatuses.Contains(listing.Status))
            {
                TempData["Error"] = relist ? "Only expired listings can be relisted." : "This listing cannot be edited.";
                return RedirectToPage("/Account/MyListings");
            }

            IsRelist = relist;
            Input = new InputModel
            {
                Id = listing.Id,
                Title = listing.Title,
                CategoryId = listing.CategoryId,
                IsNew = listing.IsNew,
                Description = listing.Description ?? "",
                DesiredPrice = listing.DesiredPrice,
                MinPrice = listing.MinPrice,
                AuctionEnabled = listing.AuctionEnabled,
                SellByDate = relist ? DateTime.Today.AddDays(14) : listing.SellByDate,
                PickupCity = listing.PickupCity ?? "",
                PickupAddress = listing.PickupAddress
            };
            ExistingImages = listing.Images.ToList();
        }
        else if (!string.IsNullOrEmpty(userProfile.Location))
        {
            Input.PickupCity = userProfile.Location;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string action)
    {
        await LoadCategoriesAsync();
        ModelState.Clear();

        if (Input.Id.HasValue)
        {
            var existing = await _context.Listings
                .Include(l => l.Images)
                .FirstOrDefaultAsync(l => l.Id == Input.Id);
            ExistingImages = existing?.Images.ToList() ?? new();
        }

        if (action == "prev")
        {
            CurrentStep = Math.Max(1, CurrentStep - 1);
            ModelState.Remove("CurrentStep");
            return Page();
        }

        var isValid = CurrentStep switch
        {
            1 => ValidateStep1(),
            2 => ValidateStep2(),
            3 => ValidateStep3(),
            4 => ValidateStep4(), // location
            5 => true,            // photos - optional, validated on submit
            _ => true
        };

        if (!isValid)
            return Page();

        if (action == "next")
        {
            CurrentStep = Math.Min(5, CurrentStep + 1);
            ModelState.Remove("CurrentStep");
            return Page();
        }

        if (action == "publish")
        {
            return await PublishListingAsync();
        }

        return Page();
    }

    private bool ValidateStep1()
    {
        if (string.IsNullOrWhiteSpace(Input.Title) || Input.Title.Length < 5)
        {
            ModelState.AddModelError("Input.Title", "Title must be at least 5 characters");
            return false;
        }
        if (Input.CategoryId == Guid.Empty)
        {
            ModelState.AddModelError("Input.CategoryId", "Please select a category");
            return false;
        }
        return true;
    }

    private bool ValidateStep2()
    {
        if (string.IsNullOrWhiteSpace(Input.Description) || Input.Description.Length < 20)
        {
            ModelState.AddModelError("Input.Description", "Description must be at least 20 characters");
            return false;
        }
        return true;
    }

    private bool ValidateStep3()
    {
        if (Input.DesiredPrice < 1)
        {
            ModelState.AddModelError("Input.DesiredPrice", "Desired price must be at least €1");
            return false;
        }
        if (Input.AuctionEnabled)
        {
            if (Input.MinPrice < 1)
            {
                ModelState.AddModelError("Input.MinPrice", "Minimum price must be at least €1");
                return false;
            }
            if (Input.MinPrice > Input.DesiredPrice * 0.8m)
            {
                ModelState.AddModelError("Input.MinPrice", $"Starting auction price cannot exceed 80% of the desired price (max €{Input.DesiredPrice * 0.8m:0})");
                return false;
            }
        }
        if (Input.SellByDate.Date < DateTime.Today.AddDays(8))
        {
            ModelState.AddModelError("Input.SellByDate", "Sell by date must be at least 8 days from now");
            return false;
        }
        if (Input.SellByDate.Date > DateTime.Today.AddDays(90))
        {
            ModelState.AddModelError("Input.SellByDate", "Sell by date cannot be more than 90 days from now");
            return false;
        }
        return true;
    }

    private bool ValidateStep4()
    {
        if (string.IsNullOrWhiteSpace(Input.PickupCity))
        {
            ModelState.AddModelError("Input.PickupCity", "Please select a city");
            return false;
        }
        return true;
    }

    private async Task<IActionResult> PublishListingAsync()
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null || !userProfile.StripeOnboardingComplete)
        {
            TempData["Error"] = "You need to connect Stripe before you can list items.";
            return RedirectToPage("/Account/ConnectStripe");
        }

        Listing listing;

        if (Input.Id.HasValue)
        {
            // Edit or relist existing listing
            listing = await _context.Listings
                .Include(l => l.Images)
                .Include(l => l.Bids)
                .FirstOrDefaultAsync(l => l.Id == Input.Id && l.SellerId == userProfile.Id)
                ?? throw new InvalidOperationException("Listing not found.");

            if (listing.IsShowcase)
            {
                TempData["Error"] = ShowcaseListingException.DefaultMessage;
                return RedirectToPage("/Account/MyListings");
            }

            if (IsRelist)
            {
                if (listing.Status != ListingStatus.Expired)
                {
                    TempData["Error"] = "Only expired listings can be relisted.";
                    return RedirectToPage("/Account/MyListings");
                }
                _context.Bids.RemoveRange(listing.Bids);
                listing.Status = ListingStatus.Active;
                listing.PublishedAt = DateTime.UtcNow;
            }
            else
            {
                if (listing.Status is ListingStatus.Sold or ListingStatus.Cancelled or ListingStatus.AuctionPhase or ListingStatus.Expired)
                {
                    TempData["Error"] = "This listing cannot be edited.";
                    return RedirectToPage("/Account/MyListings");
                }
            }

            listing.CategoryId = Input.CategoryId;
            listing.Title = Input.Title;
            listing.Description = Input.Description;
            listing.IsNew = Input.IsNew;
            listing.DesiredPrice = Input.DesiredPrice;
            listing.MinPrice = Input.AuctionEnabled ? Input.MinPrice : Input.DesiredPrice;
            listing.CurrentPrice = Input.DesiredPrice;
            listing.AuctionEnabled = Input.AuctionEnabled;
            listing.SellByDate = Input.SellByDate;
            listing.AuctionStartDate = Input.AuctionEnabled ? Input.SellByDate.AddDays(-7) : null;
            listing.PickupCity = Input.PickupCity;
            listing.PickupAddress = Input.PickupAddress;
            listing.UpdatedAt = DateTime.UtcNow;

            foreach (var imgId in RemoveImageIds)
            {
                var img = listing.Images.FirstOrDefault(i => i.Id == imgId);
                if (img != null)
                {
                    await _imageService.DeleteAsync(img.Url);
                    if (!string.IsNullOrEmpty(img.ThumbnailUrl))
                        await _imageService.DeleteAsync(img.ThumbnailUrl);
                    _context.ListingImages.Remove(img);
                }
            }

            if (Input.Photos != null && Input.Photos.Any())
            {
                var remainingCount = listing.Images.Count - RemoveImageIds.Count;
                var maxSort = listing.Images.Any() ? listing.Images.Max(i => i.SortOrder) + 1 : 0;
                foreach (var photo in Input.Photos.Take(8 - remainingCount))
                {
                    if (photo.Length > 0)
                    {
                        using var stream = photo.OpenReadStream();
                        var result = await _imageService.UploadAsync(stream, photo.FileName, "listings");
                        if (result.Success)
                        {
                            listing.Images.Add(new ListingImage
                            {
                                Id = Guid.NewGuid(),
                                ListingId = listing.Id,
                                Url = result.Url!,
                                ThumbnailUrl = result.ThumbnailUrl,
                                SortOrder = maxSort++,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
            }
        }
        else
        {
            // Create new listing
            listing = new Listing
            {
                Id = Guid.NewGuid(),
                SellerId = userProfile.Id,
                CategoryId = Input.CategoryId,
                Title = Input.Title,
                Description = Input.Description,
                IsNew = Input.IsNew,
                DesiredPrice = Input.DesiredPrice,
                MinPrice = Input.AuctionEnabled ? Input.MinPrice : Input.DesiredPrice,
                CurrentPrice = Input.DesiredPrice,
                AuctionEnabled = Input.AuctionEnabled,
                SellByDate = Input.SellByDate,
                AuctionStartDate = Input.AuctionEnabled ? Input.SellByDate.AddDays(-7) : null,
                PickupCity = Input.PickupCity,
                PickupAddress = Input.PickupAddress,
                Status = ListingStatus.Active,
                PublishedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            if (Input.Photos != null && Input.Photos.Any())
            {
                int sortOrder = 0;
                foreach (var photo in Input.Photos.Take(8))
                {
                    if (photo.Length > 0)
                    {
                        using var stream = photo.OpenReadStream();
                        var result = await _imageService.UploadAsync(stream, photo.FileName, "listings");
                        if (result.Success)
                        {
                            listing.Images.Add(new ListingImage
                            {
                                Id = Guid.NewGuid(),
                                ListingId = listing.Id,
                                Url = result.Url!,
                                ThumbnailUrl = result.ThumbnailUrl,
                                SortOrder = sortOrder++,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
            }

            _context.Listings.Add(listing);
        }

        await _context.SaveChangesAsync();
        return RedirectToPage("/Listing", new { id = listing.Id });
    }

    private async Task LoadCategoriesAsync()
    {
        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        Categories = new SelectList(categories, "Id", "Name");
    }
}
