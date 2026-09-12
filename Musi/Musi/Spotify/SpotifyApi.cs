using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Musi.Spotify;

public class SpotifyApi
{
    private readonly HttpClient _httpClient;

    public SpotifyApi(string accessToken)
    {
        _httpClient = new HttpClient();

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);
    }

    public async Task<SpotifyTrack?> GetCurrentlyPlayingAsync()
    {
        HttpResponseMessage response =
            await _httpClient.GetAsync(
                "https://api.spotify.com/v1/me/player");

        // Nothing is currently playing
        if (response.StatusCode ==
            System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        string json =
            await response.Content.ReadAsStringAsync();

        using JsonDocument document =
            JsonDocument.Parse(json);

        JsonElement root =
            document.RootElement;

        if (!root.TryGetProperty(
                "item",
                out JsonElement item))
        {
            return null;
        }

        string name =
            item.GetProperty("name").GetString() ?? "";

        string artist =
            item.GetProperty("artists")[0]
                .GetProperty("name")
                .GetString() ?? "";

        string album =
            item.GetProperty("album")
                .GetProperty("name")
                .GetString() ?? "";

        string coverUrl =
            item.GetProperty("album")
                .GetProperty("images")[0]
                .GetProperty("url")
                .GetString() ?? "";

        string trackUrl =
            item.GetProperty("external_urls")
                .GetProperty("spotify")
                .GetString() ?? "";

        bool isPlaying =
            root.GetProperty("is_playing")
                .GetBoolean();

        return new SpotifyTrack
        {
            Name = name,
            Artist = artist,
            Album = album,
            CoverUrl = coverUrl,
            TrackUrl = trackUrl,
            IsPlaying = isPlaying
        };
    }

    public async Task PauseAsync()
    {
        HttpResponseMessage response =
            await _httpClient.PutAsync(
                "https://api.spotify.com/v1/me/player/pause",
                null);

        response.EnsureSuccessStatusCode();
    }

    public async Task PlayAsync()
    {
        HttpResponseMessage response =
            await _httpClient.PutAsync(
                "https://api.spotify.com/v1/me/player/play",
                null);

        response.EnsureSuccessStatusCode();
    }

    public async Task NextAsync()
    {
        HttpResponseMessage response =
            await _httpClient.PostAsync(
                "https://api.spotify.com/v1/me/player/next",
                null);

        response.EnsureSuccessStatusCode();
    }

    public async Task PreviousAsync()
    {
        HttpResponseMessage response =
            await _httpClient.PostAsync(
                "https://api.spotify.com/v1/me/player/previous",
                null);

        response.EnsureSuccessStatusCode();
    }
}