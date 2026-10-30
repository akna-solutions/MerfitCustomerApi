namespace MerfitCustomerApi.Business.Services.ContentModeration;

/// <summary><see cref="Interfaces.Services.IContentModerationService.Check"/> sonucu.</summary>
public sealed class ModerationResult
{
    public static readonly ModerationResult Allowed = new() { IsAllowed = true };

    public bool IsAllowed { get; init; }

    /// <summary>Kullaniciya gosterilecek, alani ve nedeni belirten mesaj; izinliyse null.</summary>
    public string? RejectionReason { get; init; }

    /// <summary>Tespit edilen kategori; izinliyse null.</summary>
    public ModerationCategory? DetectedCategory { get; init; }

    /// <summary>
    /// Eslesen sozluk ifadesi (normallestirilmis bicimde). Yalnizca loglama/inceleme icindir; kullaniciya donulmez.
    /// </summary>
    public string? MatchedTerm { get; init; }

    public static ModerationResult Rejected(string reason, ModerationCategory category, string? matchedTerm = null) => new()
    {
        IsAllowed = false,
        RejectionReason = reason,
        DetectedCategory = category,
        MatchedTerm = matchedTerm,
    };
}
