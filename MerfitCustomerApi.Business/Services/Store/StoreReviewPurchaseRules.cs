using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// "Onayli alisveris" kurali. Mevcut siparis modeline gore (bkz. StoreOrderStatus/StorePaymentStatus) bir yorum,
/// yorumun sahibinin ilgili urunu iceren ve
///   - teslim edilmis (Status = Delivered),
///   - odemesi basarisiz, iptal, iade bekliyor veya iade edilmis olmayan
/// en az bir siparisi varsa onaylidir. Iptal edilen siparis Cancelled olur; iade odeme durumunda (RefundPending /
/// Refunded) izlenir. Kapida odemede teslim aninda odeme Pending kalabildigi icin Pending kabul edilir.
/// MerfitApi'de (admin) ayni kural birebir tekrarlanir; siparis durumu degisince yorumlarin etiketi yeniden hesaplanir.
/// </summary>
public static class StoreReviewPurchaseRules
{
    public static readonly StorePaymentStatus[] DisqualifyingPaymentStatuses =
    {
        StorePaymentStatus.Failed,
        StorePaymentStatus.Cancelled,
        StorePaymentStatus.RefundPending,
        StorePaymentStatus.Refunded,
    };

    /// <summary>Onayli alisverise sayilan siparisler.</summary>
    public static IQueryable<StoreOrder> QualifyingOrders(IQueryable<StoreOrder> orders) =>
        orders.Where(o => o.Status == StoreOrderStatus.Delivered && !DisqualifyingPaymentStatuses.Contains(o.PaymentStatus));
}
