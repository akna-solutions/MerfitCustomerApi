using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Notifications;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Notifications;

/// <summary>
/// INotificationService'in varsayilan implementasyonu.
///
/// Ayarlarin kaynagi NotificationType enum'udur (bkz. <see cref="Catalog"/>). Eski
/// UserNotificationSetting tablosunda sutunu olan turler (WorkoutReminder, GoalReminder, ...)
/// o sutunlarda saklanmaya devam eder - boylece GET/PUT /api/profile'daki "notifications" alani ile
/// bu uc nokta her zaman ayni degeri gosterir. Sutunu olmayan turler UserNotificationTypeSetting
/// tablosunda tutulur.
/// </summary>
public class NotificationService : INotificationService
{
    public const string CategoryWorkout = "workout";
    public const string CategoryNutrition = "nutrition";
    public const string CategoryGeneral = "general";

    private sealed record TypeInfo(
        NotificationType Type,
        string Title,
        string Description,
        string Category,
        bool DefaultEnabled,
        bool IsLocked = false,
        Func<UserNotificationSetting, bool>? GetLegacy = null,
        Action<UserNotificationSetting, bool>? SetLegacy = null);

    /// <summary>Her NotificationType icin kullaniciya gosterilen ad, aciklama ve kategori.</summary>
    private static readonly IReadOnlyList<TypeInfo> Catalog = new[]
    {
        new TypeInfo(NotificationType.WorkoutReminder, "Antrenman Hatırlatmaları", "Programındaki antrenman günlerinde hatırlatma.", CategoryWorkout, true,
            GetLegacy: s => s.WorkoutReminders, SetLegacy: (s, v) => s.WorkoutReminders = v),
        new TypeInfo(NotificationType.GoalReminder, "Günlük Hedef Hatırlatıcısı", "Günlük hedefini tamamlaman için hatırlatma.", CategoryGeneral, true,
            GetLegacy: s => s.DailyGoalReminder, SetLegacy: (s, v) => s.DailyGoalReminder = v),
        new TypeInfo(NotificationType.StreakReminder, "Seri Hatırlatıcısı", "Serini kaybetmek üzereyken haber verir.", CategoryWorkout, true,
            GetLegacy: s => s.StreakReminder, SetLegacy: (s, v) => s.StreakReminder = v),
        new TypeInfo(NotificationType.ProgressUpdate, "İlerleme Güncellemeleri", "Haftalık ilerleme özetleri.", CategoryWorkout, true,
            GetLegacy: s => s.ProgressUpdates, SetLegacy: (s, v) => s.ProgressUpdates = v),
        new TypeInfo(NotificationType.LeaderboardUpdate, "Liderlik Tablosu Güncellemeleri", "Sıralamandaki değişiklikler.", CategoryGeneral, true,
            GetLegacy: s => s.LeaderboardUpdates, SetLegacy: (s, v) => s.LeaderboardUpdates = v),
        new TypeInfo(NotificationType.Achievement, "Başarımlar", "Yeni bir başarım kazandığında.", CategoryGeneral, true),
        new TypeInfo(NotificationType.Reward, "Ödüller", "Kazandığın ödüller ve puanlar.", CategoryGeneral, true),
        new TypeInfo(NotificationType.NutritionReminder, "Beslenme Hatırlatmaları", "Öğün ve beslenme programı hatırlatmaları.", CategoryNutrition, true),
        new TypeInfo(NotificationType.WaterReminder, "Su Hatırlatmaları", "Günlük su hedefin için hatırlatma.", CategoryNutrition, true),
        new TypeInfo(NotificationType.Subscription, "Üyelik ve Abonelik", "MB FIT Plus üyeliğinle ilgili bilgiler.", CategoryGeneral, true),
        new TypeInfo(NotificationType.Marketing, "Kampanyalar ve Ürün Haberleri", "Yeni özellikler ve kampanyalar.", CategoryGeneral, false,
            GetLegacy: s => s.ProductUpdates, SetLegacy: (s, v) => s.ProductUpdates = v),
        new TypeInfo(NotificationType.System, "Sistem Bildirimleri", "Hesabın ve güvenliğinle ilgili önemli bilgiler.", CategoryGeneral, true, IsLocked: true),
    };

