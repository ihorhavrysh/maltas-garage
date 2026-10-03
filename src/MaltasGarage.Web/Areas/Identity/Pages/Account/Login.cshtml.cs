using System.ComponentModel.DataAnnotations;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MaltasGarage.Web.Areas.Identity.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ILogger<LoginModel> _logger;
    private readonly ApplicationDbContext _context;
    private readonly DemoSettings _demo;

    public LoginModel(SignInManager<IdentityUser> signInManager, ILogger<LoginModel> logger,
        ApplicationDbContext context, IOptions<DemoSettings> demo)
    {
        _signInManager = signInManager;
        _logger = logger;
        _context = context;
        _demo = demo.Value;
    }

    // Failed logins lock an account for a while (Identity: 5 attempts, 5 minutes). The shared
    // demo accounts are the exception: their password is public, so locking them would let any
    // visitor lock everyone else out by typing a wrong password five times
    private async Task<bool> LockoutAppliesAsync(string email)
    {
        if (!_demo.Enabled) return true;

        var user = await _signInManager.UserManager.FindByEmailAsync(email);
        return user == null || !await _context.UserProfiles.AnyAsync(p => p.UserId == user.Id && p.IsDemoAccount);
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        returnUrl ??= Url.Content("~/");

        // Clear existing external cookie
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (ModelState.IsValid)
        {
            var result = await _signInManager.PasswordSignInAsync(
                Input.Email,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: await LockoutAppliesAsync(Input.Email));

            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in.");
                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }

            ModelState.AddModelError(string.Empty, "Invalid email or password.");
        }

        return Page();
    }
}
