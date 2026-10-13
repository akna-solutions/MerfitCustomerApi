using MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Beslenme ekranindaki sohbet penceresinin (Mbfit Beslenme Asistani) is kurallari.</summary>
public interface INutritionAssistantService
{
    /// <summary>
    /// Verilen gunun sohbetini (varsa), gercek verilerle hazirlanan acilis degerlendirmesini ve hizli
    /// sorulari dondurur. Sohbet yoksa olusturmaz.
    /// </summary>
    Task<NutritionAssistantConversationDto> GetConversationAsync(long userId, DateOnly? date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kullanici mesajini o gunun guncel beslenme baglamiyla modele iletir; basarili yanitta iki mesaji
    /// birlikte kaydeder.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Sohbet bu kullaniciya ait degilse.</exception>
    /// <exception cref="Domain.Exceptions.TooManyRequestsException">Kullanim siniri asildiysa.</exception>
    /// <exception cref="Domain.Exceptions.ExternalServiceUnavailableException">Saglayici yanit vermezse.</exception>
    Task<NutritionAssistantChatResponse> SendMessageAsync(long userId, NutritionAssistantChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>Kullanicinin verilen gundeki sohbetini ve mesajlarini siler. Sohbet yoksa false.</summary>
    Task<bool> DeleteConversationAsync(long userId, DateOnly? date, CancellationToken cancellationToken = default);
}
