using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// JWT access token ve refresh token uretimini soyutlar.
/// </summary>
public interface ITokenService
{
    /// <summary>Kullanici icin imzali bir JWT access token uretir.</summary>
    /// <returns>Token degeri ve son gecerlilik tarihi (UTC).</returns>
    (string Token, DateTime ExpiresAt) GenerateAccessToken(ApplicationUser user);

    /// <summary>Kriptografik olarak guvenli, rastgele bir refresh token degeri uretir.</summary>
    string GenerateRefreshToken();
}