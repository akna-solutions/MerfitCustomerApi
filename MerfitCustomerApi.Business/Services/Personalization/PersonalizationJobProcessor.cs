using System.Diagnostics;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Personalization;

/// <summary>
/// IPersonalizationJobProcessor'in varsayilan implementasyonu.
///
/// Concurrency (eslzamanlilik) stratejisi: PersonalizationJob.Status/AttemptCount/StartedAt
/// guncellemesi, PostgreSQL'in "xmin" sistem kolonu EF Core optimistic concurrency token'i
/// olarak kullanilarak claim edilir (bkz. AppDbContext.OnModelCreating). Iki worker (ayni
/// instance'ta veya farkli instance'larda) ayni anda ayni job'u Pending -> Processing yapmaya
/// calisirsa, ikincinin SaveChangesAsync cagrisi DbUpdateConcurrencyException ile basarisiz
/// olur; bu, "iki worker ayni job'u islemesin" gereksinimini ek bir kilitleme mekanizmasi
/// (SELECT FOR UPDATE, dagitik lock vb.) gerektirmeden, tamamen EF Core + PostgreSQL ile karsilar.
///
/// Retry stratejisi: bir generation/persist hatasi olustugunda, AttemptCount (claim sirasinda
/// zaten +1 artirilmistir) MaxAttemptCount'un altindaysa job Pending'e donup bir sonraki
/// worker turunde tekrar denenir; degilse kalici olarak Failed'de kalir (sonsuz retry yok).
/// </summary>
public class PersonalizationJobProcessor : IPersonalizationJobProcessor
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkoutPlanGenerator _workoutPlanGenerator;
    private readonly INutritionPlanGenerator _nutritionPlanGenerator;
    private readonly PersonalizationJobProcessingOptions _options;
    private readonly ILogger<PersonalizationJobProcessor> _logger;

    public PersonalizationJobProcessor(
        IUnitOfWork unitOfWork,
        IWorkoutPlanGenerator workoutPlanGenerator,
        INutritionPlanGenerator nutritionPlanGenerator,
        IOptions<PersonalizationJobProcessingOptions> options,
        ILogger<PersonalizationJobProcessor> logger)
    {
        _unitOfWork = unitOfWork;
        _workoutPlanGenerator = workoutPlanGenerator;
        _nutritionPlanGenerator = nutritionPlanGenerator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var claimed = await TryClaimNextPendingJobAsync(cancellationToken);
        if (claimed is null)
        {
            return false;
        }

        var (jobId, userId) = claimed.Value;
        var totalStopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Personalization job {JobId} started for user {UserId}", jobId, userId);

        try
        {
            var workoutStopwatch = Stopwatch.StartNew();
            var workoutResult = await _workoutPlanGenerator.GenerateAsync(userId, cancellationToken);
            workoutStopwatch.Stop();
            _logger.LogInformation(
                "Workout plan generated for job {JobId} in {ElapsedMs}ms", jobId, workoutStopwatch.ElapsedMilliseconds);

            var nutritionStopwatch = Stopwatch.StartNew();
            var nutritionResult = await _nutritionPlanGenerator.GenerateAsync(userId, cancellationToken);
            nutritionStopwatch.Stop();
            _logger.LogInformation(
                "Nutrition plan generated for job {JobId} in {ElapsedMs}ms", jobId, nutritionStopwatch.ElapsedMilliseconds);

            await PersistPlansAndCompleteJobAsync(userId, jobId, workoutResult, nutritionResult, cancellationToken);

            totalStopwatch.Stop();
            _logger.LogInformation(
                "Personalization job {JobId} completed in {ElapsedMs}ms", jobId, totalStopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Transaction basladiysa geri al; baslamadiysa (hata generator asamasinda olustuysa) no-op'tur.
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);

            // Rollback edilen transaction icinde Added olarak izlenmeye baslanmis (ama hicbir zaman
            // kalici olmamis) entity'lerin bir sonraki SaveChanges'te sehven tekrar eklenmesini
            // onlemek icin degisiklik izleyicisini temizle.
            _unitOfWork.ClearTracking();

            await FailOrRetryJobAsync(jobId, ex, cancellationToken);

            // Stack trace veya istisna detaylari LOGLANMAZ (yalnizca kisa mesaj); sifre/token gibi
            // hassas veriler zaten bu istisna mesajlarinin icinde yer almaz.
            _logger.LogWarning("Personalization job {JobId} failed: {Message}", jobId, ex.Message);
        }

        return true;
    }

    /// <summary>
    /// Kuyruktaki en eski uygun (Pending, deneme siniri asilmamis) adaylardan ilkini, optimistic
    /// concurrency ile atomik olarak claim eder. Bir baska worker/instance ayni satiri once
    /// claim ettiyse DbUpdateConcurrencyException yakalanir ve bir sonraki adaya gecilir.
    /// </summary>
    private async Task<(long JobId, long UserId)?> TryClaimNextPendingJobAsync(CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<PersonalizationJob>();

        var candidateIds = await repo.GetQueryable()
            .Where(j => j.Status == PersonalizationJobStatus.Pending && j.AttemptCount < _options.MaxAttemptCount)
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .Take(_options.ClaimBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var candidateId in candidateIds)
        {
            var job = await repo.GetQueryable(asNoTracking: false)
                .FirstOrDefaultAsync(j => j.Id == candidateId, cancellationToken);

            if (job is null || job.Status != PersonalizationJobStatus.Pending)
            {
                // Baska bir worker bu satiri claim edene kadar biz de gormus olabiliriz; atla.
                continue;
            }

            job.Status = PersonalizationJobStatus.Processing;
            job.StartedAt = DateTime.UtcNow;
            job.AttemptCount += 1;
            job.UpdatedAt = DateTime.UtcNow;
            repo.Update(job);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return (job.Id, job.UserId);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Baska bir worker ayni anda ayni job'u claim etti (xmin uyusmadi) - bu adayi
                // birakip siradaki adaya gec.
                _unitOfWork.ClearTracking();
            }
        }

        return null;
    }

    /// <summary>
    /// Task item #8'deki akisi birebir uygular: tek bir transaction icinde eski aktif planlari
    /// pasiflestirir, yeni WorkoutPlan/NutritionPlan graph'ini kaydeder ve job'u Completed
    /// yapar; herhangi bir adimda hata olursa cagiran taraf (ProcessNextAsync) transaction'i
    /// geri alir.
    /// </summary>
    private async Task PersistPlansAndCompleteJobAsync(
        long userId,
        long jobId,
        WorkoutPlanGenerationResult workout,
        NutritionPlanGenerationResult nutrition,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        await DeactivateExistingPlansAsync<WorkoutPlan>(userId, cancellationToken);
        await PersistWorkoutPlanAsync(workout, cancellationToken);

        await DeactivateExistingPlansAsync<NutritionPlan>(userId, cancellationToken);
        await PersistNutritionPlanAsync(nutrition, cancellationToken);

        var job = await _unitOfWork.Repository<PersonalizationJob>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is not null)
        {
            job.Status = PersonalizationJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = null;
            job.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Repository<PersonalizationJob>().Update(job);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await _unitOfWork.CommitTransactionAsync(cancellationToken);
    }

    /// <summary>UserId'ye ait aktif planlari pasiflestirir (kullanici basina tek aktif plan kurali).</summary>
    private async Task DeactivateExistingPlansAsync<TPlan>(long userId, CancellationToken cancellationToken)
        where TPlan : class
    {
        // WorkoutPlan ve NutritionPlan'in her ikisi de UserId/IsActive/EndDate/UpdatedAt alanlarina
        // sahip oldugundan (ancak ortak bir interface/temel sinifi olmadigindan) burada iki somut
        // overload'a ayiriyoruz; generic constraint yalnizca cagiran tarafta tekrarı azaltmak icindir.
        if (typeof(TPlan) == typeof(WorkoutPlan))
        {
            var existing = await _unitOfWork.Repository<WorkoutPlan>()
                .GetQueryable(asNoTracking: false)
                .Where(p => p.UserId == userId && p.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var plan in existing)
            {
                plan.IsActive = false;
                plan.EndDate = DateTime.UtcNow;
                plan.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.Repository<WorkoutPlan>().Update(plan);
            }

            if (existing.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        else if (typeof(TPlan) == typeof(NutritionPlan))
        {
            var existing = await _unitOfWork.Repository<NutritionPlan>()
                .GetQueryable(asNoTracking: false)
                .Where(p => p.UserId == userId && p.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var plan in existing)
            {
                plan.IsActive = false;
                plan.EndDate = DateTime.UtcNow;
                plan.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.Repository<NutritionPlan>().Update(plan);
            }

            if (existing.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task PersistWorkoutPlanAsync(WorkoutPlanGenerationResult result, CancellationToken cancellationToken)
    {
        await _unitOfWork.Repository<WorkoutPlan>().AddAsync(result.Plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken); // result.Plan.Id atanir

        foreach (var dayResult in result.Days)
        {
            dayResult.Day.WorkoutPlanId = result.Plan.Id;
            await _unitOfWork.Repository<WorkoutPlanDay>().AddAsync(dayResult.Day, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken); // her Day.Id atanir

        var allExercises = new List<WorkoutPlanExercise>();
        foreach (var dayResult in result.Days)
        {
            foreach (var exercise in dayResult.Exercises)
            {
                exercise.WorkoutPlanDayId = dayResult.Day.Id;
                allExercises.Add(exercise);
            }
        }

        if (allExercises.Count > 0)
        {
            await _unitOfWork.Repository<WorkoutPlanExercise>().AddRangeAsync(allExercises, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task PersistNutritionPlanAsync(NutritionPlanGenerationResult result, CancellationToken cancellationToken)
    {
        await _unitOfWork.Repository<NutritionPlan>().AddAsync(result.Plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken); // result.Plan.Id atanir

        foreach (var dayResult in result.Days)
        {
            dayResult.Day.NutritionPlanId = result.Plan.Id;
            await _unitOfWork.Repository<NutritionPlanDay>().AddAsync(dayResult.Day, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken); // her Day.Id atanir

        var allMeals = new List<NutritionPlanMeal>();
        var itemsByMeal = new List<(NutritionPlanMeal Meal, List<NutritionPlanMealItem> Items)>();
        foreach (var dayResult in result.Days)
        {
            foreach (var mealResult in dayResult.Meals)
            {
                mealResult.Meal.NutritionPlanDayId = dayResult.Day.Id;
                allMeals.Add(mealResult.Meal);
                itemsByMeal.Add((mealResult.Meal, mealResult.Items));
            }
        }

        if (allMeals.Count > 0)
        {
            await _unitOfWork.Repository<NutritionPlanMeal>().AddRangeAsync(allMeals, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken); // her Meal.Id atanir
        }

        var allItems = new List<NutritionPlanMealItem>();
        foreach (var (meal, items) in itemsByMeal)
        {
            foreach (var item in items)
            {
                item.NutritionPlanMealId = meal.Id;
                allItems.Add(item);
            }
        }

        if (allItems.Count > 0)
        {
            await _unitOfWork.Repository<NutritionPlanMealItem>().AddRangeAsync(allItems, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Deneme sinirinin altindaysa job'u tekrar Pending yapar (bir sonraki turda tekrar denenir);
    /// sinira ulasildiysa kalici olarak Failed'de birakir. Her iki durumda da ErrorMessage
    /// (kisa, stack trace icermeyen) guncellenir.
    /// </summary>
    private async Task FailOrRetryJobAsync(long jobId, Exception ex, CancellationToken cancellationToken)
    {
        var job = await _unitOfWork.Repository<PersonalizationJob>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.ErrorMessage = TruncateErrorMessage(ex.Message);
        job.UpdatedAt = DateTime.UtcNow;

        if (job.AttemptCount < _options.MaxAttemptCount)
        {
            job.Status = PersonalizationJobStatus.Pending;
            job.CompletedAt = null;
        }
        else
        {
            job.Status = PersonalizationJobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
        }

        _unitOfWork.Repository<PersonalizationJob>().Update(job);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string TruncateErrorMessage(string? message)
    {
        const int maxLength = 2000; // PersonalizationJobConfiguration.ErrorMessage.HasMaxLength(2000) ile tutarli
        if (string.IsNullOrEmpty(message))
        {
            return "Bilinmeyen bir hata olustu.";
        }

        return message.Length <= maxLength ? message : message[..maxLength];
    }
}
