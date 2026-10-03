using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Web.Areas.Identity.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<RegisterModel> _logger;
    private readonly ApplicationDbContext _context;
    private readonly IEmailNotificationService _emailNotifications;
    private readonly DemoSettings _demo;

    public RegisterModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ILogger<RegisterModel> logger,
        ApplicationDbContext context,
        IEmailNotificationService emailNotifications,
        IOptions<DemoSettings> demo)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _context = context;
        _emailNotifications = emailNotifications;
        _demo = demo.Value;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "You must agree to the Terms of Service and Privacy Policy to create an account.")]
        [Range(typeof(bool), "true", "true", ErrorMessage = "You must agree to the Terms of Service and Privacy Policy to create an account.")]
        public bool AgreeToTerms { get; set; }
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (ModelState.IsValid)
        {
            var user = new IdentityUser { UserName = Input.Email, Email = Input.Email };
            var userProfile = new UserProfile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DisplayName = Input.Email.Split('@')[0],
                CreatedAt = DateTime.UtcNow
            };

            // Demo: visitors can list items straight away, using the shared test-mode
            // connected account instead of going through Stripe Connect onboarding.
            if (_demo.Enabled)
            {
                userProfile.StripeAccountId = _demo.SellerStripeAccountId;
                userProfile.StripeOnboardingComplete = true;
            }

            // The login and its profile are created together or not at all: a login without a
            // profile cannot buy, sell or even be deleted from the account page
            IdentityResult result = IdentityResult.Failed();
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync();
                result = await _userManager.CreateAsync(user, Input.Password);
                if (!result.Succeeded)
                    return;

                _context.UserProfiles.Add(userProfile);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new account.");

                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var confirmationUrl = Url.Page(
                    "/Account/ConfirmEmail",
                    pageHandler: null,
                    values: new { area = "Identity", userId = user.Id, code, returnUrl },
                    protocol: Request.Scheme)!;

                await _emailNotifications.SendWelcomeEmailAsync(user.Email!, userProfile.DisplayName, confirmationUrl);

                await _signInManager.SignInAsync(user, isPersistent: false);
                return LocalRedirect(returnUrl);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        return Page();
    }
}
