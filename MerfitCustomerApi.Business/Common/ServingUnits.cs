namespace MerfitCustomerApi.Business.Common;

/// <summary>Food.ServingUnit degerleri icin yardimcilar.</summary>
public static class ServingUnits
{
    private static readonly HashSet<string> WeighableUnits = new(StringComparer.OrdinalIgnoreCase) { "g", "gr", "gram", "ml" };

    /// <summary>Birim gram/ml gibi serbestce tartilabilir mi (adet, dilim, kase degil)?</summary>
    public static bool IsWeighable(string? unit) => unit is not null && WeighableUnits.Contains(unit.Trim());
}
