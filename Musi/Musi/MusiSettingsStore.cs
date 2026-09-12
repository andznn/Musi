using System.IO;
using System.Text.Json;

namespace SpotifyMiniPlayer;

public static class MusiSettingsStore
{
    private static readonly string FilePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Musi",
            "settings.json");

    private class Settings
    {
        public bool AlwaysOnTop { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
    }

    public static bool LoadAlwaysOnTop()
    {
        try
        {
            if (!File.Exists(FilePath))
                return true;

            string json = File.ReadAllText(FilePath);
            Settings? settings =
                JsonSerializer.Deserialize<Settings>(json);

            return settings?.AlwaysOnTop ?? true;
        }
        catch
        {
            return true;
        }
    }

    public static bool LoadStartWithWindows()
    {
        try
        {
            if (!File.Exists(FilePath))
                return false;

            string json = File.ReadAllText(FilePath);

            Settings? settings =
                JsonSerializer.Deserialize<Settings>(json);

            return settings?.StartWithWindows ?? false;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveAlwaysOnTop(bool alwaysOnTop)
    {
        Settings settings = LoadSettings();
        settings.AlwaysOnTop = alwaysOnTop;
        WriteSettings(settings);
    }

    public static void SaveStartWithWindows(bool startWithWindows)
    {
        Settings settings = LoadSettings();
        settings.StartWithWindows = startWithWindows;
        WriteSettings(settings);
    }

    private static Settings LoadSettings()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Settings();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    private static void WriteSettings(Settings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);

            if (directory != null)
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Ignore save errors.
        }
    }

}
