namespace MerfitCustomerApi.Domain.Entities.Enums;

/// <summary>Beslenme asistani sohbetindeki bir mesajin yazari.</summary>
public enum NutritionAssistantMessageRole
{
    /// <summary>Kullanicinin yazdigi mesaj.</summary>
    User,

    /// <summary>Asistanin (yapay zeka) yaniti.</summary>
    Assistant
}
