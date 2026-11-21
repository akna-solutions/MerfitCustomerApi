using System.Globalization;
using System.Text;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Training;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Training;

public interface ITrainingCoachService
{
    Task<TrainingCoachResponse> AskAsync(long userId, TrainingCoachRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// AI antrenman kocu. Mevcut AI saglayici zinciri (Groq / Gemini / Anthropic; INutritionAssistantModelClient) kullanilir,
/// yeni anahtar veya saglayici eklenmez. AI program, egzersiz veya agirlik URETMEZ: motorun ciktisini (program, gelisim,
/// toparlanma, degisiklikler, alternatifler) dogal dille aciklar. Yanit <see cref="CoachResponseValidator"/> ile dogrulanir;
/// AI kullanilamazsa ya da yanit dogrulanamazsa ayni gerceklerden kural tabanli metin doner.
/// </summary>
public sealed class TrainingCoachService : ITrainingCoachService
{
    internal const string Instructions = """
        Sen MBFit uygulamasının antrenman koçusun. Türkçe, kısa (en fazla 6 cümle), sade ve motive edici yaz.
        KURALLAR:
        - Yalnızca BAĞLAM bölümündeki gerçekleri kullan. Bağlamda olmayan egzersiz, ağırlık, set veya tekrar önerme ya da uydurma.
        - Yeni egzersiz önerme; alternatif sorulursa yalnızca bağlamdaki alternatifleri anlat.
        - Kilogram değeri yazacaksan yalnızca bağlamda geçen değerleri kullan.
        - Tahmini 1RM'yi gerçek maksimum gibi sunma, "tahmini" de.
        - Ağrı veya sakatlıkta teşhis koyma, tedavi veya ilaç önerme; hareketi zorlamamasını ve gerekirse bir sağlık profesyoneline danışmasını söyle.
        - Programı değiştirme yetkin yok; değişiklikler yalnızca kullanıcı onayıyla uygulanır.
        YANIT BİÇİMİ: Yalnızca şu JSON: {"message": "<metin>", "exerciseIds": [<metinde adı geçen egzersizlerin bağlamdaki kimlikleri>]}
        """;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ITrainingProgramService _programs;
    private readonly INutritionAssistantModelClient? _model;
    private readonly TrainingEngineOptions _options;
    private readonly ILogger<TrainingCoachService>? _logger;

    public TrainingCoachService(
        IUnitOfWork unitOfWork,
        ITrainingProgramService programs,
        IOptions<TrainingEngineOptions> options,
        INutritionAssistantModelClient? model = null,
        ILogger<TrainingCoachService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _programs = programs;
        _model = model;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TrainingCoachResponse> AskAsync(long userId, TrainingCoachRequest request, CancellationToken cancellationToken = default)
    {
        var topic = (request.Topic ?? "program").Trim().ToLowerInvariant();
        var facts = await BuildFactsAsync(userId, topic, request, cancellationToken);
        var fallback = new TrainingCoachResponse { Message = facts.Fallback, Source = "rules", ReferencedExerciseIds = facts.FallbackExerciseIds };

        if (_model is null || !_model.IsConfigured)
        {
            return fallback;
        }

        var question = topic == "question" && !string.IsNullOrWhiteSpace(request.Question)
            ? request.Question.Trim()
            : topic switch
            {
                "progress" => "Gelişimimi özetler misin?",
                "changes" => "Programımda neyin neden değiştiğini açıklar mısın?",
                "alternative" => "Bu egzersizin alternatiflerini açıklar mısın?",
                "recovery" => "Toparlanma durumumu açıklar mısın?",
                _ => "Programımı açıklar mısın?",
            };

        try
        {
            var result = await _model.CompleteAsync(new NutritionAssistantModelRequest(
                Instructions,
                facts.Context,
                new[] { new NutritionAssistantModelMessage(NutritionAssistantMessageRole.User, question) }), cancellationToken);
            if (result.Refused)
            {
                return WithNote(fallback, "AI yanıt vermedi.");
            }

            var validation = CoachResponseValidator.Validate(result.Text, facts.AllowedExerciseIds, facts.CatalogNames, facts.AllowedWeights);
            if (!validation.IsValid)
            {
                _logger?.LogInformation("Training coach response rejected: {Reason}", validation.Reason);
                return WithNote(fallback, validation.Reason);
            }

            return new TrainingCoachResponse { Message = validation.Message!, Source = "ai", ReferencedExerciseIds = validation.ExerciseIds.ToList() };
        }
        catch (ExternalServiceUnavailableException)
        {
            return WithNote(fallback, "AI sağlayıcısı kullanılamadı.");
        }
    }

    private static TrainingCoachResponse WithNote(TrainingCoachResponse r, string? note) => new()
    {
        Message = r.Message,
        Source = r.Source,
        ReferencedExerciseIds = r.ReferencedExerciseIds,
        ValidationNote = note,
    };

    internal sealed record CoachFacts(
        string Context,
        string Fallback,
        IReadOnlySet<long> AllowedExerciseIds,
        IReadOnlyDictionary<long, string> CatalogNames,
        IReadOnlySet<decimal> AllowedWeights,
        List<long> FallbackExerciseIds);

    private async Task<CoachFacts> BuildFactsAsync(long userId, string topic, TrainingCoachRequest request, CancellationToken cancellationToken)
    {
        var catalogNames = await _unitOfWork.Repository<Exercise>().GetQueryable().Where(e => e.IsActive)
            .ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var allowed = new HashSet<long>();
        var weights = new HashSet<decimal>();
        var context = new StringBuilder();
        var fallback = new StringBuilder();
        var fallbackIds = new List<long>();

        void AddWeight(decimal? kg)
        {
            if (kg is decimal v && v > 0) weights.Add(Math.Round(v, 2));
        }

        TrainingProgramDto? program = topic == "changes" && request.ProgramId.HasValue
            ? await _programs.GetProgramAsync(userId, request.ProgramId.Value, cancellationToken)
            : await _programs.GetActiveAsync(userId, cancellationToken);

        if (program is not null)
        {
            context.AppendLine($"PROGRAM: V{program.Version} ({program.Status}), {program.SplitLabel}, odak {program.Focus}, blok {program.BlockWeeks} hafta" + (program.IsDeload ? ", deload" : "") + ".");
            foreach (var reason in program.SplitReasons) context.AppendLine("- Split gerekçesi: " + reason);
            foreach (var day in program.Days)
            {
                context.AppendLine($"GÜN {day.Title} ({day.DayOfWeek}, ~{day.EstimatedMinutes} dk):");
                foreach (var e in day.Exercises)
                {
                    allowed.Add(e.ExerciseId);
                    AddWeight(e.SuggestedWeightKg);
                    var reps = e.DurationSeconds is int sec ? $"{sec} sn" : $"{e.RepsMin}-{e.RepsMax} tekrar";
                    context.AppendLine($"  [{e.ExerciseId}] {e.Name}: {e.Sets} set x {reps}, hedef RIR {e.TargetRir}, dinlenme {e.RestSeconds} sn, {e.Mechanic}; ağırlık: {e.WeightInstruction}");
                }
            }
            foreach (var v in program.WeeklyVolume)
            {
                context.AppendLine($"HACİM {v.Label}: hedef {Fmt(v.TargetSets)}, doğrudan {Fmt(v.DirectSets)}, yardımcı {Fmt(v.IndirectSets)} set/hafta");
            }
            foreach (var c in program.Changes) context.AppendLine("DEĞİŞİKLİK: " + c.Message);
            foreach (var n in program.Notes) context.AppendLine("NOT: " + n);
        }
        else
        {
            context.AppendLine("PROGRAM: Kullanıcının aktif kişisel programı yok.");
        }

        if (topic is "progress" or "question" or "recovery")
        {
            var progress = await _programs.GetProgressAsync(userId, cancellationToken);
            context.AppendLine($"GELİŞİM: son 4 haftada {progress.CompletedSessions4Weeks}, son 12 haftada {progress.CompletedSessions12Weeks} antrenman.");
            foreach (var e in progress.Exercises.Take(15))
            {
                allowed.Add(e.ExerciseId);
                AddWeight(e.LastTopWeightKg);
                AddWeight(e.FirstTopWeightKg);
                AddWeight(e.LastEstimatedOneRepMax);
                AddWeight(e.FirstEstimatedOneRepMax);
                context.AppendLine($"  [{e.ExerciseId}] {e.Name}: {e.ExposureCount} seans, en yüksek ağırlık {Fmt(e.FirstTopWeightKg)} → {Fmt(e.LastTopWeightKg)} kg, tahmini 1RM {Fmt(e.FirstEstimatedOneRepMax)} → {Fmt(e.LastEstimatedOneRepMax)} kg, eğilim {e.Trend}");
            }
            foreach (var i in progress.Insights) context.AppendLine("YORUM: " + i.Message);
            if (topic == "progress")
            {
                fallback.AppendLine(progress.Insights.Count > 0
                    ? string.Join(" ", progress.Insights.Select(i => i.Message))
                    : "Henüz gelişim yorumu için yeterli kayıt yok. Antrenmanlarında ağırlık, tekrar ve RIR girdikçe burada gelişimini göreceksin.");
            }

            var recovery = await _programs.GetRecoveryAsync(userId, cancellationToken);
            context.AppendLine($"TOPARLANMA: {recovery.Status}. " + string.Join(" ", recovery.Signals));
            foreach (var r in recovery.Recommendations) context.AppendLine("TOPARLANMA ÖNERİSİ: " + r.Message);
            if (topic == "recovery")
            {
                fallback.AppendLine(recovery.Recommendations.Count > 0
                    ? string.Join(" ", recovery.Recommendations.Select(r => r.Message))
                    : "Toparlanma sinyallerin normal görünüyor. Antrenman sonunda kısa değerlendirmeyi doldurmaya devam et.");
            }
        }

        if (topic == "alternative" && request.PlanExerciseId.HasValue)
        {
            var alternatives = await _programs.GetAlternativesAsync(userId, request.PlanExerciseId.Value, cancellationToken);
            foreach (var a in alternatives)
            {
                allowed.Add(a.ExerciseId);
                context.AppendLine($"ALTERNATİF [{a.ExerciseId}] {a.Name}: {a.Reason}");
            }
            fallback.AppendLine(alternatives.Count > 0
                ? "Bu hareketin yerine şunları kullanabilirsin: " + string.Join("; ", alternatives.Select(a => $"{a.Name} ({a.Reason})"))
                : "Ekipmanına ve tercihlerine uygun bir alternatif bulunamadı.");
            fallbackIds.AddRange(alternatives.Select(a => a.ExerciseId));
        }

        if (fallback.Length == 0)
        {
            if (program is null)
            {
                fallback.Append("Henüz kişisel programın yok. Program oluşturduğunda hangi günlerde ne çalışacağını ve nedenini burada açıklarım.");
            }
            else if (topic == "changes")
            {
                fallback.Append(program.Changes.Count > 0 ? string.Join(" ", program.Changes.Select(c => c.Message)) : "Bu versiyonda değişiklik kaydı yok.");
            }
            else
            {
                fallback.Append($"Programın {program.SplitLabel}. {string.Join(" ", program.SplitReasons)} ");
                fallback.Append("Her egzersizde hedef tekrar aralığının üst sınırına tüm setlerde hedef RIR ile ulaştığında ağırlık küçük bir adımla artar. ");
                fallback.Append("Ağırlığı bilinmeyen hareketlerde ilk seans kalibrasyondur: hafif başlayıp uygun ağırlığı bulursun.");
            }
        }

        return new CoachFacts(context.ToString(), fallback.ToString().Trim(), allowed, catalogNames, weights, fallbackIds);
    }

    private static string Fmt(decimal? v) => v is decimal d ? d.ToString("0.##", CultureInfo.InvariantCulture) : "-";
}
