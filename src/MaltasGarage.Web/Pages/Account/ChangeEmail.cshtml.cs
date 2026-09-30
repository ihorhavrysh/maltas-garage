using System.ComponentModel.DataAnnotations;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class ChangeEmailModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public ChangeEmailModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ApplicationDbContext context,
        ICurrentUserService currentUser)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
        _currentUser = currentUser;
    }

    public Domain.Entities.UserProfile? Profile { get; set; }
    public string? CurrentEmail { get; set; }

    [BindProperty]
    public EmailInput Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public class EmailInput
    {
        [Required(ErrorMessage = "New email address is required")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        [MaxLength(200)]
        public string NewEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required to confirm this change")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();

        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user == null) return NotFound();

        if (string.Equals(Input.NewEmail, CurrentEmail, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "New email is the same as your current email.";
            return Page();
        }

        // Check if new email is already taken
        var existing = await _userManager.FindByEmailAsync(Input.NewEmail);
        if (existing != null)
        {
            ErrorMessage = "This email address is already in use.";
            return Page();
        }

        // Verify password before allowing email change
        var passwordOk = await _userManager.CheckPasswordAsync(user, Input.Password);
        if (!passwordOk)
        {
            ErrorMessage = "Incorrect password.";
            return Page();
        }

        var setResult = await _userManager.SetEmailAsync(user, Input.NewEmail);
        if (!setResult.Succeeded)
        {
            ErrorMessage = setResult.Errors.FirstOrDefault()?.Description ?? "Failed to update email.";
            return Page();
        }

        // Username = email in default Identity setup
        await _userManager.SetUserNameAsync(user, Input.NewEmail);

        await _signInManager.RefreshSignInAsync(user);

        TempData["Success"] = "Email address updated successfully.";
        return RedirectToPage("/Account/Settings");
    }

    private async Task LoadAsync()
    {
        Profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        CurrentEmail = user?.Email;
    }
}
