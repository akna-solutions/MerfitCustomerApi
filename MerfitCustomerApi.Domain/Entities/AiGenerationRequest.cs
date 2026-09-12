using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// AiGenerationRequest varligini temsil eder.
/// </summary>
public class AiGenerationRequest : BaseEntity
{
    /// <summary>
    /// Istegi olusturan kullanicinin kimligi.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Istegin turu.
    /// </summary>
    public AiRequestType Type { get; set; }

    /// <summary>
    /// Yapay zekaya gonderilen istem (prompt) metni.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Istegin guncel durumu.
    /// </summary>
    public AiGenerationStatus Status { get; set; }

    /// <summary>
    /// Istek icin kullanilan yapay zeka modeli.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Uretimin baslama tarihi.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Uretimin tamamlanma tarihi.
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}
