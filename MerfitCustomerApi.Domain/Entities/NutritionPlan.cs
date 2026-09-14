using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Bir kullanicinin kisisellestirilmis beslenme programini temsil eder. Meal/MealItem'in
/// (gercekte tuketilen ogunlerin loglandigi tablolar) tersine, bu ve alt entity'leri yalnizca
/// PLANLANAN menuyu tasir; NutritionPlanGenerator tarafindan uretilir.
/// </summary>
public class NutritionPlan : BaseEntity
{
    /// <summary>Planin ait oldugu kullanicinin kimligi.</summary>
    public long UserId { get; set; }

    /// <summary>Planin adi.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Planin aciklamasi.</summary>
    public string? Description { get; set; }

    /// <summary>Planin baslangic tarihi.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Planin bitis tarihi (belirtilmemisse suresiz).</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Planin aktif olup olmadigi. Kullanici basina ayni anda yalnizca bir aktif plan olabilir.</summary>
    public bool IsActive { get; set; }

    /// <summary>Planin yapay zeka tarafindan uretilip uretilmedigi. FAZ 2'de her zaman false'tur.</summary>
    public bool IsAiGenerated { get; set; }
}

/// <summary>
/// Bir NutritionPlan'in tek bir gunune (haftanin gunune) ait planini temsil eder.
/// </summary>
public class NutritionPlanDay : BaseEntity
{
    /// <summary>Iliskili beslenme planinin kimligi.</summary>
    public long NutritionPlanId { get; set; }

    /// <summary>Planin uygulandigi haftanin gunu.</summary>
    public DayOfWeek DayOfWeek { get; set; }
}

/// <summary>
/// Bir NutritionPlanDay icindeki tek bir ogunu (kahvalti/ogle/aksam/ara ogun) temsil eder.
/// </summary>
public class NutritionPlanMeal : BaseEntity
{
    /// <summary>Iliskili beslenme plani gununun kimligi.</summary>
    public long NutritionPlanDayId { get; set; }

    /// <summary>Ogunun turu (Breakfast/Lunch/Dinner/Snack) - mevcut MealType enum'u kullanilir.</summary>
    public MerfitCustomerApi.Domain.Entities.Enums.MealType MealType { get; set; }

    /// <summary>Ogunun goruntulenecek adi (orn. "Kahvaltı").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Ogun icin onerilen saat (yalnizca saat/dakika, planlama amacli).</summary>
    public TimeOnly? PlannedTime { get; set; }

    /// <summary>Ogune ait ek notlar.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Bir NutritionPlanMeal icindeki tek bir besin ogesini temsil eder. Food tablosundaki
/// besin degerleri zaman icinde degisebilecegi icin, plan olusturuldugu andaki makrolar
/// burada SNAPSHOT (kopya) olarak saklanir; FoodId yalnizca referans/gosterim amaciyla tutulur.
/// </summary>
public class NutritionPlanMealItem : BaseEntity
{
    /// <summary>Iliskili beslenme plani ogununun kimligi.</summary>
    public long NutritionPlanMealId { get; set; }

    /// <summary>Kaynak besinin (Food) kimligi.</summary>
    public long FoodId { get; set; }

    /// <summary>Onerilen miktar (Food.ServingSize birimine gore, orn. gram).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Miktarin birimi (orn. "g", "ml", "adet").</summary>
    public string ServingUnit { get; set; } = string.Empty;

    /// <summary>Plan olusturuldugu andaki (snapshot) kalori degeri.</summary>
    public decimal Calories { get; set; }

    /// <summary>Plan olusturuldugu andaki (snapshot) protein degeri (gram).</summary>
    public decimal Protein { get; set; }

    /// <summary>Plan olusturuldugu andaki (snapshot) karbonhidrat degeri (gram).</summary>
    public decimal Carbs { get; set; }

    /// <summary>Plan olusturuldugu andaki (snapshot) yag degeri (gram).</summary>
    public decimal Fat { get; set; }
}
