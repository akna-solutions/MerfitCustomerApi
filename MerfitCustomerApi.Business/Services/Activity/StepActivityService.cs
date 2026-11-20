using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Activity;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Activity;

/// <summary>
/// IStepActivityService'in varsayilan implementasyonu.
///
/// Senkronizasyon modeli: istemci her gun icin 24 saatlik adim kovasini (o saatte atilan adim) gonderir.
/// Sunucu mevcut kayitla saat saat "buyuk olan kalir" birlestirmesi yapar; toplam her zaman kovalarin
/// toplamidir. Bu nedenle ayni istegin tekrari, yarim kalan bir istegin yeniden denenmesi ya da cihazin
/// eski bir kopyayi gondermesi adimlari ikinci kez eklemez ve azaltmaz.
///
/// Anti-hile: sunucu adimlari dogrudan olcemez; bu yuzden (1) makul olmayan veriler (saatlik/gunluk ust
/// sinir, henuz gelmemis saatlere adim, icinde bulunulan saatte dakikaya gore fazla adim, gelecek tarih)
/// gunu kalici olarak "suphe" isaretler ve bu gunler basari/puan hesabina katilmaz; (2) adimlar dogrudan
/// puan vermez, yalnizca bir kez kazanilabilen basarilara baglidir. Boylece sahte veriyle kazanilabilecek
/// puan sinirli kalir.
/// </summary>
public class StepActivityService : IStepActivityService
{
    public const string ReasonHourLimit = "hour_limit";
    public const string ReasonDayLimit = "day_limit";
    public const string ReasonFutureHour = "future_hour";
    public const string ReasonRateLimit = "rate_limit";

    public const string StatusAccepted = "accepted";
    public const string StatusUnchanged = "unchanged";
    public const string StatusRejected = "rejected";

    private const int MaxUtcOffsetMinutes = 14 * 60;
    private const int MaxSaveAttempts = 5;
    private const int HoursPerDay = 24;
    /// <summary>Yapisal olarak imkansiz deger (istek tamamen reddedilir); makul sinir ayrica uygulanir.</summary>
    private const int HardMaxStepsPerHour = 100_000;

    private static readonly HashSet<string> AllowedSources = new(StringComparer.Ordinal) { "pedometer", "healthkit", "health_connect" };
    private static readonly HashSet<string> AllowedPlatforms = new(StringComparer.Ordinal) { "ios", "android" };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMerfitScoreService _scoreService;
    private readonly StepActivityOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<StepActivityService> _logger;

