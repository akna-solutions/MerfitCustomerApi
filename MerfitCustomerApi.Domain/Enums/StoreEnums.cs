namespace MerfitCustomerApi.Domain.Entities.Enums;

/// <summary>
/// MB Shop siparisinin lojistik durumu. Odeme durumundan (<see cref="StorePaymentStatus"/>) ayridir: kapida odemeli bir
/// siparis "Delivered" olup odemesi henuz "Pending" olabilir.
/// Gecerli gecisler: Received -> Preparing | Cancelled, Preparing -> Shipped | Cancelled, Shipped -> Delivered.
/// </summary>
public enum StoreOrderStatus
{
    /// <summary>Siparis alindi; stok ayrildi.</summary>
    Received,
    /// <summary>Depoda hazirlaniyor.</summary>
    Preparing,
    /// <summary>Kargoya verildi.</summary>
    Shipped,
    /// <summary>Teslim edildi.</summary>
    Delivered,
    /// <summary>Iptal edildi; ayrilan stok iade edildi.</summary>
    Cancelled,
}

/// <summary>
/// Siparisin odeme durumu. Yalnizca dogrulanmis bir odeme sonucu (saglayici bildirimi veya yetkili admin) "Paid" yapabilir.
/// </summary>
public enum StorePaymentStatus
{
    /// <summary>Odeme bekleniyor (kapida odeme, havale veya saglayici sonucu gelmedi).</summary>
    Pending,
    /// <summary>Odeme dogrulandi.</summary>
    Paid,
    /// <summary>Odeme basarisiz; siparis iptal edilir ve stok iade edilir.</summary>
    Failed,
    /// <summary>Odenmis siparis iptal edildi; para iadesi bekleniyor.</summary>
    RefundPending,
    /// <summary>Para iadesi yapildi.</summary>
    Refunded,
    /// <summary>Odeme alinmadan siparis iptal edildi.</summary>
    Cancelled,
}

/// <summary>Musterinin sectigi odeme yontemi.</summary>
public enum StorePaymentMethod
{
    /// <summary>Kapida odeme.</summary>
    CashOnDelivery,
    /// <summary>Havale / EFT.</summary>
    BankTransfer,
    /// <summary>Kartla online odeme (bir odeme saglayicisi yapilandirilmadan kullanilamaz).</summary>
    Card,
}
