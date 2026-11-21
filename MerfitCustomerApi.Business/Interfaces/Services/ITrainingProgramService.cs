using MerfitCustomerApi.Business.Dtos.Customer.Training;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kisisellestirilmis antrenman motoru: program olusturma, versiyonlama, oneri / onay, elle degisiklik,
/// gelisim analizi ve toparlanma. Kararlar deterministik kurallarla verilir.
/// </summary>
public interface ITrainingProgramService
{
    Task<TrainingPreferencesDto> GetPreferencesAsync(long userId, CancellationToken cancellationToken = default);

    Task<TrainingPreferencesDto> UpdatePreferencesAsync(long userId, TrainingPreferencesDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Program olusturur. Aktif motor programi yoksa V1 olarak hemen aktif olur (varsa eski katalog plani arsivlenir);
    /// aktif motor programi varsa yeni versiyon "onerilen" olarak kaydedilir ve kullanici onayi bekler.
    /// </summary>
    Task<TrainingProgramDto> GenerateAsync(long userId, GenerateTrainingProgramRequest request, CancellationToken cancellationToken = default);

    Task<TrainingProgramDto?> GetActiveAsync(long userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingProgramSummaryDto>> GetVersionsAsync(long userId, CancellationToken cancellationToken = default);

    Task<TrainingProgramDto> GetProgramAsync(long userId, long programId, CancellationToken cancellationToken = default);

    /// <summary>Guncelleme gerekli mi (blok bitti, toparlanma sinyali, agri)? Yazma yapmaz.</summary>
    Task<TrainingUpdateRecommendationDto> GetUpdateRecommendationAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Gecmis performans ve toparlanmaya gore yeni versiyonu "onerilen" olarak olusturur. Aktif programi degistirmez.</summary>
    Task<TrainingProgramDto> ProposeUpdateAsync(long userId, CancellationToken cancellationToken = default);

    Task<TrainingProgramDto> AcceptProposalAsync(long userId, long programId, CancellationToken cancellationToken = default);

    Task RejectProposalAsync(long userId, long programId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingAlternativeDto>> GetAlternativesAsync(long userId, long planExerciseId, CancellationToken cancellationToken = default);

    /// <summary>Kullanicinin elle egzersiz degistirmesi: yeni versiyon (Manual) olusur, eski versiyon arsivlenir.</summary>
    Task<TrainingProgramDto> SwapExerciseAsync(long userId, long planExerciseId, SwapExerciseRequest request, CancellationToken cancellationToken = default);

    Task<TrainingProgressDto> GetProgressAsync(long userId, CancellationToken cancellationToken = default);

    Task<TrainingExerciseProgressDto?> GetExerciseHistoryAsync(long userId, long exerciseId, CancellationToken cancellationToken = default);

    Task<TrainingRecoveryDto> GetRecoveryAsync(long userId, CancellationToken cancellationToken = default);
}
