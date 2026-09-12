using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Musi.Spotify;

public class SpotifyAuth
{
    private const string ClientId =
        "0259c1902aad492e877d7fb548e02c53";

    private const string RedirectUri =
        "http://127.0.0.1:8888/callback";

    private const string Scope =
        "user-read-currently-playing " +
        "user-read-playback-state " +
        "user-modify-playback-state";

    public async Task<string> LoginAsync()
    {
        string codeVerifier =
            GenerateCodeVerifier();

        string codeChallenge =
            GenerateCodeChallenge(codeVerifier);

        string state =
            GenerateRandomString(32);

        using HttpListener listener = new();

        listener.Prefixes.Add(
            "http://127.0.0.1:8888/callback/");

        listener.Start();

        string authorizationUrl =
            "https://accounts.spotify.com/authorize" +
            "?response_type=code" +
            $"&client_id={Uri.EscapeDataString(ClientId)}" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            "&code_challenge_method=S256" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}";

        Process.Start(new ProcessStartInfo
        {
            FileName = authorizationUrl,
            UseShellExecute = true
        });

        HttpListenerContext context =
            await listener.GetContextAsync();

        string query =
            context.Request.Url?.Query ?? "";

        Dictionary<string, string> parameters =
            ParseQueryString(query);

        parameters.TryGetValue(
            "state",
            out string? returnedState);

        parameters.TryGetValue(
            "code",
            out string? code);

        parameters.TryGetValue(
            "error",
            out string? error);

        string responseHtml = """
            <html>
                <head>
                    <title>Musi - Spotify Connected</title>
                </head>
                <body style="font-family: Arial; text-align:center; padding-top:50px;">
                    <h1>Spotify connected!</h1>
                    <p>You can close this window.</p>
                </body>
            </html>
            """;

        byte[] responseBytes =
            Encoding.UTF8.GetBytes(responseHtml);

        context.Response.ContentType =
            "text/html";

        context.Response.ContentLength64 =
            responseBytes.Length;

        await context.Response.OutputStream
            .WriteAsync(responseBytes);

        context.Response.Close();

        listener.Stop();

        if (!string.IsNullOrEmpty(error))
        {
            throw new Exception(
                $"Spotify authorization failed: {error}");
        }

        if (returnedState != state)
        {
            throw new Exception(
                "Spotify authorization state mismatch.");
        }

        if (string.IsNullOrEmpty(code))
        {
            throw new Exception(
                "Spotify did not return an authorization code.");
        }

        SpotifyTokens tokens =
            await ExchangeCodeForTokenAsync(
                code,
                codeVerifier);

        // Save the refresh token securely.
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            TokenStore.SaveRefreshToken(
                tokens.RefreshToken);
        }

        return tokens.AccessToken;
    }

    public async Task<string?> TryRefreshAccessTokenAsync()
    {
        string? refreshToken =
            TokenStore.LoadRefreshToken();

        if (string.IsNullOrEmpty(refreshToken))
            return null;

        try
        {
            using HttpClient client = new();

            var values =
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = ClientId
                };

            using var content =
                new FormUrlEncodedContent(values);

            HttpResponseMessage response =
                await client.PostAsync(
                    "https://accounts.spotify.com/api/token",
                    content);

            if (!response.IsSuccessStatusCode)
            {
                TokenStore.DeleteRefreshToken();
                return null;
            }

            string responseBody =
                await response.Content.ReadAsStringAsync();

            using var json =
                System.Text.Json.JsonDocument.Parse(
                    responseBody);

            string accessToken =
                json.RootElement
                    .GetProperty("access_token")
                    .GetString()!;

            // Spotify may return a new refresh token.
            if (json.RootElement.TryGetProperty(
                    "refresh_token",
                    out var newRefreshToken))
            {
                string? newToken =
                    newRefreshToken.GetString();

                if (!string.IsNullOrEmpty(newToken))
                {
                    TokenStore.SaveRefreshToken(
                        newToken);
                }
            }

            return accessToken;
        }
        catch
        {
            return null;
        }
    }

    private async Task<SpotifyTokens>
        ExchangeCodeForTokenAsync(
            string code,
            string codeVerifier)
    {
        using HttpClient client = new();

        var values =
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = codeVerifier
            };

        using var content =
            new FormUrlEncodedContent(values);

        HttpResponseMessage response =
            await client.PostAsync(
                "https://accounts.spotify.com/api/token",
                content);

        string responseBody =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"Spotify token request failed: {responseBody}");
        }

        using var json =
            System.Text.Json.JsonDocument.Parse(
                responseBody);

        string accessToken =
            json.RootElement
                .GetProperty("access_token")
                .GetString()!;

        string? refreshToken = null;

        if (json.RootElement.TryGetProperty(
                "refresh_token",
                out var refreshTokenElement))
        {
            refreshToken =
                refreshTokenElement.GetString();
        }

        return new SpotifyTokens
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken
        };
    }

    private static string GenerateCodeVerifier()
    {
        byte[] bytes = new byte[64];

        RandomNumberGenerator.Fill(bytes);

        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(
        string codeVerifier)
    {
        byte[] bytes =
            Encoding.ASCII.GetBytes(codeVerifier);

        byte[] hash =
            SHA256.HashData(bytes);

        return Base64UrlEncode(hash);
    }

    private static string GenerateRandomString(
        int length)
    {
        byte[] bytes =
            RandomNumberGenerator.GetBytes(length);

        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(
        byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static Dictionary<string, string>
        ParseQueryString(string query)
    {
        var result =
            new Dictionary<string, string>();

        if (query.StartsWith("?"))
            query = query[1..];

        foreach (string part in query.Split('&'))
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            string[] pieces =
                part.Split('=', 2);

            string key =
                Uri.UnescapeDataString(
                    pieces[0].Replace("+", " "));

            string value =
                pieces.Length > 1
                    ? Uri.UnescapeDataString(
                        pieces[1].Replace("+", " "))
                    : "";

            result[key] = value;
        }

        return result;
    }
}

public class SpotifyTokens
{
    public string AccessToken { get; set; } = "";

    public string? RefreshToken { get; set; }
}