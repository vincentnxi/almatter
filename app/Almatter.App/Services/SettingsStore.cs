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
            var loaded = JsonSerializer.Deserialize(json, StorageJsonContext.Default.AppSettings);
            if (loaded is not null)
            {
                // The size is free now, so old saved values (13, 14,5, 16,5…)
                // are kept as they are; only out-of-range or hand-edited
                // garbage is pulled back between the picker's bounds.
                loaded.MessageFontSize = AppSettings.ClampMessageFontSize(loaded.MessageFontSize);
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
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(settings, StorageJsonContext.Default.AppSettings));
        }
        catch
        {
            // Persisting preferences must never crash the app.
        }
    }
}
