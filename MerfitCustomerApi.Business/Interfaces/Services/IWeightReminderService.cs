using MerfitCustomerApi.Business.Dtos.Customer.Progress;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Yaklasik iki gunde bir gosterilen kilo guncelleme hatirlatmasinin zamanlamasi.</summary>
public interface IWeightReminderService
{
    Task<CustomerWeightReminderResponse> GetAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Hatirlatmayi erteler ("later") ya da bir sonraki araliga kadar kapatir ("dismiss").</summary>
    Task<CustomerWeightReminderResponse> SnoozeAsync(long userId, SnoozeWeightReminderRequest request, CancellationToken cancellationToken = default);
}
