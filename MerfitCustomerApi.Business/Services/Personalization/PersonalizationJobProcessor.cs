using System.Diagnostics;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
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
/// Kullanici basina siralama: ayni kullanicinin islenen (Processing) bir isi varken diger isleri claim
/// edilmez; iki worker ayni anda ayni kullanicinin iki farkli isini claim ederse kimligi kucuk olan devam
/// eder, digeri Pending'e geri birakilir. Ek olarak veritabanindaki kismi benzersiz index
/// (UX_NutritionPlan_UserId_Active) kullanici basina birden fazla aktif beslenme planini reddeder.
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

        var (jobId, userId, nutritionOnly) = claimed.Value;
        var totalStopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Personalization job {JobId} started for user {UserId}", jobId, userId);

        try
        {
            // Yalnizca beslenmeyi etkileyen profil degisikliklerinde (kilo, boy, alerji...) antrenman
            // programi yeniden uretilmez.
            WorkoutPlanGenerationResult? workoutResult = null;
            if (!nutritionOnly)
            {
                var workoutStopwatch = Stopwatch.StartNew();
                workoutResult = await _workoutPlanGenerator.GenerateAsync(userId, cancellationToken);
                workoutStopwatch.Stop();
                _logger.LogInformation(
                    "Workout plan generated for job {JobId} in {ElapsedMs}ms", jobId, workoutStopwatch.ElapsedMilliseconds);
            }

            // Beslenme programi uretilemezse (orn. besin katalogu bos) antrenman programi yine de
            // kaydedilir; kullanicinin mevcut beslenme programi (varsa) aktif kalir. Boylece profilde
            // hedef/gun degisikligi beslenme verisine takilmadan antrenman programina yansir.
            NutritionPlanGenerationResult? nutritionResult = null;
            string? nutritionError = null;
            var nutritionStopwatch = Stopwatch.StartNew();
            try
            {
                nutritionResult = await _nutritionPlanGenerator.GenerateAsync(userId, cancellationToken);
                _logger.LogInformation(
                    "Nutrition plan generated for job {JobId} in {ElapsedMs}ms", jobId, nutritionStopwatch.ElapsedMilliseconds);
            }
            catch (PersonalizationGenerationException ex)
            {
                nutritionError = ex.Message;
                _logger.LogWarning(
                    "Nutrition plan skipped for job {JobId}; workout plan will still be saved: {Message}", jobId, ex.Message);
            }

            await PersistPlansAndCompleteJobAsync(userId, jobId, workoutResult, nutritionResult, nutritionError, cancellationToken);

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
    private async Task<(long JobId, long UserId, bool NutritionOnly)?> TryClaimNextPendingJobAsync(CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<PersonalizationJob>();
        var lockCutoff = DateTime.UtcNow - _options.UserProcessingLockTimeout;

        // Ayni kullanicinin islenmekte olan bir isi varsa o kullanicinin diger isleri bu turda atlanir.
        var allJobs = repo.GetQueryable();
        var candidateIds = await repo.GetQueryable()
            .Where(j => j.Status == PersonalizationJobStatus.Pending && j.AttemptCount < _options.MaxAttemptCount)
            .Where(j => !allJobs.Any(other => other.UserId == j.UserId
                && other.Status == PersonalizationJobStatus.Processing
                && other.StartedAt > lockCutoff))
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
            }
            catch (DbUpdateConcurrencyException)
            {
                // Baska bir worker ayni anda ayni job'u claim etti (xmin uyusmadi) - bu adayi
                // birakip siradaki adaya gec.
                _unitOfWork.ClearTracking();
                continue;
            }

            if (await YieldToEarlierProcessingJobAsync(job, lockCutoff, cancellationToken))
            {
                continue;
            }

            return (job.Id, job.UserId, job.NutritionOnly);
        }

        return null;
    }

    /// <summary>
    /// Iki worker ayni kullanicinin farkli islerini ayni anda claim ettiyse yalnizca kimligi en kucuk
    /// olan devam eder; bu is Pending'e (deneme hakki geri verilerek) birakilir ve true doner.
    /// Kimligi en kucuk olan hicbir zaman geri cekilmedigi icin iki is birbirini sonsuza dek bekletmez.
    /// </summary>
    private async Task<bool> YieldToEarlierProcessingJobAsync(PersonalizationJob job, DateTime lockCutoff, CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<PersonalizationJob>();
        var earlierProcessing = await repo.GetQueryable()
            .AnyAsync(other => other.UserId == job.UserId
                && other.Id < job.Id
                && other.Status == PersonalizationJobStatus.Processing
                && other.StartedAt > lockCutoff,
                cancellationToken);
        if (!earlierProcessing)
        {
            return false;
        }

        job.Status = PersonalizationJobStatus.Pending;
        job.StartedAt = null;
        job.AttemptCount -= 1;
        job.UpdatedAt = DateTime.UtcNow;
        repo.Update(job);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Satir bu arada degistiyse (cok olasi degil) bir sonraki turda yeniden degerlendirilir.
        }

        _unitOfWork.ClearTracking();
        _logger.LogInformation("Personalization job {JobId} deferred: another job of the same user is processing", job.Id);
        return true;
    }

    /// <summary>
    /// Task item #8'deki akisi birebir uygular: tek bir transaction icinde eski aktif planlari
    /// pasiflestirir, yeni WorkoutPlan/NutritionPlan graph'ini kaydeder (beslenme uretilemediyse
    /// yalnizca WorkoutPlan; mevcut beslenme programi korunur) ve job'u Completed yapar; herhangi bir adimda hata olursa cagiran taraf (ProcessNextAsync) transaction'i
    /// geri alir.
    /// </summary>
    private async Task PersistPlansAndCompleteJobAsync(
        long userId,
        long jobId,
        WorkoutPlanGenerationResult? workout,
        NutritionPlanGenerationResult? nutrition,
        string? nutritionError,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        if (workout is not null)
        {
            await DeactivateExistingPlansAsync<WorkoutPlan>(userId, cancellationToken);
            await PersistWorkoutPlanAsync(workout, cancellationToken);
        }

        if (nutrition is not null)
        {
            // Onceki plan silinmez, pasiflestirilir (gecmis ve ogun kayitlari korunur); yeni plan ve
            // dayandigi hedefler ayni transaction icinde yazilir.
            await DeactivateExistingPlansAsync<NutritionPlan>(userId, cancellationToken);
            await PersistNutritionPlanAsync(nutrition, cancellationToken);

            if (nutrition.Targets is not null)
            {
                await UpsertNutritionGoalAsync(userId, nutrition.Targets, cancellationToken);
            }
        }

        var job = await _unitOfWork.Repository<PersonalizationJob>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is not null)
        {
            job.Status = PersonalizationJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            // Job tamamlandi; beslenme atlandiysa sebebi teshis icin saklanir.
            job.ErrorMessage = nutritionError is null ? null : TruncateErrorMessage(nutritionError);
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
    /// NutritionGoal profilden turetilen bir degerdir (uygulamada manuel duzenleme yolu yoktur): plan
    /// hangi hedeflere gore uretildiyse gunluk beslenme ekrani da ayni hedefleri gostersin diye guncellenir.
    /// </summary>
    private async Task UpsertNutritionGoalAsync(long userId, NutritionCalculationResult targets, CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<NutritionGoal>();
        var goal = await repo.GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);

        if (goal is null)
        {
            await repo.AddAsync(new NutritionGoal
            {
                UserId = userId,
                DailyCalories = targets.DailyCalories,
                ProteinTarget = targets.ProteinTarget,
                CarbsTarget = targets.CarbsTarget,
                FatTarget = targets.FatTarget,
                WaterTargetMl = targets.WaterTargetMl,
                CreatedAt = DateTime.UtcNow,
            }, cancellationToken);
        }
        else
        {
            goal.DailyCalories = targets.DailyCalories;
            goal.ProteinTarget = targets.ProteinTarget;
            goal.CarbsTarget = targets.CarbsTarget;
            goal.FatTarget = targets.FatTarget;
            goal.WaterTargetMl = targets.WaterTargetMl;
            goal.UpdatedAt = DateTime.UtcNow;
            repo.Update(goal);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
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
