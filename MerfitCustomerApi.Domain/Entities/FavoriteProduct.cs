using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>Kullanicinin favori urunu (store.Favorite); (UserId, ProductId) benzersizdir.</summary>
public class FavoriteProduct : BaseEntity
{
    public long UserId { get; set; }

    public long ProductId { get; set; }
}
