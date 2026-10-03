using Microsoft.Data.SqlClient;

namespace MaltasGarage.Web.Services;

/// <summary>
/// Recognises the two ways the free Azure SQL database is unreachable. While it resumes from
/// auto-pause (up to a minute) a retry helps. Once the monthly free allowance is used up the
/// database stays paused until the 1st of next month (SQL error 42119), and retrying only
/// burns the web app's CPU quota.
/// </summary>
public static class DatabaseErrors
{
    public const int FreeLimitReached = 42119;

    public static bool IsFreeLimitReached(Exception? error)
    {
        for (var e = error; e != null; e = e.InnerException)
        {
            if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(x => x.Number == FreeLimitReached))
                return true;
        }
        return false;
    }

    public static bool IsUnavailable(Exception? error)
    {
        for (var e = error; e != null; e = e.InnerException)
        {
            if (e is SqlException or TimeoutException)
                return true;
        }
        return false;
    }

    /// <summary>When a database paused for the month comes back (midnight UTC on the 1st).</summary>
    public static DateTime ResumesAtUtc(DateTime nowUtc) =>
        new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
}
