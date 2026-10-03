using MaltasGarage.Application.Common.Interfaces;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MaltasGarage.Web.Areas.Admin.Pages.Staff;

[Authorize(Policy = "RequireAdminRole")]
public class IndexModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IAccountDeletionService _deletion;

    public IndexModel(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager,
        IAccountDeletionService deletion)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _deletion = deletion;
    }

    public List<ManagerRow> Managers { get; set; } = new();
    public string? ErrorMessage { get; set; }

    [BindProperty] public string NewEmail { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;
    [BindProperty] public string NewPasswordConfirm { get; set; } = string.Empty;

    public string CurrentUserId { get; set; } = string.Empty;

    public class ManagerRow
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsCurrentUser { get; set; }
    }

    public async Task OnGetAsync()
    {
        ViewData["ActivePage"] = "Staff";
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await LoadManagersAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        ViewData["ActivePage"] = "Staff";

        if (string.IsNullOrWhiteSpace(NewEmail) || string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "Email and password are required.";
            await LoadManagersAsync();
            return Page();
        }

        if (NewPassword != NewPasswordConfirm)
        {
            ErrorMessage = "Passwords do not match.";
            await LoadManagersAsync();
            return Page();
        }

        if (!await _roleManager.RoleExistsAsync("Manager"))
            await _roleManager.CreateAsync(new IdentityRole("Manager"));

        var existing = await _userManager.FindByEmailAsync(NewEmail);
        if (existing != null)
        {
            ErrorMessage = $"An account with this email already exists. Use a different email to create a separate manager account.";
            await LoadManagersAsync();
            return Page();
        }

        var user = new IdentityUser
        {
            UserName = NewEmail,
            Email = NewEmail,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, NewPassword);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            await LoadManagersAsync();
            return Page();
        }

        await _userManager.AddToRoleAsync(user, "Manager");
        TempData["Success"] = $"Manager {NewEmail} created successfully.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
            await _userManager.RemoveFromRoleAsync(user, "Manager");

        TempData["Success"] = "Manager removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPromoteAsync(string userId)
    {
        if (!await _roleManager.RoleExistsAsync("Admin"))
            await _roleManager.CreateAsync(new IdentityRole("Admin"));

        var user = await _userManager.FindByIdAsync(userId);
        if (user != null && !await _userManager.IsInRoleAsync(user, "Admin"))
            await _userManager.AddToRoleAsync(user, "Admin");

        TempData["Success"] = "User promoted to Admin.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string userId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == currentUserId)
        {
            TempData["Error"] = "You cannot delete your own account.";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return RedirectToPage();

        // Protect against deleting the last admin
        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
            {
                TempData["Error"] = "Cannot delete the last admin account.";
                return RedirectToPage();
            }
        }

        // The same path as a user deleting their own account: a profile with orders or reviews
        // is anonymised, not left behind pointing at a login that no longer exists
        var email = user.Email;
        if (!await _deletion.DeleteAsync(user.Id))
        {
            TempData["Error"] = $"{email} still has open orders or auctions and cannot be deleted yet.";
            return RedirectToPage();
        }

        TempData["Success"] = $"Account {email} has been deleted.";
        return RedirectToPage();
    }

    private async Task LoadManagersAsync()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var managers = await _userManager.GetUsersInRoleAsync("Manager");
        var admins   = await _userManager.GetUsersInRoleAsync("Admin");

        var allStaff = managers.Concat(admins).DistinctBy(u => u.Id).ToList();

        var rows = new List<ManagerRow>();
        foreach (var m in allStaff)
        {
            rows.Add(new ManagerRow
            {
                UserId        = m.Id,
                Email         = m.Email ?? "-",
                IsAdmin       = await _userManager.IsInRoleAsync(m, "Admin"),
                IsManager     = await _userManager.IsInRoleAsync(m, "Manager"),
                IsCurrentUser = m.Id == currentUserId
            });
        }
        Managers = rows;
    }
}
