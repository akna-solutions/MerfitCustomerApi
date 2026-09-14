namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "PersonalizationJobProcessing" bolumune karsilik gelen ayarlar.
/// PersonalizationJobProcessor ve PersonalizationBackgroundWorker tarafindan kullanilir.
/// </summary>
public class PersonalizationJobProcessingOptions
{
    public const string SectionName = "PersonalizationJobProcessing";

    /// <summary>
    /// Bir job'un en fazla kac kez denenebilecegi. AttemptCount bu degere ulastiginda
    /// job artik Pending'e donmez, kalici olarak Failed durumunda kalir (sonsuz retry engeli).
    /// </summary>
    public int MaxAttemptCount { get; set; } = 3;

    /// <summary>
    /// Worker'in her turda veritabanindan kac aday Pending job cekecegi. Ilk uygun (baska bir
    /// worker tarafindan henuz claim edilmemis) olan islenir; kalanlar bir sonraki tura birakilir.
    /// </summary>
    public int ClaimBatchSize { get; set; } = 5;

    /// <summary>Worker'in, isleyecek Pending job bulamadiginda tekrar kontrol etmeden once bekleyecegi sure.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Worker'in bir job islendikten hemen sonra, kuyrukta baska job olup olmadigini kontrol etmeden once bekleyecegi (kisa) sure.</summary>
    public TimeSpan BackToBackDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Uygulama baslarken worker'in ilk kontrolden once bekleyecegi baslangic gecikmesi.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(5);
}
