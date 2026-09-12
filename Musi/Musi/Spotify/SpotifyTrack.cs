namespace Musi.Spotify;

public class SpotifyTrack
{
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string TrackUrl { get; set; } = "";
    public bool IsPlaying { get; set; }
}