using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// SaveChanges hatalarini ayirt eder. Business katmani Npgsql'e referans vermedigi icin
/// PostgresException'in SqlState / ConstraintName ozellikleri reflection ile okunur.
/// </summary>
public static class DbExceptionClassifier
{
    private const string UniqueViolationSqlState = "23505";

    /// <summary>Hata bir unique index ihlali mi (orn. ayni kullanici adi)? Ihlal edilen index adini da dondurur.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex, out string? constraintName)
    {
        constraintName = null;
        var inner = ex.InnerException;
        if (inner is null) return false;

        var type = inner.GetType();
        if (type.GetProperty("SqlState")?.GetValue(inner) as string != UniqueViolationSqlState) return false;

        constraintName = type.GetProperty("ConstraintName")?.GetValue(inner) as string;
        return true;
    }
}
