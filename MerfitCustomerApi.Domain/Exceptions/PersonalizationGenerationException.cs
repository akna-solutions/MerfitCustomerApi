namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// WorkoutPlanGenerator/NutritionPlanGenerator, kullanicinin kriterlerine uyan yeterli veri
/// (aday antrenman, besin vb.) bulamadiginda firlatir. PersonalizationJobProcessor bu istisnayi
/// yakalayip ilgili PersonalizationJob'u Failed durumuna gecirir ve mesaji ErrorMessage'a yazar;
/// controller katmanina kadar ulasmaz.
/// </summary>
public class PersonalizationGenerationException : Exception
{
    public PersonalizationGenerationException(string message) : base(message)
    {
    }
}
