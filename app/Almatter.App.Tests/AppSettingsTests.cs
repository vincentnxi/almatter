using System.Text.Json;
using Almatter.App.Models;
using Almatter.App.Services;

namespace Almatter.App.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData(15.5, 15.5)]
    [InlineData(14.3, 14.5)]
    [InlineData(1, AppSettings.MinMessageFontSize)]
    [InlineData(500, AppSettings.MaxMessageFontSize)]
    [InlineData(double.NaN, AppSettings.DefaultMessageFontSize)]
    [InlineData(double.PositiveInfinity, AppSettings.DefaultMessageFontSize)]
    public void FontSizeIsPulledOntoTheGrid(double saved, double expected)
    {
        Assert.Equal(expected, AppSettings.ClampMessageFontSize(saved));
    }

    // The settings file goes through the source-generated JSON context
    // (no reflection, for Native AOT): what is saved must load back.
    [Fact]
    public void SettingsSurviveASaveAndLoad()
    {
        var settings = new AppSettings { MessageFontSize = 17 };
        var json = JsonSerializer.Serialize(settings, StorageJsonContext.Default.AppSettings);
        var loaded = JsonSerializer.Deserialize(json, StorageJsonContext.Default.AppSettings);
        Assert.NotNull(loaded);
        Assert.Equal(json, JsonSerializer.Serialize(loaded, StorageJsonContext.Default.AppSettings));
        Assert.Equal(17, loaded.MessageFontSize);
    }

    [Fact]
    public void UnknownFieldsFromAnotherVersionAreIgnored()
    {
        var loaded = JsonSerializer.Deserialize("{\"SomeFutureSetting\":true}", StorageJsonContext.Default.AppSettings);
        Assert.NotNull(loaded);
    }
}
