using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Bir egzersiz icin gereken ekipman (Equipment tablosuna FK). Exercise.EquipmentType serbest metninin
/// yapilandirilmis karsiligidir. Kayit yoksa motor EquipmentType metnini Equipment adlariyla eslemeyi dener.
/// </summary>
public class ExerciseEquipment : BaseEntity
{
    /// <summary>
    /// Egzersizin kimligi.
    /// </summary>
    public long ExerciseId { get; set; }

    /// <summary>
    /// Gereken ekipmanin kimligi.
    /// </summary>
    public long EquipmentId { get; set; }
}
