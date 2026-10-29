using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kartla online odeme saglayicisi icin genisletme noktasi (orn. iyzico, PayTR, Stripe). Su an HICBIR saglayici kayitli
/// degildir; kayitli saglayici yoksa "card" yontemi GET /api/store/checkout/options'ta kapali gorunur ve siparis kabul
/// edilmez. Saglayici eklenirken:
/// 1) Bu arayuzu uygulayan sinif DI'a eklenir; gizli anahtarlar yalnizca sunucu ortam degiskenlerinden okunur.
/// 2) CreatePaymentAsync siparis olustuktan sonra (stok ayrilmis, PaymentStatus=Pending) odeme oturumu baslatir.
/// 3) Saglayicinin callback/webhook'u imza dogrulamasi VerifyCallbackAsync ile yapildiktan sonra
///    IStorePaymentResultService.ApplyAsync'e iletilir. Dogrulanmamis bir bildirim siparisi "Paid" yapamaz.
/// </summary>
public interface IStorePaymentProvider
{
    /// <summary>Saglayici adi (StoreOrder.PaymentProvider'a yazilir).</summary>
    string Name { get; }

    /// <summary>Odeme oturumu baslatir ve kullanicinin yonlendirilecegi adresi dondurur.</summary>
    Task<StorePaymentSession> CreatePaymentAsync(StoreOrder order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saglayici bildiriminin imzasini/dogrulugunu kontrol eder. Gecersiz veya dogrulanamayan bildirimde null doner.
    /// </summary>
    Task<StorePaymentResult?> VerifyCallbackAsync(IReadOnlyDictionary<string, string> headers, string body, CancellationToken cancellationToken = default);
}

public sealed record StorePaymentSession(string PaymentReference, string RedirectUrl);

/// <summary>Dogrulanmis odeme sonucu.</summary>
public sealed record StorePaymentResult(string OrderNumber, bool Succeeded, string Provider, string PaymentReference, string? FailureReason = null);

/// <summary>Dogrulanmis odeme sonuclarini siparise uygular (odendi / basarisiz + stok iadesi).</summary>
public interface IStorePaymentResultService
{
    /// <summary>
    /// Idempotent: ayni sonuc tekrar gelirse siparis degismez. Basarisiz odemede siparis (henuz kargolanmadiysa)
    /// iptal edilir ve ayrilan stok iade edilir.
    /// </summary>
    Task ApplyAsync(StorePaymentResult result, CancellationToken cancellationToken = default);
}
