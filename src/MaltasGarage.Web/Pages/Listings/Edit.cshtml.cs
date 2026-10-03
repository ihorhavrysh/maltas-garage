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
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IImageService _imageService;

    public EditModel(ApplicationDbContext context, ICurrentUserService currentUser, IImageService imageService)
    {
        _context = context;
        _currentUser = currentUser;
        _imageService = imageService;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile>? Photos { get; set; }

    [BindProperty]
    public List<Guid> RemoveImageIds { get; set; } = [];

    public List<ListingImage> ExistingImages { get; set; } = [];
    public SelectList? Categories { get; set; }

    public class InputModel
    {
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
        [Range(1, 100000)]
        public decimal DesiredPrice { get; set; }

        [Range(0, 100000)]
        public decimal MinPrice { get; set; }

        public bool AuctionEnabled { get; set; } = true;

        [Required(ErrorMessage = "Sell by date is required")]
        [DataType(DataType.Date)]
        public DateTime SellByDate { get; set; }

        [Required(ErrorMessage = "City is required")]
        public string PickupCity { get; set; } = string.Empty;

        public string? PickupAddress { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var listing = await LoadListingAsync();
        if (listing == null) return NotFound();

        var profile = await GetProfileAsync();
        if (profile == null || listing.SellerId != profile.Id) return Forbid();

        if (listing.IsShowcase)
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage("/Listing", new { id = Id });
        }

        if (listing.Status != ListingStatus.Active)
        {
            TempData["Error"] = "This listing can no longer be edited.";
            return RedirectToPage("/Listing", new { id = Id });
        }

        await LoadCategoriesAsync();
        ExistingImages = listing.Images.OrderBy(i => i.SortOrder).ToList();

        Input = new InputModel
        {
            Title = listing.Title,
            CategoryId = listing.CategoryId,
            IsNew = listing.IsNew,
            Description = listing.Description ?? "",
            DesiredPrice = listing.DesiredPrice,
            MinPrice = listing.AuctionEnabled ? listing.MinPrice : Math.Round(listing.DesiredPrice * 0.5m, 2),
            AuctionEnabled = listing.AuctionEnabled,
            SellByDate = listing.SellByDate,
            PickupCity = listing.PickupCity ?? "",
            PickupAddress = listing.PickupAddress
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadCategoriesAsync();

        var listing = await LoadListingAsync();
        if (listing == null) return NotFound();

        var profile = await GetProfileAsync();
        if (profile == null || listing.SellerId != profile.Id) return Forbid();

        if (listing.IsShowcase)
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage("/Listing", new { id = Id });
        }

        ExistingImages = listing.Images.OrderBy(i => i.SortOrder).ToList();

        if (Input.AuctionEnabled && Input.MinPrice > Input.DesiredPrice * 0.8m)
            ModelState.AddModelError("Input.MinPrice", $"Starting auction price cannot exceed 80% of the desired price (max €{Input.DesiredPrice * 0.8m:0})");

        if (Input.SellByDate.Date < DateTime.Today.AddDays(8))
            ModelState.AddModelError("Input.SellByDate", "Sell by date must be at least 8 days from now");

        if (Input.SellByDate.Date > DateTime.Today.AddDays(90))
            ModelState.AddModelError("Input.SellByDate", "Sell by date cannot be more than 90 days from now");

        if (!ModelState.IsValid)
            return Page();

        // Remove images marked for deletion
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

        // Upload new photos
        if (Photos != null && Photos.Any())
        {
            var currentMax = listing.Images
                .Where(i => !RemoveImageIds.Contains(i.Id))
                .Select(i => i.SortOrder)
                .DefaultIfEmpty(-1)
                .Max();

            foreach (var photo in Photos.Take(8 - listing.Images.Count + RemoveImageIds.Count))
            {
                if (photo.Length == 0) continue;
                using var stream = photo.OpenReadStream();
                var result = await _imageService.UploadAsync(stream, photo.FileName, "listings");
                if (result.Success)
                {
                    _context.ListingImages.Add(new ListingImage
                    {
                        Id = Guid.NewGuid(),
                        ListingId = listing.Id,
                        Url = result.Url!,
                        ThumbnailUrl = result.ThumbnailUrl,
                        SortOrder = ++currentMax,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        // Update listing fields
        listing.Title = Input.Title;
        listing.CategoryId = Input.CategoryId;
        listing.IsNew = Input.IsNew;
        listing.Description = Input.Description;
        listing.DesiredPrice = Input.DesiredPrice;
        listing.MinPrice = Input.AuctionEnabled ? Input.MinPrice : Input.DesiredPrice;
        listing.AuctionEnabled = Input.AuctionEnabled;
        listing.SellByDate = Input.SellByDate;
        listing.AuctionStartDate = Input.AuctionEnabled ? Input.SellByDate.AddDays(-7) : null;
        listing.PickupCity = Input.PickupCity;
        listing.PickupAddress = Input.PickupAddress;
        listing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Success"] = "Listing updated successfully.";
        return RedirectToPage("/Listing", new { id = Id });
    }

    private async Task<Listing?> LoadListingAsync() =>
        await _context.Listings
            .Include(l => l.Images)
            .FirstOrDefaultAsync(l => l.Id == Id);

    private async Task<UserProfile?> GetProfileAsync() =>
        await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

    private async Task LoadCategoriesAsync()
    {
        var cats = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
        Categories = new SelectList(cats, "Id", "Name");
    }
}
