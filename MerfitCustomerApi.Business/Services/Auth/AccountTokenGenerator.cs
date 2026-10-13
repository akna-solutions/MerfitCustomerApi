using System.Security.Cryptography;
using System.Text;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>
/// E-posta baglantilarindaki tek kullanimlik jetonlari uretir ve ozetler. Jeton 256 bit kriptografik
/// rastgele degerdir (base64url, 43 karakter); veritabaninda yalnizca SHA-256 ozeti tutulur. Jeton zaten
/// yuksek entropili oldugu icin tuz/yavas hash gerekmez; ozetten jeton geri elde edilemez.
/// </summary>
public static class AccountTokenGenerator
{
    public const int TokenLength = 43;

    public static (string Token, string TokenHash) Create()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Bicimi gecersiz jetonlar veritabanina hic sorulmadan reddedilir.</summary>
    public static bool HasValidFormat(string? token) =>
        token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
