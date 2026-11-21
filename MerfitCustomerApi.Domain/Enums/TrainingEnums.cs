namespace MerfitCustomerApi.Domain.Entities.Enums;

/// <summary>
/// Bir egzersizin mekanik turu: birden fazla eklemi calistiran bilesik hareket veya tek eklemli izolasyon.
/// </summary>
public enum ExerciseMechanicType
{
    /// <summary>Bilesik (cok eklemli) hareket.</summary>
    Compound,
    /// <summary>Izolasyon (tek eklemli) hareket.</summary>
    Isolation
}

/// <summary>
/// Egzersizin temel hareket paterni. Ayni gunde ayni paternin gereksiz tekrarini onlemek icin kullanilir.
/// </summary>
public enum MovementPattern
{
    /// <summary>Yatay itis (bench press, sinav).</summary>
    HorizontalPush,
    /// <summary>Dikey itis (overhead press).</summary>
    VerticalPush,
    /// <summary>Yatay cekis (row).</summary>
    HorizontalPull,
    /// <summary>Dikey cekis (pull-up, lat pulldown).</summary>
    VerticalPull,
    /// <summary>Squat paterni.</summary>
    Squat,
    /// <summary>Kalca mentesesi (deadlift, hip thrust).</summary>
    Hinge,
    /// <summary>Tek bacak / lunge.</summary>
    Lunge,
    /// <summary>Tasima.</summary>
    Carry,
    /// <summary>Govde / core.</summary>
    Core,
    /// <summary>Tek eklem izolasyon (curl, extension, raise).</summary>
    Isolation,
    /// <summary>Kardiyo / kondisyon.</summary>
    Conditioning
}

/// <summary>
/// Bir egzersizde kas grubunun rolu.
/// </summary>
public enum ExerciseMuscleRole
{
    /// <summary>Ana kas (Exercise.PrimaryMuscleGroupId ile ayni anlamda).</summary>
    Primary,
    /// <summary>Yardimci kas. Haftalik hacimde kesirli sayilir.</summary>
    Secondary
}

/// <summary>
/// Bir antrenman planinin (program versiyonunun) yasam dongusu durumu.
/// </summary>
public enum TrainingProgramStatus
{
    /// <summary>Motorun onerdigi, kullanici onayi bekleyen versiyon. Aktif programi degistirmez.</summary>
    Proposed,
    /// <summary>Kullanicinin uyguladigi aktif versiyon.</summary>
    Active,
    /// <summary>Yerini yeni versiyona birakmis versiyon. Gunleri, egzersizleri ve oturumlari korunur.</summary>
    Archived,
    /// <summary>Kullanicinin reddettigi oneri.</summary>
    Rejected
}

/// <summary>
/// Planin nasil olusturuldugu.
/// </summary>
public enum TrainingProgramSource
{
    /// <summary>Katalogdaki hazir antrenmanlardan olusturulan mevcut (eski) plan.</summary>
    Catalog,
    /// <summary>Egzersiz duzeyinde calisan kisisellestirilmis antrenman motoru.</summary>
    Engine,
    /// <summary>Kullanicinin elle yaptigi degisiklikten olusan versiyon.</summary>
    Manual
}

/// <summary>
/// Kullanicinin antrenman odagi. Profil hedefinden (FitnessGoal) daha ayrintilidir; bos ise hedeften turetilir.
/// </summary>
public enum TrainingFocus
{
    /// <summary>Kas kazanimi.</summary>
    Hypertrophy,
    /// <summary>Kuvvet kazanimi.</summary>
    Strength,
    /// <summary>Yag kaybi.</summary>
    FatLoss,
    /// <summary>Genel fitness.</summary>
    GeneralFitness,
    /// <summary>Dayaniklilik.</summary>
    Endurance
}

/// <summary>
/// Programdaki bir egzersiz icin baslangic agirliginin nasil belirlenecegi.
/// </summary>
public enum WeightGuidanceType
{
    /// <summary>Kullanicinin gecmis set kayitlarindan hesaplandi.</summary>
    FromHistory,
    /// <summary>Gecmis yok: ilk seansta kademeli yuk belirleme (kalibrasyon). Agirlik uretilmez.</summary>
    RampUp,
    /// <summary>Vucut agirligi egzersizi; harici agirlik gerekmez.</summary>
    Bodyweight
}
