namespace MerfitCustomerApi.Business.Services.ContentModeration;

/// <summary>Reddedilen icerigin kategorisi; <see cref="ModerationLexicon"/>'daki her ifade birine aittir.</summary>
public enum ModerationCategory
{
    /// <summary>Kufur.</summary>
    Profanity,

    /// <summary>Hakaret.</summary>
    Insult,

    /// <summary>Cinsel icerikli ifade.</summary>
    Sexual,

    /// <summary>Tehdit veya asagilayici ifade.</summary>
    Threat,

    /// <summary>Irkci veya ayrimci ifade.</summary>
    HateSpeech,

    /// <summary>Kullanici adi olarak kullanilmamasi gereken ifade (orn. "hitler", "porn").</summary>
    InappropriateUsername,

    /// <summary>Sistem/resmi hesap taklidi (orn. "admin", "mbfit_support").</summary>
    ReservedName,

    /// <summary>Kontrol edilemeyecek kadar uzun metin.</summary>
    TooLong,
}
