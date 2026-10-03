using System.Security.Claims;

namespace MaltasGarage.Web.Services;

/// <summary>
/// Who counts as staff. The Admin area is guarded by the RequireAdminOrManagerRole policy; pages
/// outside it that show staff-only actions (a dispute seen from the order) use this same rule.
/// </summary>
public static class StaffRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";

    public static bool IsStaff(this ClaimsPrincipal user) => user.IsInRole(Admin) || user.IsInRole(Manager);
}
