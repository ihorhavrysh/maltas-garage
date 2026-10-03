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
public class DeleteAccountModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly IAccountDeletionService _deletion;

    public DeleteAccountModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        IAccountDeletionService deletion)
    {
        _context = context;
        _currentUser = currentUser;
        _userManager = userManager;
        _signInManager = signInManager;
        _deletion = deletion;
    }

    [BindProperty]
    [Required(ErrorMessage = "Please enter your password to confirm.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public List<string> Blockers { get; set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadBlockersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Index");

        if (!await _userManager.CheckPasswordAsync(user, Password))
        {
            ModelState.AddModelError(nameof(Password), "Incorrect password.");
            await LoadBlockersAsync();
            return Page();
        }

        await LoadBlockersAsync();
        if (Blockers.Any())
            return Page();

        // The service checks the blockers again inside its transaction
        if (!await _deletion.DeleteAsync(user.Id))
        {
            await LoadBlockersAsync();
            return Page();
        }

        await _signInManager.SignOutAsync();
        return RedirectToPage("/Index");
    }

    private async Task LoadBlockersAsync()
    {
        Blockers.Clear();

        var profileId = await _context.UserProfiles
            .Where(p => p.UserId == _currentUser.UserId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync();

        if (profileId != null)
            Blockers.AddRange(await _deletion.GetBlockersAsync(profileId.Value));
    }
}
