using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Antrenman sonu kisa degerlendirme (oturum basina en fazla bir kayit). Tum puanlar 1-5 olcegindedir
/// ve hepsi istege baglidir; kullanici yalnizca istedigi sorulari yanitlayabilir.
/// </summary>
public class WorkoutSessionFeedback : BaseEntity
{
    /// <summary>
    /// Gecerli en dusuk puan.
    /// </summary>
    public const int MinScore = 1;

    /// <summary>
    /// Gecerli en yuksek puan.
    /// </summary>
    public const int MaxScore = 5;

    /// <summary>
    /// Degerlendirilen oturumun kimligi.
    /// </summary>
    public long WorkoutSessionId { get; set; }

    /// <summary>
    /// Kullanicinin kimligi.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Antrenman zorlugu (1 = cok kolay, 5 = cok zor).
    /// </summary>
    public int? Difficulty { get; set; }

    /// <summary>
    /// Genel yorgunluk (1 = dinc, 5 = cok yorgun).
    /// </summary>
    public int? Fatigue { get; set; }

    /// <summary>
    /// Kaslarin toparlanma durumu (1 = hic toparlanmamis, 5 = tamamen toparlanmis).
    /// </summary>
    public int? Recovery { get; set; }

    /// <summary>
    /// Programdan memnuniyet (1 = hic, 5 = cok).
    /// </summary>
    public int? Satisfaction { get; set; }

    /// <summary>
    /// Hareketlerde agri / rahatsizlik oldu mu?
    /// </summary>
    public bool HasDiscomfort { get; set; }

    /// <summary>
    /// Rahatsizlik verilen egzersiz kimlikleri (virgulle ayrilmis).
    /// </summary>
    public string? DiscomfortExerciseIds { get; set; }

    /// <summary>
    /// Istege bagli kisa not.
    /// </summary>
    public string? Note { get; set; }
}
