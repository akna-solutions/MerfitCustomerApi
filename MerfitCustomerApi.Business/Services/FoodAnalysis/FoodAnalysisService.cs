using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.FoodAnalysis;
using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.FoodAnalysis;

/// <summary>
/// IFoodAnalysisService'in varsayilan implementasyonu.
///
/// Analiz: dosya imzasi/boyutu dogrulanir -> gunluk kota kontrol edilir -> fotograf goruntu modeline gonderilir
/// (saglayici arayuzu arkasinda) -> donen JSON sema, aralik ve enerji tutarliligi kontrollerinden gecirilir ->
/// her yemek dogrulanmis besin katalogu (Food) ile eslestirilir; eslesirse degerler katalogdan, eslesmezse model
/// tahmininden hesaplanir -> sonuc, kota/sahiplik/tekrar kayit korumasi icin mevcut ai.AiGenerationRequest ve
/// ai.AiGenerationResult tablolarina yazilir. Fotograf hicbir yerde saklanmaz ve loglanmaz; hicbir ogun kaydi olusmaz.
///
/// Onay: analiz kullanicinin kendi kaydi olmali ve suresi gecmemis olmali. Besin degerleri istemciden degil
/// saklanan analizden okunur. Kayit tek transaction icinde, mevcut Meal/MealItem yazim kurallariyla
/// (MealLogWriter) yapilir. Analiz kaydi "kaydedildi" olarak isaretlenir (UpdatedAt, iyimser eszamanlilik
/// belirteci); ayni analiz icin ikinci istek yeni kayit olusturmaz, ilk kaydin sonucunu dondurur.
/// </summary>
public class FoodAnalysisService : IFoodAnalysisService
{
    /// <summary>
    /// Katalogda karsiligi olmayan, model tahminiyle kaydedilen yemekler icin olusturulan Food kayitlarinin markasi.
    /// Bu kayitlar besin aramasinda gosterilmez (FoodService) ve kisisel plana girmez (Category = null).
    /// </summary>
    public const string EstimatedFoodBrand = "Fotoğraf analizi (tahmini)";

    public const string UnavailableMessage = "Fotoğraf analizi şu an kullanılamıyor. Lütfen daha sonra tekrar dene.";
    public const string BusyMessage = "Fotoğraf analizi şu an çok yoğun. Lütfen biraz sonra tekrar dene.";
    public const string TimeoutMessage = "Analiz zaman aşımına uğradı. Lütfen tekrar dene.";
    public const string InvalidResultMessage = "Analiz sonucu okunamadı. Lütfen fotoğrafı tekrar dene.";
    public const string NoFoodMessage = "Fotoğrafta tanınabilen bir yemek bulunamadı. Tabağın tamamının net göründüğü bir fotoğraf dene.";
    public const string RefusedMessage = "Bu fotoğraf analiz edilemedi. Yalnızca yemeğin göründüğü bir fotoğraf dene.";

    private const int MaxStoredResultLength = 2000;
    private const int CatalogCandidateLimit = 5000;
    private const decimal LowConfidenceThreshold = 0.6m;