    private static readonly Dictionary<NotificationType, TypeInfo> CatalogByType = Catalog.ToDictionary(t => t.Type);

    private readonly IUnitOfWork _unitOfWork;

    public NotificationService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<CustomerNotificationDto>> GetNotificationsAsync(long userId, CustomerNotificationListRequest request, CancellationToken cancellationToken = default)
    {
        var types = await GetVisibleTypesAsync(userId, request.Category, cancellationToken);
        var now = DateTime.UtcNow;

        var query = _unitOfWork.Repository<Notification>()
            .GetQueryable()
            .Where(n => n.UserId == userId
                && types.Contains(n.Type)
                && (n.ExpiresAt == null || n.ExpiresAt > now))
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id);

        var page = await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
        var items = page.Items.Select(n => new CustomerNotificationDto
        {
            Id = n.Id,
            Type = n.Type.ToString(),
            Category = CategoryOf(n.Type),
            Title = n.Title,
            Body = n.Body,
            ImageUrl = n.ImageUrl,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt,
        }).ToList();

        return PagedResult<CustomerNotificationDto>.Create(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<CustomerNotificationUnreadCountResponse> GetUnreadCountAsync(long userId, string? category, CancellationToken cancellationToken = default)
    {
        var types = await GetVisibleTypesAsync(userId, category, cancellationToken);
        var now = DateTime.UtcNow;

        var count = await _unitOfWork.Repository<Notification>()
            .GetQueryable()
            .CountAsync(n => n.UserId == userId
                && !n.IsRead
                && types.Contains(n.Type)
                && (n.ExpiresAt == null || n.ExpiresAt > now), cancellationToken);

        return new CustomerNotificationUnreadCountResponse { Count = count };
    }

    public async Task MarkAsReadAsync(long userId, long notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _unitOfWork.Repository<Notification>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);
        if (notification is null)
        {
            throw new NotFoundException(nameof(Notification), notificationId);
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            notification.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAllAsReadAsync(long userId, string? category, CancellationToken cancellationToken = default)
    {
        var types = ResolveCategoryTypes(category);
        var unread = await _unitOfWork.Repository<Notification>()
            .GetQueryable(asNoTracking: false)
            .Where(n => n.UserId == userId && !n.IsRead && types.Contains(n.Type))
            .ToListAsync(cancellationToken);
        if (unread.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
            notification.UpdatedAt = now;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<CustomerNotificationTypeSettingDto>> GetSettingsAsync(long userId, CancellationToken cancellationToken = default)
    {
        var enabled = await GetEnabledMapAsync(userId, cancellationToken);
        return Catalog.Select(info => new CustomerNotificationTypeSettingDto
        {
            Type = info.Type.ToString(),
            Title = info.Title,
            Description = info.Description,
            Category = info.Category,
            IsEnabled = enabled[info.Type],
            IsLocked = info.IsLocked,
        }).ToList();
    }

    public async Task<List<CustomerNotificationTypeSettingDto>> UpdateSettingsAsync(long userId, UpdateNotificationTypeSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var changes = new List<(TypeInfo Info, bool IsEnabled)>();
        foreach (var item in request.Settings)
        {
            if (!Enum.TryParse<NotificationType>(item.Type, ignoreCase: true, out var type)
                || !Enum.IsDefined(type)
                || !CatalogByType.TryGetValue(type, out var info))
            {
                throw new AppValidationException(nameof(item.Type), $"Bilinmeyen bildirim turu: {item.Type}");
            }

            if (!info.IsLocked)
            {
                changes.Add((info, item.IsEnabled));
            }
        }

        if (changes.Any(c => c.Info.SetLegacy is not null))
        {
            var legacy = await GetOrCreateLegacySettingsAsync(userId, cancellationToken);
            foreach (var (info, isEnabled) in changes.Where(c => c.Info.SetLegacy is not null))
            {
                info.SetLegacy!(legacy, isEnabled);
            }
            legacy.UpdatedAt = DateTime.UtcNow;
        }

        var typeChanges = changes.Where(c => c.Info.SetLegacy is null).ToList();
        if (typeChanges.Count > 0)
        {
            var repo = _unitOfWork.Repository<UserNotificationTypeSetting>();
            var existing = await repo.GetQueryable(asNoTracking: false)
                .Where(s => s.UserId == userId)
                .ToListAsync(cancellationToken);

            foreach (var (info, isEnabled) in typeChanges)
            {
                var row = existing.FirstOrDefault(s => s.Type == info.Type);
                if (row is null)
                {
                    row = new UserNotificationTypeSetting
                    {
                        UserId = userId,
                        Type = info.Type,
                        IsEnabled = isEnabled,
                        CreatedAt = DateTime.UtcNow,
                    };
                    existing.Add(row);
                    await repo.AddAsync(row, cancellationToken);
                }
                else
                {
                    row.IsEnabled = isEnabled;
                    row.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetSettingsAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Kayit yoksa, GET /api/profile'in kayitsiz kullaniciya gosterdigi varsayilanlarla olusturur
    /// (bkz. ProfileService.BuildResponseAsync) - aksi halde tek bir ayari degistirmek digerlerini
    /// sessizce kapatirdi.
    /// </summary>
    private async Task<UserNotificationSetting> GetOrCreateLegacySettingsAsync(long userId, CancellationToken cancellationToken)
    {
        var settings = await _unitOfWork.Repository<UserNotificationSetting>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new UserNotificationSetting
        {
            UserId = userId,
            WorkoutReminders = true,
            DailyGoalReminder = true,
            StreakReminder = true,
            ProgressUpdates = true,
            LeaderboardUpdates = true,
            ProductUpdates = false,
            CreatedAt = DateTime.UtcNow,
        };
        await _unitOfWork.Repository<UserNotificationSetting>().AddAsync(settings, cancellationToken);
        return settings;
    }

    private async Task<Dictionary<NotificationType, bool>> GetEnabledMapAsync(long userId, CancellationToken cancellationToken)
    {
        var legacy = await _unitOfWork.Repository<UserNotificationSetting>()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        var rows = await _unitOfWork.Repository<UserNotificationTypeSetting>()
            .GetQueryable()
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);

        return Catalog.ToDictionary(info => info.Type, info =>
        {
            if (info.IsLocked) return true;
            if (info.GetLegacy is not null) return legacy is null ? info.DefaultEnabled : info.GetLegacy(legacy);
            return rows.FirstOrDefault(r => r.Type == info.Type)?.IsEnabled ?? info.DefaultEnabled;
        });
    }

    /// <summary>Kategoriye uyan ve kullanicinin kapatmadigi turler.</summary>
    private async Task<List<NotificationType>> GetVisibleTypesAsync(long userId, string? category, CancellationToken cancellationToken)
    {
        var enabled = await GetEnabledMapAsync(userId, cancellationToken);
        return ResolveCategoryTypes(category).Where(t => enabled[t]).ToList();
    }

    private static List<NotificationType> ResolveCategoryTypes(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Catalog.Select(t => t.Type).ToList();
        }

        var normalized = category.Trim().ToLowerInvariant();
        if (normalized is not (CategoryWorkout or CategoryNutrition or CategoryGeneral))
        {
            throw new AppValidationException(nameof(category), "Kategori 'workout', 'nutrition' veya 'general' olabilir.");
        }

        return Catalog.Where(t => t.Category == normalized).Select(t => t.Type).ToList();
    }

    private static string CategoryOf(NotificationType type) =>
        CatalogByType.TryGetValue(type, out var info) ? info.Category : CategoryGeneral;
}