    public StepActivityService(
        IUnitOfWork unitOfWork,
        IMerfitScoreService scoreService,
        IOptions<StepActivityOptions> options,
        TimeProvider clock,
        ILogger<StepActivityService> logger)
    {
        _unitOfWork = unitOfWork;
        _scoreService = scoreService;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<StepSyncResponse> SyncAsync(long userId, StepSyncRequest request, CancellationToken cancellationToken = default)
    {
        var (source, platform) = ValidateSyncRequest(request);

        StepSyncResponse response;
        bool changed;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                (response, changed) = await SyncOnceAsync(userId, request, source, platform, cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (attempt < MaxSaveAttempts && IsRetryableConflict(ex))
            {
                // Ayni gun icin eszamanli senkronizasyon: diger istek satiri olusturdu (benzersiz index) ya da
                // guncelledi (xmin). Guncel satir yeniden okunup birlestirilir; "buyuk olan kalir" kurali
                // sayesinde sonuc istek sirasindan bagimsizdir ve hicbir istegin adimlari kaybolmaz.
                _unitOfWork.ClearTracking();
            }
        }

        if (changed)
        {
            // Yeni adim verisi adim basarilarini tetikleyebilir (puan basari uzerinden gelir).
            await _scoreService.TryRefreshUserAsync(userId, cancellationToken);
        }

        return response;
    }

    private async Task<(StepSyncResponse Response, bool Changed)> SyncOnceAsync(
        long userId, StepSyncRequest request, string source, string? platform, CancellationToken cancellationToken)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var settings = await GetSettingsAsync(userId, cancellationToken);

        var dates = request.Days.Select(d => d.Date!.Value).ToList();
        var repository = _unitOfWork.Repository<DailyActivity>();
        var existingRows = await repository.GetQueryable(asNoTracking: false)
            .Where(a => a.UserId == userId && dates.Contains(a.Date))
            .ToListAsync(cancellationToken);
        var existingByDate = existingRows.ToDictionary(a => a.Date);

        var results = new List<StepSyncDayResultDto>();
        var changed = false;
        var dirty = false;

        foreach (var day in request.Days)
        {
            var date = day.Date!.Value;
            var localNow = nowUtc.AddMinutes(day.UtcOffsetMinutes);
            var localToday = DateOnly.FromDateTime(localNow);

            if (date > localToday || date < localToday.AddDays(-_options.MaxBackfillDays))
            {
                existingByDate.TryGetValue(date, out var untouched);
                var rejected = ToResult(untouched, date, settings.DailyGoal, StatusRejected);
                rejected.Reason = date > localToday ? "future_date" : "too_old";
                results.Add(rejected);
                continue;
            }

            existingByDate.TryGetValue(date, out var row);
            var previous = row?.HourlySteps ?? new int[HoursPerDay];
            var merged = new int[HoursPerDay];
            for (var h = 0; h < HoursPerDay; h++)
            {
                merged[h] = Math.Max(previous.Length > h ? previous[h] : 0, day.HourlySteps[h]);
            }

            var isNew = row is null;
            var stepsChanged = isNew || !merged.SequenceEqual(previous);
            var isToday = date == localToday;
            var goalChanged = row is not null && isToday && row.DailyGoal != settings.DailyGoal;

            if (!stepsChanged && !goalChanged)
            {
                results.Add(ToResult(row, date, settings.DailyGoal, StatusUnchanged));
                continue;
            }

            if (row is null)
            {
                row = new DailyActivity { UserId = userId, Date = date, CreatedAt = nowUtc, DailyGoal = settings.DailyGoal };
                await repository.AddAsync(row, cancellationToken);
                existingByDate[date] = row;
            }
            else
            {
                row.UpdatedAt = nowUtc;
            }

            // Gun icinde saat dilimi degisirse (orn. batiya ucus) daha once yazilmis gec saatler "gelecek saat"
            // sayilmasin diye gun icinde gorulen en buyuk UTC farki kullanilir.
            var effectiveOffset = isNew ? day.UtcOffsetMinutes : Math.Max(row.UtcOffsetMinutes, day.UtcOffsetMinutes);
            var reason = FindSuspiciousReason(merged, previous, date, nowUtc.AddMinutes(effectiveOffset));
            if (reason is not null && !row.IsSuspicious)
            {
                row.IsSuspicious = true;
                row.SuspiciousReason = reason;
                _logger.LogInformation("Adim verisi suphe olarak isaretlendi. UserId={UserId} Date={Date} Reason={Reason}", userId, date, reason);
            }

            row.HourlySteps = merged;
            row.TotalSteps = merged.Sum();
            row.DistanceMeters = (int)Math.Round(row.TotalSteps * settings.StrideLengthMeters);
            row.CaloriesBurned = (int)Math.Round(row.TotalSteps * settings.CaloriesPerStep);
            if (isToday)
            {
                // Gecmis gunlerin hedefi o gunun hedefi olarak kalir; yalnizca bugun guncel hedefi izler.
                row.DailyGoal = settings.DailyGoal;
            }

            row.UtcOffsetMinutes = effectiveOffset;
            row.Source = source;
            row.Platform = platform;
            row.LastSyncedAt = nowUtc;
            if (!isNew)
            {
                repository.Update(row);
            }

            changed |= stepsChanged;
            dirty = true;
            results.Add(ToResult(row, date, settings.DailyGoal, stepsChanged ? StatusAccepted : StatusUnchanged));
        }

        if (dirty)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return (new StepSyncResponse { Days = results, Settings = settings }, changed);
    }

