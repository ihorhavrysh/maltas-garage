using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Tests.Web;

/// <summary>
/// The whole app on an in-memory SQLite database. Requests sign in through a test scheme: the
/// <see cref="UserHeader"/> header names the Identity user, whose roles come from the database
/// like they would for a cookie.
/// </summary>
public class MarketplaceWebFactory : WebApplicationFactory<Program>
{
    public const string UserHeader = "X-Test-User";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public MarketplaceWebFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused");
        builder.UseSetting("App:BaseUrl", "https://localhost");
        builder.UseSetting("App:SupportEmail", "support@example.test");
        builder.UseSetting("Demo:Enabled", "false");
        builder.UseSetting("Stripe:SecretKey", "sk_test_unused");
        builder.UseSetting("Stripe:PublishableKey", "pk_test_unused");
        builder.UseSetting("Stripe:WebhookSecret", "whsec_unused");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions>();
            // EF keeps the UseSqlServer call as a separate registration; it has to go too
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));

            // No background sweep against the test database
            var sweep = services.Single(d => d.ImplementationType == typeof(MarketplaceSweepService));
            services.Remove(sweep);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    public HttpClient ClientFor(string identityUserId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(UserHeader, identityUserId);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async Task<IdentityUser> CreateUserAsync(string email, params string[] roles)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await users.CreateAsync(user, "Test-Password-1");
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        foreach (var role in roles)
            await users.AddToRoleAsync(user, role);
        return user;
    }

    /// <summary>GETs a page and POSTs a form back with its antiforgery token, like a browser.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string getUrl, string postUrl,
        Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(getUrl);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(token))
            token = Regex.Match(page, "name=\"request-verification-token\" content=\"([^\"]+)\"").Groups[1].Value;

        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(form));
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        private readonly UserManager<IdentityUser> _users;

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, UserManager<IdentityUser> users)
            : base(options, logger, encoder) => _users = users;

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var id))
                return AuthenticateResult.NoResult();

            var user = await _users.FindByIdAsync(id.ToString());
            if (user == null)
                return AuthenticateResult.Fail("Unknown test user");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.UserName!)
            };
            claims.AddRange((await _users.GetRolesAsync(user)).Select(r => new Claim(ClaimTypes.Role, r)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
    }
}
