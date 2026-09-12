namespace MerfitCustomerApi.Business.Dtos.Admin.Auth;

/// <summary>GET /api/admin/auth/me yaniti; admin panelinin giris yapan kullaniciyi tanimasi icin kullanilir.</summary>
public class AdminMeResponse
{
    public long UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime? LastLoginAt { get; set; }
}
