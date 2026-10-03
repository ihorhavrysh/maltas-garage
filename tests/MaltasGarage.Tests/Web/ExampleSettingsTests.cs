using System.Reflection;
using System.Text.Json;
using MaltasGarage.Application.Common.Models;

namespace MaltasGarage.Tests.Web;

/// <summary>appsettings.Example.json is the reference of every setting; a new one must be added there.</summary>
public class ExampleSettingsTests
{
    public static TheoryData<Type, string> SettingsClasses => new()
    {
        { typeof(AppSettings), AppSettings.SectionName },
        { typeof(StripeSettings), StripeSettings.SectionName },
        { typeof(StorageSettings), StorageSettings.SectionName },
        { typeof(EmailSettings), EmailSettings.SectionName },
        { typeof(DemoSettings), DemoSettings.SectionName },
        { typeof(SeedSettings), SeedSettings.SectionName }
    };

    [Theory]
    [MemberData(nameof(SettingsClasses))]
    public void ExampleFile_ListsEverySetting(Type settings, string section)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "MaltasGarage.Web", "appsettings.Example.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(json.RootElement.TryGetProperty(section, out var listed), $"Section {section} is missing");

        var bound = settings.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name);
        foreach (var name in bound)
            Assert.True(listed.TryGetProperty(name, out _), $"{section}:{name} is missing from appsettings.Example.json");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MaltasGarage.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
