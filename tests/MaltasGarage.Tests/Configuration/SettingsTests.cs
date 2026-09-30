using MaltasGarage.Application.Common.Models;
using MaltasGarage.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Tests.Configuration;

public class SettingsTests
{
    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=Test",
        ["App:BaseUrl"] = "https://localhost:7130",
        ["App:SupportEmail"] = "support@example.com",
        ["Stripe:SecretKey"] = "sk_test_x",
        ["Stripe:PublishableKey"] = "pk_test_x",
        ["Stripe:WebhookSecret"] = "whsec_x",
        ["Storage:Provider"] = "Local",
        ["Email:Provider"] = "Log",
        ["Email:FromEmail"] = "noreply@example.com",
        ["Email:FromName"] = "Malta's Garage"
    };

    private static IServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> With(params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>(ValidSettings);
        foreach (var (key, value) in overrides)
            settings[key] = value;
        return settings;
    }

    [Fact]
    public void ValidSettings_BindWithoutErrors()
    {
        var provider = BuildProvider(ValidSettings);

        Assert.Equal("sk_test_x", provider.GetRequiredService<IOptions<StripeSettings>>().Value.SecretKey);
        Assert.Equal(EmailProvider.Log, provider.GetRequiredService<IOptions<EmailSettings>>().Value.Provider);
    }

    [Theory]
    [InlineData("Stripe:SecretKey")]
    [InlineData("Stripe:WebhookSecret")]
    [InlineData("App:BaseUrl")]
    [InlineData("Email:FromEmail")]
    public void MissingRequiredSetting_FailsValidation(string key)
    {
        var provider = BuildProvider(With((key, "")));

        var ex = Assert.Throws<OptionsValidationException>(() => ResolveAllSettings(provider));
        Assert.Contains(key.Split(':')[1], ex.Message);
    }

    [Fact]
    public void ResendProvider_WithoutApiKey_FailsValidation()
    {
        var provider = BuildProvider(With(("Email:Provider", "Resend")));

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EmailSettings>>().Value);
        Assert.Contains("Email:ResendApiKey", ex.Message);
    }

    [Fact]
    public void AzureBlobProvider_WithoutConnectionString_FailsValidation()
    {
        var provider = BuildProvider(With(("Storage:Provider", "AzureBlob")));

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<StorageSettings>>().Value);
        Assert.Contains("Storage:AzureBlobConnectionString", ex.Message);
    }

    [Fact]
    public void MissingConnectionString_FailsAtRegistration()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildProvider(With(("ConnectionStrings:DefaultConnection", null))));
        Assert.Contains("DefaultConnection", ex.Message);
    }

    private static void ResolveAllSettings(IServiceProvider provider)
    {
        _ = provider.GetRequiredService<IOptions<AppSettings>>().Value;
        _ = provider.GetRequiredService<IOptions<StripeSettings>>().Value;
        _ = provider.GetRequiredService<IOptions<StorageSettings>>().Value;
        _ = provider.GetRequiredService<IOptions<EmailSettings>>().Value;
    }
}
