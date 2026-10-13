using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Beslenme asistani sohbetindeki tek bir mesaj. Kullanici mesaji ve asistan yaniti, model cagrisi
/// basarili olduktan sonra birlikte kaydedilir; yarim (yanitsiz) kullanici mesaji birakilmaz.
/// </summary>
public class NutritionAssistantMessage : BaseEntity
{
    public long ConversationId { get; set; }

    public NutritionAssistantMessageRole Role { get; set; }

    /// <summary>Duz metin icerik (HTML/Markdown olarak yorumlanmaz).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Asistan mesajinda modelin okudugu girdi token sayisi (maliyet takibi).</summary>
    public int? InputTokens { get; set; }

    /// <summary>Asistan mesajinda modelin urettigi cikti token sayisi (maliyet takibi).</summary>
    public int? OutputTokens { get; set; }
}
