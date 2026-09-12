using System.IO;
using System.Text.Json;

namespace SpotifyMiniPlayer;

public static class WindowPositionStore
{
    private static readonly string FilePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Musi",
            "window_position.json");

    private class WindowPosition
    {
        public double Left { get; set; }
        public double Top { get; set; }
    }

    public static void Save(double left, double top)
    {
        try
        {
            string? directory =
                Path.GetDirectoryName(FilePath);

            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            WindowPosition position = new()
            {
                Left = left,
                Top = top
            };

            string json =
                JsonSerializer.Serialize(position);

            File.WriteAllText(
                FilePath,
                json);
        }
        catch
        {
            // Ignore save errors.
        }
    }

    public static (double Left, double Top)? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            string json =
                File.ReadAllText(FilePath);

            WindowPosition? position =
                JsonSerializer.Deserialize<WindowPosition>(
                    json);

            if (position == null)
                return null;

            return (
                position.Left,
                position.Top
            );
        }
        catch
        {
            return null;
        }
    }
}