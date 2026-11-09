namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Antrenman Laboratuvari simulasyon kuyrugunu isler (bkz. WorkoutPlanSimulationProcessor). Kullanici verisine
/// dokunmaz; yalnizca WorkoutPlanSimulation satirini gunceller.
/// </summary>
public interface IWorkoutPlanSimulationProcessor
{
    /// <summary>Bekleyen bir simulasyonu isler; islenecek kayit yoksa false.</summary>
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
}
