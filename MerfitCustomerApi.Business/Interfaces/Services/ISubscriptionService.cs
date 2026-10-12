using MerfitCustomerApi.Business.Dtos.Customer.Subscriptions;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Kullanicinin Plus aboneligini okuyan servis sozlesmesi.</summary>
public interface ISubscriptionService
{
    Task<MySubscriptionResponse> GetMySubscriptionAsync(long userId, CancellationToken cancellationToken = default);
}
