using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MerfitCustomerApi.Api.Extensions;

/// <summary>
/// JWT claim'lerinden mevcut (giris yapmis) kullaniciya ait bilgileri okumak icin yardimci metotlar.
/// Admin controller'larinda audit log'a yazilacak "islemi yapan admin" bilgisini elde etmek icin kullanilir
/// (bkz. madde 44 - "Admin kullanici bilgisi JWT claim uzerinden alinmali").
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Token'daki "sub" claim'inden kullanici Id'sini okur; bulunamazsa/parse edilemezse null doner.</summary>
    public static long? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return long.TryParse(value, out var userId) ? userId : null;
    }

    /// <summary>Token'daki "email" claim'ini okur.</summary>
    public static string? GetEmail(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(JwtRegisteredClaimNames.Email)
            ?? principal.FindFirstValue(ClaimTypes.Email);
    }

    /// <summary>Token'daki rol claim'ini okur (User/Admin/SuperAdmin).</summary>
    public static string? GetRole(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Role);
    }
}
