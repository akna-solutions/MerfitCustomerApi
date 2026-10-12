namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Saglayiciya gonderilecek tek bir push mesaji.</summary>
public sealed record PushMessage(string To, string Title, string Body, IReadOnlyDictionary<string, string> Data);

public enum PushSendOutcome
{
    /// <summary>Saglayici kabul etti. Cihaza teslim garanti DEGILDIR.</summary>
    Accepted,
    /// <summary>Kalici red; yeniden denenmez.</summary>
    Rejected,
    /// <summary>Gecici hata; yeniden denenebilir.</summary>
    Transient,
}

/// <summary>Bir mesaj icin saglayici sonucu. Liste, gonderilen mesajlarla ayni siradadir.</summary>
public sealed record PushSendResult(
    PushSendOutcome Outcome,
    string? ProviderMessageId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    /// <summary>Token artik gecerli degil; cihaz pasiflestirilmeli.</summary>
    public bool IsDeviceUnregistered => ErrorCode == PushErrorCodes.DeviceNotRegistered;
}

/// <summary>Saglayici makbuzu.</summary>
public sealed record PushReceiptResult(bool IsOk, string? ErrorCode = null, string? ErrorMessage = null)
{
    public bool IsDeviceUnregistered => ErrorCode == PushErrorCodes.DeviceNotRegistered;
}

public static class PushErrorCodes
{
    public const string DeviceNotRegistered = "DeviceNotRegistered";
    public const string InvalidToken = "InvalidToken";
    public const string Network = "NetworkError";
}

/// <summary>
/// Push saglayicisi soyutlamasi. Ilk uygulama Expo Push Service'tir (ExpoPushNotificationService);
/// FCM/APNs dogrudan kullanilmak istenirse yalnizca bu arayuzun yeni bir uygulamasi eklenir.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>Token bu saglayicinin bicimine uygun mu (orn. ExponentPushToken[...]).</summary>
    bool IsValidToken(string token);

    Task<IReadOnlyList<PushSendResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Kabul edilmis mesajlarin makbuzlari; saglayicinin henuz hazir olmayan makbuzlari sonucta yer almaz.</summary>
    Task<IReadOnlyDictionary<string, PushReceiptResult>> GetReceiptsAsync(IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken = default);
}
