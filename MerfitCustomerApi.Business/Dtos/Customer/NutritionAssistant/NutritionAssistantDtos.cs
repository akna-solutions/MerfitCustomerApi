using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;

/// <summary>
/// Beslenme ekranindaki dinamik degerlendirme. Hedeflerle gerceklesen tuketimin karsilastirmasindan
/// sunucuda uretilir (NutritionInsightEvaluator); ayni esikler hem kartta hem sohbette kullanilir.
/// </summary>
public class NutritionInsightDto
{
    /// <summary>
    /// "noData" | "noTargets" | "goalsCompleted" | "caloriesExceeded" | "proteinBehind" | "fatsHigh" |
    /// "carbsHigh" | "caloriesMostlyDone" | "carbsOnTrack" | "inProgress".
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Kullaniciya gosterilecek Turkce kisa degerlendirme.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Degerlendirmeye bagli hizli soru (sohbet acilinca ilk oneri olarak gosterilir).</summary>
    public string? SuggestedQuestion { get; set; }
}

/// <summary>Sohbetteki tek bir mesaj. Icerik duz metindir; istemci HTML olarak yorumlamamalidir.</summary>
public class NutritionAssistantMessageDto
{
    /// <summary>Kalici mesaj kimligi; sunucunun o an urettigi acilis mesajinda null'dir.</summary>
    public long? Id { get; set; }

    /// <summary>"user" | "assistant".</summary>
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>GET /api/nutrition-assistant/conversation yaniti.</summary>
public class NutritionAssistantConversationDto
{
    /// <summary>O gun icin sohbet yoksa null; ilk mesajla olusur.</summary>
    public long? ConversationId { get; set; }

    /// <summary>Sohbetin ait oldugu gun (yyyy-MM-dd).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Saglayici yapilandirilmamis/kapaliysa false; istemci mesaj alanini devre disi birakir.</summary>
    public bool AssistantAvailable { get; set; }

    /// <summary>Gercek verilerle hazirlanan acilis degerlendirmesi (kaydedilmez, her acilista guncel).</summary>
    public NutritionAssistantMessageDto Greeting { get; set; } = new();

    /// <summary>Kayitli gecmis mesajlar, eskiden yeniye.</summary>
    public List<NutritionAssistantMessageDto> Messages { get; set; } = new();

    public List<string> SuggestedQuestions { get; set; } = new();

    public NutritionInsightDto Insight { get; set; } = new();

    /// <summary>Son 24 saatte gonderilebilecek kalan mesaj sayisi.</summary>
    public int RemainingMessages { get; set; }
}

/// <summary>POST /api/nutrition-assistant/chat govdesi.</summary>
public class NutritionAssistantChatRequest
{
    [Required(ErrorMessage = "Mesaj bos olamaz.")]
    [StringLength(4000, ErrorMessage = "Mesaj cok uzun.")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Var olan sohbetin kimligi; sunucu sohbetin bu kullaniciya ait oldugunu dogrular.</summary>
    public long? ConversationId { get; set; }

    /// <summary>Beslenme ekraninda secili gun ("yyyy-MM-dd"); verilmezse bugun.</summary>
    public DateOnly? Date { get; set; }
}

/// <summary>POST /api/nutrition-assistant/chat yaniti.</summary>
public class NutritionAssistantChatResponse
{
    /// <summary>Mesajin yazildigi sohbet. Gun degistiyse istekteki kimlikten farkli (yeni gunun sohbeti) olabilir.</summary>
    public long ConversationId { get; set; }

    public DateOnly Date { get; set; }

    public NutritionAssistantMessageDto UserMessage { get; set; } = new();

    /// <summary>Asistan yaniti (duz metin).</summary>
    public NutritionAssistantMessageDto Message { get; set; } = new();

    public List<string> SuggestedQuestions { get; set; } = new();

    public int RemainingMessages { get; set; }
}