    private static readonly JsonSerializerOptions StorageJson = new()
    {
        // Turkce karakterler ç gibi kacislanmasin (sutun 2000 karakterle sinirli).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFoodImageAnalysisProvider _provider;
    private readonly FoodAnalysisGate _gate;
    private readonly FoodAnalysisOptions _options;
    private readonly MealLogWriter _mealLogWriter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FoodAnalysisService> _logger;

    public FoodAnalysisService(
        IUnitOfWork unitOfWork,
        IFoodImageAnalysisProvider provider,
        FoodAnalysisGate gate,
        IOptions<FoodAnalysisOptions> options,
        IMerfitScoreService scoreService,
        TimeProvider timeProvider,
        ILogger<FoodAnalysisService> logger,
        INotificationReminderCanceller? reminderCanceller = null)
    {
        _unitOfWork = unitOfWork;
        _provider = provider;
        _gate = gate;
        _options = options.Value;
        _mealLogWriter = new MealLogWriter(unitOfWork, scoreService, reminderCanceller);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<FoodAnalysisResultDto> AnalyzeAsync(long userId, FoodImageUpload upload, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_provider.IsConfigured)
        {
            throw new ExternalServiceUnavailableException(UnavailableMessage);
        }

        var image = ValidateImage(upload);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var remaining = await EnsureQuotaAsync(userId, now, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        FoodImageAnalysisProviderResult result;
        try
        {
            result = await CallProviderAsync(upload.Content, image.MimeType, cancellationToken);
        }
        catch (ExternalServiceUnavailableException)
        {
            await TryRecordFailureAsync(userId, image, upload.Content.Length, now, stopwatch.ElapsedMilliseconds, "provider_error");
            throw;
        }

        if (result.Refused)
        {
            await TryRecordFailureAsync(userId, image, upload.Content.Length, now, stopwatch.ElapsedMilliseconds, "refused");
            throw new AppValidationException("Photo", RefusedMessage);
        }

        ParsedFoodAnalysis parsed;
        try
        {
            parsed = FoodAnalysisResponseParser.Parse(result.Json, Math.Clamp(_options.MaxItems, 1, 10));
        }
        catch (FoodAnalysisInvalidResponseException ex)
        {
            // Yanit icerigi loglanmaz; yalnizca hata kodu ve saglayici bilgisi.
            _logger.LogWarning("Fotograf analizi yaniti gecersiz. UserId: {UserId}, Provider: {Provider}, Model: {Model}, Reason: {Reason}, StopReason: {StopReason}",
                userId, result.Provider, result.Model, ex.Reason, result.StopReason);
            await TryRecordFailureAsync(userId, image, upload.Content.Length, now, stopwatch.ElapsedMilliseconds, "invalid_response:" + ex.Reason);
            throw new ExternalServiceUnavailableException(InvalidResultMessage, ex);
        }

        // Saglayici cagrisi basarili oldugu icin "yemek yok" sonucu da kotadan duser.
        var request = NewUsageRecord(userId, image, upload.Content.Length, AiGenerationStatus.Completed, now, stopwatch.ElapsedMilliseconds, result.Model);
        await _unitOfWork.Repository<AiGenerationRequest>().AddAsync(request, cancellationToken);
        await _unitOfWork.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation(
            "Fotograf analizi tamamlandi. UserId: {UserId}, AnalysisId: {AnalysisId}, Provider: {Provider}, Model: {Model}, Items: {Items}, InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, DurationMs: {DurationMs}",
            userId, request.Id, result.Provider, result.Model, parsed.Items.Count, result.InputTokens, result.OutputTokens, stopwatch.ElapsedMilliseconds);

        if (!parsed.ContainsFood)
        {
            throw new AppValidationException("Photo", NoFoodMessage);
        }

        var catalog = await LoadCatalogAsync(cancellationToken);
        var dto = BuildResult(request.Id, parsed, catalog, now);
        dto.RemainingAnalyses = Math.Max(0, remaining - 1);

        await _unitOfWork.Repository<AiGenerationResult>().AddAsync(new AiGenerationResult
        {
            RequestId = request.Id,
            ResultJson = SerializeStoredAnalysis(dto, result.Provider, result.InputTokens, result.OutputTokens),
            CreatedAt = now,
            CreatedUser = userId,
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(CancellationToken.None);

        await TryPurgeOldResultsAsync(userId, now);
        return dto;
    }

    public async Task<ConfirmFoodAnalysisResponse> ConfirmAsync(long userId, long analysisId, ConfirmFoodAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var mealType = MealLogWriter.ParseMealType(request.MealType, nameof(request.MealType));
        var date = ResolveDate(request.Date);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var analysis = await _unitOfWork.Repository<AiGenerationRequest>().GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(r => r.Id == analysisId && r.UserId == userId && r.Type == AiRequestType.FoodPhotoAnalysis, cancellationToken)
            ?? throw new NotFoundException("Analiz bulunamadı.");

        if (analysis.UpdatedAt is not null)
        {
            return await AlreadyLoggedAsync(analysisId, cancellationToken);
        }
        if (analysis.Status != AiGenerationStatus.Completed)
        {
            throw new AppValidationException("AnalysisId", "Başarısız bir analiz öğüne kaydedilemez.");
        }
        if (analysis.CreatedAt < now.AddHours(-Math.Max(1, _options.ConfirmWindowHours)))
        {
            throw new AppValidationException("AnalysisId", "Bu analizin süresi doldu. Lütfen fotoğrafı yeniden analiz et.");
        }

        var stored = await LoadStoredAnalysisAsync(analysisId, cancellationToken)
            ?? throw new AppValidationException("AnalysisId", "Bu analizde kaydedilecek yemek yok. Lütfen fotoğrafı yeniden analiz et.");

        var selections = ResolveSelections(request.Items, stored);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        List<(MealItem Item, string Name)> logged;
        try
        {
            // Analizi "kaydedildi" olarak isaretle. UpdatedAt eszamanlilik belirtecidir: ayni analiz icin eszamanli
            // ikinci onay bu satiri guncelleyemez (DbUpdateConcurrencyException) ve yeni ogun kaydi olusturmaz.
            analysis.UpdatedAt = now;
            analysis.UpdatedUser = userId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var meal = await _mealLogWriter.GetOrCreateMealAsync(userId, date, mealType, cancellationToken);
            logged = new List<(MealItem, string)>();
            foreach (var (item, grams) in selections)
            {
                var food = await ResolveFoodAsync(userId, item, now, cancellationToken);
                var mealItem = await _mealLogWriter.AddItemAsync(meal, food, grams / food.ServingSize, cancellationToken);
                logged.Add((mealItem, food.Name));
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.Repository<AiGenerationResult>().AddAsync(new AiGenerationResult
            {
                RequestId = analysisId,
                ResultJson = JsonSerializer.Serialize(new StoredLog
                {
                    MealItemIds = logged.Select(l => l.Item.Id).ToList(),
                    MealType = mealType.ToString(),
                    Date = DateOnly.FromDateTime(date).ToString("yyyy-MM-dd"),
                }, StorageJson),
                CreatedAt = now,
                CreatedUser = userId,
            }, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackQuietlyAsync();
            return await AlreadyLoggedAsync(analysisId, cancellationToken);
        }
        catch
        {
            await RollbackQuietlyAsync();
            throw;
        }

        await _mealLogWriter.AfterLoggedAsync(userId, date, mealType, cancellationToken);

        _logger.LogInformation("Fotograf analizi ogune kaydedildi. UserId: {UserId}, AnalysisId: {AnalysisId}, Items: {Items}",
            userId, analysisId, logged.Count);

        return new ConfirmFoodAnalysisResponse
        {
            AnalysisId = analysisId,
            AlreadyLogged = false,
            Entries = logged.Select(l => ToEntry(l.Item, l.Name, mealType)).ToList(),
        };
    }

    private FoodImageInfo ValidateImage(FoodImageUpload upload)
    {
        if (upload.Content.Length == 0)
        {
            throw new AppValidationException("Photo", "Fotoğraf boş. Lütfen yeni bir fotoğraf seç.");
        }
        if (upload.Content.Length > _options.MaxImageBytes)
        {
            throw new AppValidationException("Photo", $"Fotoğraf en fazla {_options.MaxImageBytes / (1024 * 1024)} MB olabilir.");
        }

        var info = FoodImageInspector.Inspect(upload.Content.Span)
            ?? throw new AppValidationException("Photo", "Dosya bozuk ya da desteklenmeyen bir biçimde. Lütfen JPEG, PNG veya WebP fotoğraf yükle.");

        if (Math.Min(info.Width, info.Height) < _options.MinImageDimension)
        {
            throw new AppValidationException("Photo", "Fotoğraf çok küçük. Lütfen daha yüksek çözünürlüklü bir fotoğraf kullan.");
        }
        if (Math.Max(info.Width, info.Height) > _options.MaxImageDimension)
        {
            throw new AppValidationException("Photo", "Fotoğrafın çözünürlüğü çok yüksek. Lütfen fotoğrafı küçültüp tekrar dene.");
        }
        return info;
    }

    /// <summary>Son 24 saatteki basarili analizleri sayar; kalan hakki dondurur.</summary>
    private async Task<int> EnsureQuotaAsync(long userId, DateTime now, CancellationToken cancellationToken)
    {
        var since = now.AddHours(-24);
        var recent = await _unitOfWork.Repository<AiGenerationRequest>().GetQueryable()
            .Where(r => r.UserId == userId && r.Type == AiRequestType.FoodPhotoAnalysis
                && r.Status == AiGenerationStatus.Completed && r.CreatedAt >= since)
            .Select(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var remaining = _options.DailyAnalysisLimitPerUser - recent.Count;
        if (remaining > 0)
        {
            return remaining;
        }

        // En eski analiz 24 saatlik pencereden ciktiginda yeni hak acilir.
        var retryAfter = recent.Count == 0 ? (TimeSpan?)null : recent.Min().AddHours(24) - now;
        throw new TooManyRequestsException("Bugünlük fotoğraf analizi hakkın doldu. Öğünü listeden arayarak ekleyebilirsin.",
            retryAfter is { } wait && wait > TimeSpan.Zero ? wait : null);
    }

    private async Task<FoodImageAnalysisProviderResult> CallProviderAsync(ReadOnlyMemory<byte> image, string mimeType, CancellationToken cancellationToken)
    {
        if (_gate.CooldownRemaining is { } cooldown)
        {
            throw new ExternalServiceUnavailableException(BusyMessage, retryAfter: cooldown);
        }
        if (!await _gate.TryEnterAsync(TimeSpan.FromSeconds(Math.Max(1, _options.QueueTimeoutSeconds)), cancellationToken))
        {
            throw new ExternalServiceUnavailableException(BusyMessage, retryAfter: TimeSpan.FromSeconds(5));
        }

        try
        {
            var request = new FoodImageAnalysisProviderRequest(image, mimeType, FoodAnalysisPrompt.Instructions, FoodAnalysisPrompt.ResponseSchema);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await _provider.AnalyzeAsync(request, cancellationToken);
                }
                catch (NutritionAiProviderException ex)
                {
                    _logger.LogWarning("Fotograf analizi saglayici hatasi. Provider: {Provider}, Kind: {Kind}, Status: {Status}, ErrorCode: {ErrorCode}, Attempt: {Attempt}",
                        ex.Provider, ex.Kind, ex.StatusCode, ex.ErrorCode, attempt + 1);

                    switch (ex.Kind)
                    {
                        case NutritionAiFailureKind.Transient when attempt < Math.Max(0, _options.MaxRetries):
                            await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), cancellationToken);
                            continue;
                        case NutritionAiFailureKind.RateLimited:
                            var wait = ex.RetryAfter ?? TimeSpan.FromSeconds(Math.Max(1, _options.DefaultCooldownSeconds));
                            _gate.StartCooldown(wait);
                            throw new ExternalServiceUnavailableException(BusyMessage, ex, wait);
                        case NutritionAiFailureKind.Timeout:
                            throw new ExternalServiceUnavailableException(TimeoutMessage, ex);
                        default:
                            throw new ExternalServiceUnavailableException(UnavailableMessage, ex, ex.RetryAfter);
                    }
                }
            }
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>Eslestirmeye uygun katalog besinleri: barkodsuz, tahmini olmayan, g/ml birimli kayitlar (100 g'a normalize).</summary>
    private async Task<IReadOnlyList<CatalogFoodCandidate>> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Repository<Food>().GetQueryable()
            .Where(f => f.Barcode == null && (f.Brand == null || f.Brand != EstimatedFoodBrand) && f.ServingSize > 0)
            .OrderBy(f => f.Id)
            .Take(CatalogCandidateLimit)
            .Select(f => new { f.Id, f.Name, f.ServingSize, f.ServingUnit, f.Calories, f.Protein, f.Carbs, f.Fat })
            .ToListAsync(cancellationToken);

        return rows
            .Where(f => ServingUnits.IsWeighable(f.ServingUnit))
            .Select(f =>
            {
                var factor = 100m / f.ServingSize;
                return new CatalogFoodCandidate(
                    f.Id,
                    f.Name,
                    FoodCatalogMatcher.Normalize(f.Name),
                    Math.Round(f.Calories * factor, 1),
                    Math.Round(f.Protein * factor, 1),
                    Math.Round(f.Carbs * factor, 1),
                    Math.Round(f.Fat * factor, 1));
            })
            .Where(f => f.NormalizedName.Length > 0)
            .ToList();
    }

    private FoodAnalysisResultDto BuildResult(long analysisId, ParsedFoodAnalysis parsed, IReadOnlyList<CatalogFoodCandidate> catalog, DateTime now)
    {
        var items = new List<FoodAnalysisItemDto>();
        foreach (var (item, index) in parsed.Items.Select((item, index) => (item, index)))
        {
            var match = FoodCatalogMatcher.FindBestMatch(item, catalog);
            var per100 = match is null
                ? new FoodAnalysisNutrientsDto { Calories = item.CaloriesPer100g, Protein = item.ProteinPer100g, Carbs = item.CarbsPer100g, Fat = item.FatPer100g }
                : new FoodAnalysisNutrientsDto { Calories = match.CaloriesPer100g, Protein = match.ProteinPer100g, Carbs = match.CarbsPer100g, Fat = match.FatPer100g };

            var spread = item.EstimatedGrams <= 0 ? 0 : (item.MaxGrams - item.MinGrams) / item.EstimatedGrams;
            items.Add(new FoodAnalysisItemDto
            {
                ItemId = index + 1,
                FoodName = item.Name,
                EstimatedGrams = item.EstimatedGrams,
                MinGrams = item.MinGrams,
                MaxGrams = item.MaxGrams,
                Per100g = per100,
                Calories = Scale(per100.Calories, item.EstimatedGrams, 0),
                Protein = Scale(per100.Protein, item.EstimatedGrams, 1),
                Carbs = Scale(per100.Carbs, item.EstimatedGrams, 1),
                Fat = Scale(per100.Fat, item.EstimatedGrams, 1),
                Confidence = item.Confidence,
                PortionUncertainty = item.PortionUncertainty,
                NeedsPortionConfirmation = item.PortionUncertainty == "high" || item.Confidence < LowConfidenceThreshold || spread > 0.6m,
                IsEstimate = true,
                NutritionSource = match is null ? FoodAnalysisNutritionSources.AiEstimate : FoodAnalysisNutritionSources.Catalog,
                MatchedFoodName = match?.Name,
                Note = item.Note,
                MatchedFoodId = match?.FoodId,
            });
        }

        var warnings = new List<string>(parsed.Warnings);
        if (items.Any(i => i.NeedsPortionConfirmation))
        {
            warnings.Add("Bazı porsiyonlar fotoğraftan net anlaşılmıyor. Kaydetmeden önce işaretli gramajları kontrol et.");
        }
        var estimated = items.Where(i => i.NutritionSource == FoodAnalysisNutritionSources.AiEstimate).Select(i => i.FoodName).ToList();
        if (estimated.Count > 0)
        {
            warnings.Add($"{string.Join(", ", estimated)} besin kataloğunda bulunamadı; değerleri yapay zekâ tahminidir.");
        }
        if (parsed.UnidentifiedItems.Count > 0)
        {
            warnings.Add($"Tanımlanamayan yiyecekler var ({string.Join(", ", parsed.UnidentifiedItems)}). Bunları listeden arayarak ekleyebilirsin.");
        }

        return new FoodAnalysisResultDto
        {
            AnalysisId = analysisId,
            FoodName = parsed.MealName,
            Items = items,
            Totals = new FoodAnalysisTotalsDto
            {
                EstimatedGrams = items.Sum(i => i.EstimatedGrams),
                Calories = items.Sum(i => i.Calories),
                Protein = items.Sum(i => i.Protein),
                Carbs = items.Sum(i => i.Carbs),
                Fat = items.Sum(i => i.Fat),
            },
            Confidence = parsed.OverallConfidence,
            IsEstimate = true,
            NeedsPortionConfirmation = items.Any(i => i.NeedsPortionConfirmation),
            Warnings = warnings,
            UnidentifiedItems = parsed.UnidentifiedItems.ToList(),
            ConfirmBefore = now.AddHours(Math.Max(1, _options.ConfirmWindowHours)),
        };
    }

    private static decimal Scale(decimal per100, decimal grams, int decimals) => Math.Round(per100 * grams / 100m, decimals);

    private static string SerializeStoredAnalysis(FoodAnalysisResultDto dto, string provider, int inputTokens, int outputTokens)
    {
        StoredAnalysis Build(int nameLength) => new()
        {
            Provider = provider,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Items = dto.Items.Select(i => new StoredItem
            {
                Id = i.ItemId,
                Name = i.FoodName.Length > nameLength ? i.FoodName[..nameLength] : i.FoodName,
                FoodId = i.MatchedFoodId,
                Calories = i.Per100g.Calories,
                Protein = i.Per100g.Protein,
                Carbs = i.Per100g.Carbs,
                Fat = i.Per100g.Fat,
            }).ToList(),
        };

        var json = JsonSerializer.Serialize(Build(FoodAnalysisResponseParser.MaxNameLength), StorageJson);
        return json.Length <= MaxStoredResultLength ? json : JsonSerializer.Serialize(Build(30), StorageJson);
    }

    private async Task<StoredAnalysis?> LoadStoredAnalysisAsync(long analysisId, CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Repository<AiGenerationResult>().GetQueryable()
            .Where(r => r.RequestId == analysisId)
            .OrderBy(r => r.Id)
            .Select(r => r.ResultJson)
            .ToListAsync(cancellationToken);

        foreach (var json in rows)
        {
            try
            {
                if (JsonSerializer.Deserialize<StoredAnalysis>(json, StorageJson) is { Kind: StoredAnalysis.KindValue, Items.Count: > 0 } stored)
                {
                    return stored;
                }
            }
            catch (JsonException)
            {
                // Bozuk kayit yok sayilir.
            }
        }
        return null;
    }

    private static List<(StoredItem Item, decimal Grams)> ResolveSelections(IReadOnlyList<ConfirmFoodAnalysisItem> requested, StoredAnalysis stored)
    {
        if (requested.Count == 0)
        {
            throw new AppValidationException("Items", "En az bir yemek seçilmelidir.");
        }
        if (requested.Select(i => i.ItemId).Distinct().Count() != requested.Count)
        {
            throw new AppValidationException("Items", "Aynı yemek birden fazla kez gönderildi.");
        }

        var byId = stored.Items.ToDictionary(i => i.Id);
        return requested.Select(selection =>
        {
            if (!byId.TryGetValue(selection.ItemId, out var item))
            {
                throw new AppValidationException("Items", "Seçilen yemek bu analizde bulunamadı.");
            }
            if (selection.Grams < 1 || selection.Grams > FoodAnalysisResponseParser.MaxGrams)
            {
                throw new AppValidationException("Items", $"Gramaj 1 ile {FoodAnalysisResponseParser.MaxGrams:0} arasında olmalıdır.");
            }
            return (item, Math.Round(selection.Grams, 0));
        }).ToList();
    }

    /// <summary>
    /// Katalog eslesmesinde mevcut Food kullanilir. Eslesme yoksa (ya da katalog kaydi silinmisse) analizdeki 100 g
    /// degerleriyle kullaniciya ait tahmini bir Food olusturulur; ayni kullanici ayni yemegi tekrar kaydederse yeniden kullanilir.
    /// </summary>
    private async Task<Food> ResolveFoodAsync(long userId, StoredItem item, DateTime now, CancellationToken cancellationToken)
    {
        var foods = _unitOfWork.Repository<Food>();
        if (item.FoodId is { } foodId
            && await foods.GetByIdAsync(foodId, cancellationToken) is { } catalogFood
            && catalogFood.ServingSize > 0
            && ServingUnits.IsWeighable(catalogFood.ServingUnit))
        {
            return catalogFood;
        }

        var name = item.Name.Length > 200 ? item.Name[..200] : item.Name;
        var existing = await foods.FirstOrDefaultAsync(f =>
            f.CreatedUser == userId && f.Brand == EstimatedFoodBrand && f.Name == name
            && f.Calories == item.Calories && f.Protein == item.Protein && f.Carbs == item.Carbs && f.Fat == item.Fat, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var food = new Food
        {
            Name = name,
            Brand = EstimatedFoodBrand,
            ServingSize = 100,
            ServingUnit = "g",
            Calories = item.Calories,
            Protein = item.Protein,
            Carbs = item.Carbs,
            Fat = item.Fat,
            CreatedAt = now,
            CreatedUser = userId,
        };
        await foods.AddAsync(food, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return food;
    }

    private async Task<ConfirmFoodAnalysisResponse> AlreadyLoggedAsync(long analysisId, CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Repository<AiGenerationResult>().GetQueryable()
            .Where(r => r.RequestId == analysisId)
            .OrderByDescending(r => r.Id)
            .Select(r => r.ResultJson)
            .ToListAsync(cancellationToken);

        StoredLog? log = null;
        foreach (var json in rows)
        {
            try
            {
                log = JsonSerializer.Deserialize<StoredLog>(json, StorageJson);
                if (log is { Kind: StoredLog.KindValue }) break;
                log = null;
            }
            catch (JsonException)
            {
            }
        }

        var response = new ConfirmFoodAnalysisResponse { AnalysisId = analysisId, AlreadyLogged = true };
        if (log is null || log.MealItemIds.Count == 0 || !Enum.TryParse<MealType>(log.MealType, out var mealType))
        {
            return response;
        }

        var ids = log.MealItemIds;
        var items = await _unitOfWork.Repository<MealItem>().GetQueryable()
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(cancellationToken);
        var foodIds = items.Select(i => i.FoodId).Distinct().ToList();
        var names = await _unitOfWork.Repository<Food>().GetQueryable()
            .Where(f => foodIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        response.Entries = items
            .OrderBy(i => ids.IndexOf(i.Id))
            .Select(i => ToEntry(i, names.GetValueOrDefault(i.FoodId, string.Empty), mealType))
            .ToList();
        return response;
    }

    private static CustomerMealEntryDto ToEntry(MealItem item, string name, MealType mealType) => new()
    {
        Id = item.Id,
        Type = MealLogWriter.MapMealType(mealType),
        Name = name,
        Calories = (int)Math.Round(item.Calories),
    };

    /// <summary>Kayit gunu kullanicinin saat dilimine gore dogrulanir: bugunden MaxPastDays gun once ile yarin arasi.</summary>
    private DateTime ResolveDate(DateOnly? requested)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), zone).DateTime);
        var date = requested ?? today;
        if (date > today.AddDays(1) || date < today.AddDays(-Math.Max(0, _options.MaxPastDays)))
        {
            throw new AppValidationException("Date", "Bu tarihe öğün kaydedilemez.");
        }
        return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }

    private static AiGenerationRequest NewUsageRecord(long userId, FoodImageInfo image, int bytes, AiGenerationStatus status, DateTime startedAt, long durationMs, string? model) => new()
    {
        UserId = userId,
        Type = AiRequestType.FoodPhotoAnalysis,
        // Fotograf ya da icerik saklanmaz; yalnizca bicim ve boyut.
        Prompt = $"Yemek fotoğrafı analizi ({image.MimeType}, {image.Width}x{image.Height}, {Math.Max(1, bytes / 1024)} KB)",
        Status = status,
        Model = model,
        StartedAt = startedAt,
        CompletedAt = startedAt.AddMilliseconds(durationMs),
        CreatedAt = startedAt,
        CreatedUser = userId,
    };

    private async Task TryRecordFailureAsync(long userId, FoodImageInfo image, int bytes, DateTime startedAt, long durationMs, string reason)
    {
        try
        {
            _unitOfWork.ClearTracking();
            var request = NewUsageRecord(userId, image, bytes, AiGenerationStatus.Failed, startedAt, durationMs, _provider.Model);
            await _unitOfWork.Repository<AiGenerationRequest>().AddAsync(request, CancellationToken.None);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            await _unitOfWork.Repository<AiGenerationResult>().AddAsync(new AiGenerationResult
            {
                RequestId = request.Id,
                ResultJson = JsonSerializer.Serialize(new { kind = "failure", reason }, StorageJson),
                CreatedAt = startedAt,
                CreatedUser = userId,
            }, CancellationToken.None);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fotograf analizi hata kaydi yazilamadi. UserId: {UserId}", userId);
        }
    }

    /// <summary>
    /// Saklama politikasi: ResultRetentionDays'den eski analiz sonuclari (yemek adlari ve degerler) silinir; kullanim
    /// kaydi (icerik tasimaz) kota/maliyet takibi icin kalir. Fotograf zaten hic saklanmaz.
    /// </summary>
    private async Task TryPurgeOldResultsAsync(long userId, DateTime now)
    {
        try
        {
            var cutoff = now.AddDays(-Math.Max(1, _options.ResultRetentionDays));
            var oldRequestIds = _unitOfWork.Repository<AiGenerationRequest>().GetQueryable()
                .Where(r => r.UserId == userId && r.Type == AiRequestType.FoodPhotoAnalysis && r.CreatedAt < cutoff)
                .Select(r => r.Id);
            var stale = await _unitOfWork.Repository<AiGenerationResult>().GetQueryable(asNoTracking: false)
                .Where(r => oldRequestIds.Contains(r.RequestId))
                .Take(200)
                .ToListAsync(CancellationToken.None);
            if (stale.Count == 0) return;

            _unitOfWork.Repository<AiGenerationResult>().RemoveRange(stale);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _unitOfWork.ClearTracking();
            _logger.LogWarning(ex, "Eski fotograf analizi sonuclari silinemedi. UserId: {UserId}", userId);
        }
    }

    private async Task RollbackQuietlyAsync()
    {
        try
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fotograf analizi kayit transaction'i geri alinamadi.");
        }
        _unitOfWork.ClearTracking();
    }

    /// <summary>ai.AiGenerationResult.ResultJson icinde saklanan analiz (100 g degerleri); fotograf ya da model metni icermez.</summary>
    private sealed class StoredAnalysis
    {
        public const string KindValue = "food_analysis";

        [JsonPropertyName("kind")] public string Kind { get; init; } = KindValue;
        [JsonPropertyName("v")] public int Version { get; init; } = 1;
        [JsonPropertyName("provider")] public string? Provider { get; init; }
        [JsonPropertyName("in")] public int InputTokens { get; init; }
        [JsonPropertyName("out")] public int OutputTokens { get; init; }
        [JsonPropertyName("items")] public List<StoredItem> Items { get; init; } = new();
    }

    private sealed class StoredItem
    {
        [JsonPropertyName("id")] public int Id { get; init; }
        [JsonPropertyName("n")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("f")] public long? FoodId { get; init; }
        [JsonPropertyName("k")] public decimal Calories { get; init; }
        [JsonPropertyName("p")] public decimal Protein { get; init; }
        [JsonPropertyName("c")] public decimal Carbs { get; init; }
        [JsonPropertyName("y")] public decimal Fat { get; init; }
    }

    private sealed class StoredLog
    {
        public const string KindValue = "food_analysis_logged";

        [JsonPropertyName("kind")] public string Kind { get; init; } = KindValue;
        [JsonPropertyName("mealItemIds")] public List<long> MealItemIds { get; init; } = new();
        [JsonPropertyName("mealType")] public string MealType { get; init; } = string.Empty;
        [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
    }
}