    private static bool IsRetryableConflict(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException || DbExceptionClassifier.IsUniqueViolation(ex, out _);

    /// <summary>Makullik kurallari; ilk ihlalin kodunu doner, ihlal yoksa null.</summary>
    private string? FindSuspiciousReason(int[] merged, int[] previous, DateOnly date, DateTime localNow)
    {
        if (merged.Any(steps => steps > _options.MaxStepsPerHour))
        {
            return ReasonHourLimit;
        }

        if (merged.Sum() > _options.MaxStepsPerDay)
        {
            return ReasonDayLimit;
        }

        var localToday = DateOnly.FromDateTime(localNow);
        if (date < localToday)
        {
            // Gecmis gun: saat bazli zaman kurallari uygulanmaz (gun tamamen gecmiste).
            return null;
        }

        if (date > localToday)
        {
            return ReasonFutureHour;
        }

        var tolerance = TimeSpan.FromMinutes(_options.ClockToleranceMinutes);
        var latestLocal = localNow + tolerance;
        var latestHour = DateOnly.FromDateTime(latestLocal) > date ? HoursPerDay - 1 : latestLocal.Hour;
        for (var h = latestHour + 1; h < HoursPerDay; h++)
        {
            if (merged[h] > previous.ElementAtOrDefault(h))
            {
                return ReasonFutureHour;
            }
        }

        // Icinde bulunulan saat: o saatin gecen dakikasi kadar adim atilabilir.
        var currentHour = localNow.Hour;
        var elapsedMinutes = localNow.Minute + _options.ClockToleranceMinutes;
        if (merged[currentHour] > previous.ElementAtOrDefault(currentHour)
            && merged[currentHour] > elapsedMinutes * _options.MaxStepsPerMinute)
        {
            return ReasonRateLimit;
        }

        return null;
    }

    public async Task<StepSettingsDto> GetSettingsAsync(long userId, CancellationToken cancellationToken = default)
    {
        var setting = await _unitOfWork.Repository<UserActivitySetting>()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        var profile = await _unitOfWork.Repository<UserProfile>().GetQueryable()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.HeightCm, p.WeightKg, p.Gender })
            .FirstOrDefaultAsync(cancellationToken);

        var stride = StepMetrics.StrideLengthMeters(profile?.HeightCm, profile?.Gender, _options.DefaultStrideMeters);
        var weight = profile?.WeightKg is > 0 ? (double)profile.WeightKg.Value : _options.DefaultWeightKg;

