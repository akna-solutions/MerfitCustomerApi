using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Haftalik gun sayisi, deneyim, seans suresi, hedef ve toparlanmaya gore split secer. Kurallar:
/// - Haftalik gun: baslangic seviyesinde en fazla 4, herkes icin en fazla 6 (toparlanma). En az 2.
/// - 2 gun: Full Body (her kas haftada 2 kez).
/// - 3 gun: Full Body; ileri seviye ve seans 45 dk+ ise Upper / Lower / Full (daha fazla hacim alani).
/// - 4 gun: Upper / Lower x2.
/// - 5 gun: Upper / Lower + Push / Pull / Legs (5 gunluk kisisellestirilmis split).
/// - 6 gun: Push / Pull / Legs x2.
/// - Yuksek yorgunluk sinyali varsa bir gun azaltilir (en az 2).
/// Herkese sabit atama yapilmaz; her karar gerekcesiyle kaydedilir.
/// </summary>
public static class SplitSelector
{
    private static readonly MuscleRegion[] Upper =
        { MuscleRegion.Chest, MuscleRegion.Back, MuscleRegion.Shoulders, MuscleRegion.Biceps, MuscleRegion.Triceps };
    private static readonly MuscleRegion[] Lower =
        { MuscleRegion.Quadriceps, MuscleRegion.Hamstrings, MuscleRegion.Glutes, MuscleRegion.Calves, MuscleRegion.Core };
    private static readonly MuscleRegion[] Push = { MuscleRegion.Chest, MuscleRegion.Shoulders, MuscleRegion.Triceps };
    private static readonly MuscleRegion[] Pull = { MuscleRegion.Back, MuscleRegion.Biceps, MuscleRegion.Core };
    private static readonly MuscleRegion[] Legs =
        { MuscleRegion.Quadriceps, MuscleRegion.Hamstrings, MuscleRegion.Glutes, MuscleRegion.Calves };
    private static readonly MuscleRegion[] Full =
    {
        MuscleRegion.Quadriceps, MuscleRegion.Chest, MuscleRegion.Back, MuscleRegion.Hamstrings, MuscleRegion.Shoulders,
        MuscleRegion.Glutes, MuscleRegion.Biceps, MuscleRegion.Triceps, MuscleRegion.Calves, MuscleRegion.Core,
    };

    public static SplitDecision Select(TrainingProfile profile, TrainingEngineOptions options, bool highFatigue = false)
    {
        var reasons = new List<string>();
        var days = Math.Clamp(profile.RequestedDaysPerWeek, 2, 7);

        if (days > options.MaxDaysPerWeek)
        {
            reasons.Add($"Haftada {days} gün yerine {options.MaxDaysPerWeek} gün planlandı: her kas grubunun toparlanması için en az bir tam dinlenme günü bırakıldı.");
            days = options.MaxDaysPerWeek;
        }

        if (profile.Experience == ExperienceLevel.Beginner && days > options.BeginnerMaxDaysPerWeek)
        {
            reasons.Add($"Başlangıç seviyesinde haftada {days} yerine {options.BeginnerMaxDaysPerWeek} gün planlandı. Bu seviyede gelişim için {options.BeginnerMaxDaysPerWeek} gün yeterli, kalan günlerde yürüyüş gibi hafif aktivite önerilir.");
            days = options.BeginnerMaxDaysPerWeek;
        }

        if (highFatigue && days > 2)
        {
            reasons.Add("Son geri bildirimlerde yorgunluk yüksek olduğu için haftalık antrenman günü bir azaltıldı.");
            days--;
        }

        var available = profile.AvailableDays.Count;
        if (available >= 2 && available < days)
        {
            reasons.Add($"Müsait olduğun {available} gün kadar antrenman planlandı.");
            days = available;
        }

        days = Math.Max(2, days);

        SplitDecision Build(string type, string label, params (string Code, string Title, string Focus, MuscleRegion[] Regions)[] d)
            => new(type, label, d.Select(x => new DayTemplate(x.Code, x.Title, x.Focus, x.Regions)).ToList(), reasons, d.Length);

        switch (days)
        {
            case 2:
                reasons.Add("Haftada 2 günde her kas grubunu iki kez çalıştırmak için Full Body seçildi.");
                return Build("FullBody", "Full Body (2 gün)",
                    ("A", "Tüm Vücut A", "FullBody", Full), ("B", "Tüm Vücut B", "FullBody", Full));
            case 3 when profile.Experience == ExperienceLevel.Advanced && profile.SessionMinutes >= 45
                        && profile.Focus is TrainingFocus.Hypertrophy or TrainingFocus.FatLoss:
                reasons.Add("İleri seviye ve 45 dk+ seans süresiyle 3 günde daha fazla hacim alanı için Upper / Lower / Full seçildi.");
                return Build("UpperLowerFull", "Üst / Alt / Tüm Vücut",
                    ("U", "Üst Vücut", "Upper", Upper), ("L", "Alt Vücut", "Lower", Lower), ("F", "Tüm Vücut", "FullBody", Full));
            case 3:
                reasons.Add("Haftada 3 günde her kas grubunu haftada 3 kez, seans başına az hacimle çalıştırmak için Full Body seçildi.");
                return Build("FullBody", "Full Body (3 gün)",
                    ("A", "Tüm Vücut A", "FullBody", Full), ("B", "Tüm Vücut B", "FullBody", Full), ("C", "Tüm Vücut C", "FullBody", Full));
            case 4:
                reasons.Add("Haftada 4 günde her kası haftada 2 kez çalıştırmak için Upper / Lower seçildi.");
                return Build("UpperLower", "Üst / Alt (4 gün)",
                    ("UA", "Üst Vücut A", "Upper", Upper), ("LA", "Alt Vücut A", "Lower", Lower),
                    ("UB", "Üst Vücut B", "Upper", Upper), ("LB", "Alt Vücut B", "Lower", Lower));
            case 5:
                reasons.Add("Haftada 5 gün için Upper / Lower ve Push / Pull / Legs birleşimi seçildi; her kas haftada 2 kez çalışıyor.");
                return Build("UpperLowerPushPullLegs", "Üst / Alt + İtiş / Çekiş / Bacak (5 gün)",
                    ("U", "Üst Vücut", "Upper", Upper), ("L", "Alt Vücut", "Lower", Lower),
                    ("PU", "İtiş", "Push", Push), ("PL", "Çekiş", "Pull", Pull), ("LG", "Bacak", "Legs", Legs));
            default:
                reasons.Add("Haftada 6 günde her kası haftada 2 kez çalıştırmak için Push / Pull / Legs x2 seçildi.");
                return Build("PushPullLegs", "İtiş / Çekiş / Bacak x2",
                    ("PA", "İtiş A", "Push", Push), ("PLA", "Çekiş A", "Pull", Pull), ("LGA", "Bacak A", "Legs", Legs),
                    ("PB", "İtiş B", "Push", Push), ("PLB", "Çekiş B", "Pull", Pull), ("LGB", "Bacak B", "Legs", Legs));
        }
    }
}
