using System.Text.Json;
using System.Text.Json.Nodes;

namespace MerfitCustomerApi.Business.Services.Notifications.Engine;

/// <summary>
/// Bildirime dokunuldugunda mobil uygulamanin acacagi ekran. Yalnizca izin verilen ekran adlari ve
/// basit (string/sayi) parametreler kabul edilir; mobil uygulama da ayni listeyi dogrular. Liste
/// MerfitApi'deki admin dogrulamasi ile aynidir.
/// </summary>
public static class NotificationNavigation
{
    public const string ScreenKey = "screen";

    public static readonly IReadOnlySet<string> AllowedScreens = new HashSet<string>(StringComparer.Ordinal)
    {
        "dashboard", "nutrition", "workouts", "workoutPlan", "progress", "leaderboard", "notifications", "profile", "subscription",
        // MB Shop ekranlari (storeOrder: orderId, storeProduct: productId parametresi alir).
        "store", "storeOrders", "storeOrder", "storeProduct", "storeNotifications",
    };

    /// <summary>MB Shop'a ait ekranlar; MB Shop bildirimleri yalnizca bunlara, MB Fit bildirimleri yalnizca digerlerine yonlendirebilir.</summary>
    public static readonly IReadOnlySet<string> ShopScreens = new HashSet<string>(StringComparer.Ordinal)
    {
        "store", "storeOrders", "storeOrder", "storeProduct", "storeNotifications",
    };

    private const int MaxParams = 5;
    private const int MaxValueLength = 100;

    /// <summary>
    /// Gecerli ise normalize edilmis JSON'u, null/bos girdi icin null'u dondurur; gecersiz ise
    /// error doldurulur.
    /// </summary>
    public static string? Normalize(string? dataJson, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        JsonObject? obj;
        try
        {
            obj = JsonNode.Parse(dataJson) as JsonObject;
        }
        catch (JsonException)
        {
            obj = null;
        }

        if (obj is null)
        {
            error = "Yönlendirme verisi geçerli bir JSON nesnesi olmalı.";
            return null;
        }

        var result = new JsonObject();
        foreach (var (key, node) in obj)
        {
            if (node is not JsonValue value)
            {
                error = $"'{key}' alanı yalnızca metin veya sayı olabilir.";
                return null;
            }

            var text = value.TryGetValue<string>(out var s) ? s : value.ToJsonString();
            if (text.Length > MaxValueLength)
            {
                error = $"'{key}' alanı en fazla {MaxValueLength} karakter olabilir.";
                return null;
            }

            result[key] = text;
        }

        if (!result.TryGetPropertyValue(ScreenKey, out var screen) || screen is null || !AllowedScreens.Contains(screen.GetValue<string>()))
        {
            error = $"'screen' alanı şunlardan biri olmalı: {string.Join(", ", AllowedScreens)}.";
            return null;
        }

        if (result.Count > MaxParams + 1)
        {
            error = $"En fazla {MaxParams} ek parametre gönderilebilir.";
            return null;
        }

        return result.ToJsonString();
    }

    public static string Screen(string screen, params (string Key, string Value)[] extra)
    {
        var obj = new JsonObject { [ScreenKey] = screen };
        foreach (var (key, value) in extra)
        {
            obj[key] = value;
        }
        return obj.ToJsonString();
    }

    /// <summary>Kayitli veriyi push "data" alani ve uygulama ici liste icin sozluge cevirir; bozuk veri yok sayilir.</summary>
    public static Dictionary<string, string> ToDictionary(string? dataJson)
    {
        var normalized = Normalize(dataJson, out _);
        if (normalized is null)
        {
            return new Dictionary<string, string>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(normalized) ?? new Dictionary<string, string>();
    }
}
