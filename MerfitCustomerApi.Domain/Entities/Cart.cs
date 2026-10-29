using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>Kullanicinin kalici sepeti (store.Cart); kullanici basina tek sepet (UserId benzersiz).</summary>
public class Cart : BaseEntity
{
    /// <summary>Sepetin sahibi (JWT'deki kullanici; istemciden alinmaz).</summary>
    public long UserId { get; set; }

    public List<CartItem> Items { get; set; } = new();
}
