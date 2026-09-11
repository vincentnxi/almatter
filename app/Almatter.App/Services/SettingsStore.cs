using System;
using System.IO;
using System.Text.Json;
using Almatter.App.Models;

namespace Almatter.App.Services;

/// <summary>Loads/saves AppSettings as JSON under %LOCALAPPDATA%\Almatter, mirroring CrashLogger's layout.</summary>
internal static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Almatter",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded is not null)
            {
                return loaded;
            }
        }
        catch
        {
            // Missing file, corrupt JSON, first run — fall back to defaults.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }
        catch
        {
            // Persisting preferences must never crash the app.
        }
    }
}
