namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json "Store" bolumu: MB Shop kargo kurali, adet sinirlari, odeme yontemleri ve hiz sinirlari.
/// Tutarlarin tek kaynagi sunucudur; mobil uygulama bu degerleri GET /api/store/checkout/options ile okur.
/// Bolum atlanirsa asagidaki varsayilanlar kullanilir. Banka hesabi gibi bilgiler production'da ortam degiskeni ile
/// verilir (Store__BankTransfer__Iban ...); IBAN tanimli degilse havale secenegi kapali gorunur.
/// </summary>
public class StoreOptions
{
    public const string SectionName = "Store";

    public string Currency { get; set; } = "TRY";

    /// <summary>Indirimli ara toplam bu tutara esit veya ustundeyse kargo ucretsizdir.</summary>
    public decimal FreeShippingThreshold { get; set; } = 500m;

    /// <summary>Esigin altindaki siparislerin sabit kargo bedeli.</summary>
    public decimal ShippingCost { get; set; } = 49.90m;

    /// <summary>Bir sepet satirinda en fazla adet.</summary>
    public int MaxQuantityPerLine { get; set; } = 10;

    /// <summary>Sepetteki en fazla farkli satir.</summary>
    public int MaxCartLines { get; set; } = 30;

    /// <summary>Kullanici basina en fazla kayitli adres.</summary>
    public int MaxAddressesPerUser { get; set; } = 10;

    /// <summary>Kapida odeme acik mi.</summary>
    public bool CashOnDeliveryEnabled { get; set; } = true;

    public BankTransferOptions BankTransfer { get; set; } = new();

    /// <summary>POST /api/store/orders icin kullanici basina dakikalik istek siniri.</summary>
    public int OrderRequestsPerMinute { get; set; } = 10;

    /// <summary>Sepet, favori ve adres yazma uclari icin kullanici basina dakikalik istek siniri.</summary>
    public int WriteRequestsPerMinute { get; set; } = 120;
}

/// <summary>Havale/EFT bilgileri. Iban bos ise havale secenegi sunulmaz.</summary>
public class BankTransferOptions
{
    public string? Iban { get; set; }

    public string? AccountName { get; set; }

    public string? BankName { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Iban) && !string.IsNullOrWhiteSpace(AccountName);
}
