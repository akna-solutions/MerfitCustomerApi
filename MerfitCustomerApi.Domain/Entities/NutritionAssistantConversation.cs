using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Kullanicinin bir gune ait beslenme asistani sohbeti. Kullanici ve gun basina tek sohbet vardir
/// (benzersiz index); gun degisince yeni sohbet baslar, boylece onceki gunun tuketimi bugunun gibi
/// degerlendirilmez. Beslenme verisinin kendisi burada saklanmaz; her istekte o gunun guncel verisi
/// yeniden okunur.
/// </summary>
public class NutritionAssistantConversation : BaseEntity
{
    /// <summary>Sohbetin sahibi (JWT'deki kullanici; istemciden alinmaz).</summary>
    public long UserId { get; set; }

    /// <summary>Sohbetin ait oldugu beslenme gunu (mobilin /api/nutrition?date= ile kullandigi gun).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Son mesajin zamani (saklama suresi bu alana gore uygulanir).</summary>
    public DateTime LastMessageAt { get; set; }
}
