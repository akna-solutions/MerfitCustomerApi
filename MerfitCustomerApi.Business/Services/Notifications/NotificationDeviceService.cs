using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Notifications;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Notifications;

/// <summary>
/// Push token kayit/guncelleme/pasiflestirme. Bir token ayni anda tek bir kullaniciya aittir (unique
/// index): ayni cihazda baska hesapla giris yapildiginda satir yeni kullaniciya tasinir, boylece onceki
/// hesabin bildirimleri bu cihaza gitmez. Ayni kurulum (InstallationId) yeni token aldiginda eski
/// token pasiflestirilir.
/// </summary>
public class NotificationDeviceService : INotificationDeviceService
{
    public const string ReasonLogout = "Logout";
    public const string ReasonPermissionRevoked = "PermissionRevoked";
    public const string ReasonReplaced = "Replaced";
    public const string ReasonReassigned = "Reassigned";
    public const string ReasonDeviceNotRegistered = "DeviceNotRegistered";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPushNotificationService _pushService;

    public NotificationDeviceService(IUnitOfWork unitOfWork, IPushNotificationService pushService)
    {
        _unitOfWork = unitOfWork;
        _pushService = pushService;
    }

    public async Task<CustomerDeviceRegistrationDto> RegisterAsync(long userId, RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var token = request.Token.Trim();
        if (!_pushService.IsValidToken(token))
        {
            throw new AppValidationException(nameof(request.Token), "Geçersiz push token.");
        }

        var platform = request.Platform.Trim().ToLowerInvariant() switch
        {
            "ios" => Platform.IOS,
            "android" => Platform.Android,
            _ => throw new AppValidationException(nameof(request.Platform), "Platform 'ios' veya 'android' olmalı."),
        };

        var installationId = string.IsNullOrWhiteSpace(request.InstallationId) ? null : request.InstallationId.Trim();
        var now = DateTime.UtcNow;
        var repo = _unitOfWork.Repository<UserDevice>();

        var device = await repo.GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(d => d.DeviceToken == token, cancellationToken);

        if (device is null)
        {
            device = new UserDevice { DeviceToken = token, CreatedAt = now };
            await repo.AddAsync(device, cancellationToken);
        }
        else
        {
            device.UpdatedAt = now;
        }

        device.UserId = userId;
        device.Platform = platform;
        device.InstallationId = installationId;
        device.DeviceName = request.DeviceName;
        device.AppVersion = request.AppVersion;
        device.OsVersion = request.OsVersion;
        device.IsActive = true;
        device.LastSeenAt = now;
        device.DeactivatedAt = null;
        device.DeactivationReason = null;

        if (installationId is not null)
        {
            // Ayni kurulumun eski token'lari (token yenilendi veya baska hesaba gecildi).
            var stale = await repo.GetQueryable(asNoTracking: false)
                .Where(d => d.InstallationId == installationId && d.DeviceToken != token && d.IsActive)
                .ToListAsync(cancellationToken);
            foreach (var old in stale)
            {
                Deactivate(old, ReasonReplaced, now);
            }
        }

        if (request.TimeZone is not null && NotificationClock.IsValidTimeZone(request.TimeZone))
        {
            await NotificationService.SetTimeZoneAsync(_unitOfWork, userId, request.TimeZone, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CustomerDeviceRegistrationDto
        {
            Id = device.Id,
            Platform = platform == Platform.IOS ? "ios" : "android",
            IsActive = device.IsActive,
            LastSeenAt = device.LastSeenAt,
        };
    }

    public async Task DeactivateAsync(long userId, DeactivateDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var token = request.Token.Trim();
        var device = await _unitOfWork.Repository<UserDevice>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(d => d.DeviceToken == token && d.UserId == userId, cancellationToken);
        if (device is null || !device.IsActive)
        {
            return;
        }

        var reason = request.Reason == ReasonPermissionRevoked ? ReasonPermissionRevoked : ReasonLogout;
        Deactivate(device, reason, DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public static void Deactivate(UserDevice device, string reason, DateTime now)
    {
        device.IsActive = false;
        device.DeactivatedAt = now;
        device.DeactivationReason = reason;
        device.UpdatedAt = now;
    }
}