        return new StepSettingsDto
        {
            DailyGoal = setting?.DailyStepGoal ?? _options.DefaultDailyGoal,
            DefaultDailyGoal = _options.DefaultDailyGoal,
            MinDailyGoal = _options.MinDailyGoal,
            MaxDailyGoal = _options.MaxDailyGoal,
            StrideLengthMeters = Math.Round(stride, 3),
            CaloriesPerStep = Math.Round(StepMetrics.CaloriesPerStep(stride, weight, _options.WalkingKcalPerKgPerKm), 5),
        };
    }

    public Task<StepSettingsDto> UpdateGoalAsync(long userId, UpdateStepGoalRequest request, CancellationToken cancellationToken = default) =>
        UpdateGoalAsync(userId, request, cancellationToken, attempt: 1);

    private async Task<StepSettingsDto> UpdateGoalAsync(long userId, UpdateStepGoalRequest request, CancellationToken cancellationToken, int attempt)
    {
        if (request.DailyGoal < _options.MinDailyGoal || request.DailyGoal > _options.MaxDailyGoal)
        {
            throw new AppValidationException(nameof(request.DailyGoal),
                $"Günlük adım hedefi {_options.MinDailyGoal:N0} ile {_options.MaxDailyGoal:N0} arasında olmalı.");
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var repository = _unitOfWork.Repository<UserActivitySetting>();
        var setting = await repository.GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (setting is null)
        {
            await repository.AddAsync(new UserActivitySetting { UserId = userId, DailyStepGoal = request.DailyGoal, CreatedAt = nowUtc }, cancellationToken);
        }
        else
        {
            setting.DailyStepGoal = request.DailyGoal;
            setting.UpdatedAt = nowUtc;
            repository.Update(setting);
        }

        // Bugunun kaydi varsa hedefi de guncellenir; tarih ancak sunucu saatine gore bugun/dun olabilir
        // (gecmis gunlerin hedefini sonradan dusurup basari kazanmak engellenir).
        if (request.Date is { } date && Math.Abs(date.DayNumber - DateOnly.FromDateTime(nowUtc).DayNumber) <= 1)
        {
            var today = await _unitOfWork.Repository<DailyActivity>().GetQueryable(asNoTracking: false)
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == date, cancellationToken);
            if (today is not null && today.DailyGoal != request.DailyGoal)
            {
                today.DailyGoal = request.DailyGoal;
                today.UpdatedAt = nowUtc;
                _unitOfWork.Repository<DailyActivity>().Update(today);
            }
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (attempt < MaxSaveAttempts && IsRetryableConflict(ex))
        {
            // Eszamanli ilk hedef kaydi ya da ayni anda guncellenen gun satiri: guncel veriyle tekrar denenir.
            _unitOfWork.ClearTracking();
            return await UpdateGoalAsync(userId, request, cancellationToken, attempt + 1);
        }

        return await GetSettingsAsync(userId, cancellationToken);
    }

    public async Task<StepDayDto> GetDayAsync(long userId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(userId, cancellationToken);
        var row = await _unitOfWork.Repository<DailyActivity>()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == date, cancellationToken);
        var dto = ToDto(row, date, settings.DailyGoal);
        dto.HourlySteps = row?.HourlySteps?.Length == HoursPerDay ? row.HourlySteps.ToArray() : new int[HoursPerDay];
        return dto;
    }

    public async Task<StepRangeResponse> GetRangeAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new AppValidationException("from", "Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        }

        if (to.DayNumber - from.DayNumber + 1 > _options.MaxRangeDays)
        {
            throw new AppValidationException("to", $"En fazla {_options.MaxRangeDays} günlük aralık sorgulanabilir.");
        }

        var settings = await GetSettingsAsync(userId, cancellationToken);
        var days = await LoadDaysAsync(userId, from, to, settings.DailyGoal, cancellationToken);
        return new StepRangeResponse { From = from, To = to, Days = days };
    }

    public async Task<StepWeeklySummaryDto> GetWeeklySummaryAsync(long userId, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(userId, cancellationToken);
        var start = endDate.AddDays(-6);
        var previousStart = start.AddDays(-7);
        var all = await LoadDaysAsync(userId, previousStart, endDate, settings.DailyGoal, cancellationToken);
        var days = all.Where(d => d.Date >= start).ToList();
        var previousTotal = all.Where(d => d.Date < start).Sum(d => d.TotalSteps);

        var total = days.Sum(d => d.TotalSteps);
        var completed = days.Count(d => d.GoalCompleted);
        var mostActive = days.Where(d => d.TotalSteps > 0)
            .OrderByDescending(d => d.TotalSteps)
            .ThenByDescending(d => d.Date)
            .FirstOrDefault();

        return new StepWeeklySummaryDto
        {
            StartDate = start,
            EndDate = endDate,
            Days = days,
            TotalSteps = total,
            AverageSteps = (int)Math.Round(total / 7.0),
            TotalDistanceMeters = days.Sum(d => d.DistanceMeters),
            TotalCaloriesBurned = days.Sum(d => d.CaloriesBurned),
            GoalCompletedDays = completed,
            GoalCompletionRate = Math.Round(completed * 100.0 / 7, 1),
            MostActiveDay = mostActive,
            PreviousPeriodTotalSteps = previousTotal,
            ChangePercent = previousTotal > 0 ? Math.Round((total - previousTotal) * 100.0 / previousTotal, 1) : null,
        };
    }

    private async Task<List<StepDayDto>> LoadDaysAsync(long userId, DateOnly from, DateOnly to, int currentGoal, CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Repository<DailyActivity>().GetQueryable()
            .Where(a => a.UserId == userId && a.Date >= from && a.Date <= to)
            .ToListAsync(cancellationToken);
        var byDate = rows.ToDictionary(r => r.Date);

        var result = new List<StepDayDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            byDate.TryGetValue(date, out var row);
            result.Add(ToDto(row, date, currentGoal));
        }

        return result;
    }

    private static StepDayDto ToDto(DailyActivity? row, DateOnly date, int currentGoal)
    {
        var dto = new StepDayDto();
        Fill(dto, row, date, currentGoal);
        return dto;
    }

    private static StepSyncDayResultDto ToResult(DailyActivity? row, DateOnly date, int currentGoal, string status)
    {
        var dto = new StepSyncDayResultDto { Status = status };
        Fill(dto, row, date, currentGoal);
        return dto;
    }

    private static void Fill(StepDayDto dto, DailyActivity? row, DateOnly date, int currentGoal)
    {
        var goal = row?.DailyGoal > 0 ? row.DailyGoal : currentGoal;
        var steps = row?.TotalSteps ?? 0;
        dto.Date = date;
        dto.TotalSteps = steps;
        dto.DistanceMeters = row?.DistanceMeters ?? 0;
        dto.CaloriesBurned = row?.CaloriesBurned ?? 0;
        dto.DailyGoal = goal;
        dto.GoalCompleted = steps >= goal;
        dto.CompletionPercent = goal > 0 ? Math.Round(steps * 100.0 / goal, 1) : 0;
        dto.HasData = row is not null;
        dto.IsVerified = row is not null && !row.IsSuspicious;
    }

    private (string Source, string? Platform) ValidateSyncRequest(StepSyncRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Days is null || request.Days.Count == 0)
        {
            errors["days"] = new[] { "En az bir gün gönderilmeli." };
        }
        else if (request.Days.Count > _options.MaxDaysPerSync)
        {
            errors["days"] = new[] { $"Tek istekte en fazla {_options.MaxDaysPerSync} gün gönderilebilir." };
        }
        else
        {
            if (request.Days.Any(d => d.Date is null))
            {
                errors["days.date"] = new[] { "Her gün için tarih zorunludur." };
            }
            else if (request.Days.Select(d => d.Date).Distinct().Count() != request.Days.Count)
            {
                errors["days.date"] = new[] { "Aynı gün birden fazla kez gönderilemez." };
            }

            if (request.Days.Any(d => d.HourlySteps is null || d.HourlySteps.Length != HoursPerDay))
            {
                errors["days.hourlySteps"] = new[] { "Saatlik adımlar 24 elemanlı olmalı." };
            }
            else if (request.Days.Any(d => d.HourlySteps.Any(s => s < 0 || s > HardMaxStepsPerHour)))
            {
                errors["days.hourlySteps"] = new[] { "Saatlik adım değeri geçersiz." };
            }

            if (request.Days.Any(d => Math.Abs(d.UtcOffsetMinutes) > MaxUtcOffsetMinutes))
            {
                errors["days.utcOffsetMinutes"] = new[] { "Saat dilimi farkı geçersiz." };
            }
        }

        var source = (request.Source ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedSources.Contains(source))
        {
            errors["source"] = new[] { "Geçersiz veri kaynağı." };
        }

        var platform = string.IsNullOrWhiteSpace(request.Platform) ? null : request.Platform.Trim().ToLowerInvariant();
        if (platform is not null && !AllowedPlatforms.Contains(platform))
        {
            errors["platform"] = new[] { "Geçersiz platform." };
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        return (source, platform);
    }
}

/// <summary>Adim -> mesafe/kalori tahmini. Tani amacli degildir; kullaniciya "tahmini" olarak gosterilir.</summary>
public static class StepMetrics
{
    /// <summary>
    /// Boydan adim uzunlugu: erkek boy x 0,415, kadin boy x 0,413 (yaygin kullanilan yaklasim). Boy yoksa varsayilan.
    /// </summary>
    public static double StrideLengthMeters(decimal? heightCm, Gender? gender, double defaultStride)
    {
        if (heightCm is not > 0)
        {
            return defaultStride;
        }

        var factor = gender switch
        {
            Gender.Male => 0.415,
            Gender.Female => 0.413,
            _ => 0.414,
        };
        var stride = (double)heightCm.Value / 100 * factor;
        // Hatali profil verisi (orn. boy 20 cm) makul olmayan sonuc vermesin.
        return Math.Clamp(stride, 0.4, 1.1);
    }

    /// <summary>Adim basina kalori = adim uzunlugu (km) x kilo x kcal/kg/km.</summary>
    public static double CaloriesPerStep(double strideMeters, double weightKg, double kcalPerKgPerKm) =>
        strideMeters / 1000 * weightKg * kcalPerKgPerKm;
}
