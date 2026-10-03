namespace MaltasGarage.Application.Common.Interfaces;

public interface IAccountDeletionService
{
    /// <summary>
    /// Reasons the account cannot be deleted yet (money in flight, a running auction the user
    /// sells or leads). Empty when it can.
    /// </summary>
    Task<IReadOnlyList<string>> GetBlockersAsync(Guid profileId);

    /// <summary>
    /// Removes the user's personal data and their login in one transaction. The profile row stays,
    /// anonymised, because orders and reviews of the other party point at it. Returns false when a
    /// blocker appeared in the meantime.
    /// </summary>
    Task<bool> DeleteAsync(string identityUserId);
}
