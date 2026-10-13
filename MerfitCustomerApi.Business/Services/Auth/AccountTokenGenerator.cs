using System.Security.Cryptography;
using System.Text;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>
/// E-posta dogrulama ve sifre sifirlama icin 6 haneli tek kullanimlik kodlari uretir ve ozetler.
/// Kod kriptografik rastgeledir; veritabaninda yalnizca kullaniciya ve amaca bagli SHA-256 ozeti tutulur.
/// 6 hane dusuk entropili oldugu icin asil koruma deneme sinirlaridir: kod basina en fazla
/// <see cref="Common.AccountEmailOptions.MaxCodeAttempts"/> deneme, kisa gecerlilik ve saatlik e-posta siniri.
/// </summary>
public static class AccountTokenGenerator
{
    public const int CodeLength = 6;

    public static (string Code, string CodeHash) CreateCode(long userId, AccountTokenPurpose purpose)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        return (code, Hash(userId, purpose, code));
    }

    public static string Hash(long userId, AccountTokenPurpose purpose, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{purpose}:{code}")));

    /// <summary>Iki ozeti sabit surede karsilastirir.</summary>
    public static bool HashEquals(string expectedHash, string actualHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(actualHash));

    /// <summary>Kod 6 rakamdan olusmali; bosluk ve tireler (kopyala-yapistir) yok sayilir.</summary>
    public static string? Normalize(string? code)
    {
        if (code is null)
        {
            return null;
        }

        var digits = new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return digits.Length == CodeLength && digits.All(char.IsAsciiDigit) ? digits : null;
    }
}
