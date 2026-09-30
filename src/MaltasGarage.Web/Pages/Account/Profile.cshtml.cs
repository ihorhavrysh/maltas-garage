using System.ComponentModel.DataAnnotations;
using System.Text;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IImageService _imageService;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IEmailSender<IdentityUser> _emailSender;
    private readonly IMemoryCache _cache;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<ProfileModel> _logger;

    public ProfileModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IImageService imageService,
        UserManager<IdentityUser> userManager,
        IEmailSender<IdentityUser> emailSender,
        IMemoryCache cache,
        IPaymentService paymentService,
        ILogger<ProfileModel> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _imageService = imageService;
        _userManager = userManager;
        _emailSender = emailSender;
        _cache = cache;
        _paymentService = paymentService;
        _logger = logger;
    }

    public UserProfile? Profile { get; set; }
    public bool EmailConfirmed { get; set; }
    public List<ReviewViewModel> Reviews { get; set; } = new();

    public class ReviewViewModel
    {
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public string FromUserName { get; set; } = string.Empty;
        public string? FromUserAvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public IFormFile? AvatarFile { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required]
        [StringLength(100)]
        [Display(Name = "Display Name")]
        public string DisplayName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Location { get; set; }

        [StringLength(20)]
        [Phone]
        [Display(Name = "Phone Number")]
        public string? PhoneNumber { get; set; }

        [StringLength(500)]
        public string? Bio { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        Profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (Profile == null)
            return NotFound();

        var user = await _userManager.FindByIdAsync(_currentUser.UserId);
        EmailConfirmed = user?.EmailConfirmed ?? false;

        Input = new InputModel
        {
            DisplayName = Profile.DisplayName ?? "",
            Location = Profile.Location,
            PhoneNumber = Profile.PhoneNumber,
            Bio = Profile.Bio
        };

        Reviews = await _context.Reviews
            .Include(r => r.FromUser)
            .Where(r => r.ToUserId == Profile.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewViewModel
            {
                Rating = r.Rating,
                Comment = r.Comment,
                FromUserName = r.FromUser.DisplayName ?? "User",
                FromUserAvatarUrl = r.FromUser.AvatarUrl,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        Profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (Profile == null)
            return NotFound();

        if (!ModelState.IsValid)
            return Page();

        Profile.DisplayName = Input.DisplayName;
        Profile.Location = Input.Location;
        Profile.PhoneNumber = Input.PhoneNumber;
        Profile.Bio = Input.Bio;
        Profile.UpdatedAt = DateTime.UtcNow;

        if (AvatarFile != null && AvatarFile.Length > 0)
        {
            // Delete old avatar if exists
            if (!string.IsNullOrEmpty(Profile.AvatarUrl))
                await _imageService.DeleteAsync(Profile.AvatarUrl);

            using var stream = AvatarFile.OpenReadStream();
            var result = await _imageService.UploadAsync(stream, AvatarFile.FileName, "avatars");

            if (!result.Success)
            {
                ErrorMessage = result.Error ?? "Avatar upload failed.";
                return RedirectToPage();
            }

            Profile.AvatarUrl = result.Url;
        }

        await _context.SaveChangesAsync();

        SuccessMessage = "Profile updated successfully!";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStripeDashboardAsync()
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null || string.IsNullOrEmpty(profile.StripeAccountId))
            return RedirectToPage();

        try
        {
            var url = await _paymentService.CreateLoginLinkAsync(profile.StripeAccountId);
            return Redirect(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Stripe login link for user {UserId}", _currentUser.UserId);
            ErrorMessage = "Could not open Stripe Dashboard. Please try again later.";
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostResendConfirmationAsync()
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        var cacheKey = $"resend-confirm:{_currentUser.UserId}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            SuccessMessage = "Confirmation email already sent. Please wait a few minutes before requesting another one.";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(_currentUser.UserId);
        if (user == null || user.EmailConfirmed)
            return RedirectToPage();

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var callbackUrl = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { area = "Identity", userId = user.Id, code },
            protocol: Request.Scheme)!;

        await _emailSender.SendConfirmationLinkAsync(user, user.Email!, callbackUrl);

        _cache.Set(cacheKey, true, TimeSpan.FromMinutes(5));

        SuccessMessage = "Confirmation email sent! Please check your inbox.";
        return RedirectToPage();
    }
}
