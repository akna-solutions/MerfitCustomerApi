using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Legal;

/// <summary>
/// GET /api/admin/user-consents liste satiri (TUM kullanicilar genelinde). Tamamen salt-okunurdur;
/// consent verileri admin tarafindan degistirilemez/silinemez (madde 30).
/// NOT: Kasitli olarak "AdminUserConsentListItemDto" DEGIL "AdminConsentListItemDto" adlandirildi;
/// AdminUserController altindaki (tek kullaniciya ozel) Dtos.Admin.Users.AdminUserConsentListItemDto
/// ile ayni sinif adini paylasip Swagger schema Id çakýþmasina yol acmamasi icin.
/// </summary>
public class AdminConsentListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public long DocumentId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public DateTime AcceptedAt { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}

public class AdminUserConsentListRequest : PagedRequest
{
    public long? UserId { get; set; }
    public long? DocumentId { get; set; }
    public bool? Accepted { get; set; }
}