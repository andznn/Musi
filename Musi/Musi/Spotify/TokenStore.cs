using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Musi.Spotify;

public static class TokenStore
{
    private static readonly string TokenPath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Musi",
            "spotify_token.dat");

    public static void SaveRefreshToken(string refreshToken)
    {
        string? directory =
            Path.GetDirectoryName(TokenPath);

        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        byte[] plainText =
            Encoding.UTF8.GetBytes(refreshToken);

        byte[] encrypted =
            ProtectedData.Protect(
                plainText,
                null,
                DataProtectionScope.CurrentUser);

        File.WriteAllBytes(
            TokenPath,
            encrypted);
    }

    public static string? LoadRefreshToken()
    {
        if (!File.Exists(TokenPath))
            return null;

        try
        {
            byte[] encrypted =
                File.ReadAllBytes(TokenPath);

            byte[] plainText =
                ProtectedData.Unprotect(
                    encrypted,
                    null,
                    DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(
                plainText);
        }
        catch
        {
            return null;
        }
    }

    public static void DeleteRefreshToken()
    {
        if (File.Exists(TokenPath))
        {
            File.Delete(TokenPath);
        }
    }
}