namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Yag Kaybi hedefi secilirken kullanicinin olcumlerine gore vucut yag orani dusuk aralikta gorundugunde
/// ve kullanici uyariyi acikca onaylamadiginda firlatilir. API bunu 409 ve <see cref="Code"/> hata koduyla
/// dondurur; istemci uyariyi gosterip kullanici "devam et" derse istegi onay alaniyla tekrar gonderir.
/// Onay olmadan hedef kaydedilmez.
/// </summary>
public class LowBodyFatConfirmationRequiredException : ConflictException
{
    /// <summary>Yanittaki Errors listesinde istemcinin aradigi sabit kod.</summary>
    public const string Code = "LOW_BODY_FAT_CONFIRMATION_REQUIRED";

    public LowBodyFatConfirmationRequiredException(string message) : base(message)
    {
    }
}
