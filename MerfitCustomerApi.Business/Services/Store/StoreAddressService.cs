using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// IStoreAddressService'in varsayilan implementasyonu. Kullanicinin ilk adresi otomatik varsayilan olur; varsayilan
/// silinirse en son eklenen adres varsayilan yapilir. Baska kullanicinin adresi icin NotFound doner.
/// </summary>
public class StoreAddressService : IStoreAddressService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly StoreOptions _options;
    private readonly TimeProvider _timeProvider;

    public StoreAddressService(IUnitOfWork unitOfWork, IOptions<StoreOptions> options, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<List<StoreAddressDto>> GetAsync(long userId, CancellationToken cancellationToken = default)
    {
        var rows = await _unitOfWork.Repository<UserAddress>().GetQueryable()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<StoreAddressDto> CreateAsync(long userId, UpsertStoreAddressRequest request, CancellationToken cancellationToken = default)
    {
        var values = Normalize(request);
        var repo = _unitOfWork.Repository<UserAddress>();
        var existing = await repo.GetQueryable(asNoTracking: false).Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        if (existing.Count >= _options.MaxAddressesPerUser)
        {
            throw new ConflictException($"En fazla {_options.MaxAddressesPerUser} adres kaydedebilirsin.");
        }

        var entity = new UserAddress { UserId = userId, CreatedAt = UtcNow };
        Apply(entity, values);
        entity.IsDefault = request.IsDefault || existing.Count == 0;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (entity.IsDefault) await ClearDefaultAsync(existing, cancellationToken);
            await repo.AddAsync(entity, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            _unitOfWork.ClearTracking();
            throw new ConflictException("Adreslerin aynı anda güncellendi. Lütfen tekrar dene.");
        }

        return ToDto(entity);
    }

    public async Task<StoreAddressDto> UpdateAsync(long userId, long id, UpsertStoreAddressRequest request, CancellationToken cancellationToken = default)
    {
        var values = Normalize(request);
        var all = await _unitOfWork.Repository<UserAddress>().GetQueryable(asNoTracking: false).Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        var entity = all.FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("Adres bulunamadı.");

        Apply(entity, values);
        entity.UpdatedAt = UtcNow;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (request.IsDefault && !entity.IsDefault)
            {
                await ClearDefaultAsync(all, cancellationToken);
                entity.IsDefault = true;
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            _unitOfWork.ClearTracking();
            throw new ConflictException("Adreslerin aynı anda güncellendi. Lütfen tekrar dene.");
        }

        return ToDto(entity);
    }

    public async Task DeleteAsync(long userId, long id, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<UserAddress>();
        var all = await repo.GetQueryable(asNoTracking: false).Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        var entity = all.FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("Adres bulunamadı.");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (entity.IsDefault)
        {
            // Siparisler adresin kopyasini tuttugu icin silmek gecmis siparisleri etkilemez.
            var next = all.Where(a => a.Id != id).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefault();
            if (next is not null)
            {
                next.IsDefault = true;
                next.UpdatedAt = UtcNow;
            }
        }

        await _unitOfWork.CommitTransactionAsync(cancellationToken);
    }

    public async Task<StoreAddressDto> SetDefaultAsync(long userId, long id, CancellationToken cancellationToken = default)
    {
        var all = await _unitOfWork.Repository<UserAddress>().GetQueryable(asNoTracking: false).Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        var entity = all.FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("Adres bulunamadı.");
        if (entity.IsDefault) return ToDto(entity);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        await ClearDefaultAsync(all, cancellationToken);
        entity.IsDefault = true;
        entity.UpdatedAt = UtcNow;
        await _unitOfWork.CommitTransactionAsync(cancellationToken);
        return ToDto(entity);
    }

    /// <summary>
    /// Filtreli benzersiz index (kullanici basina tek varsayilan) ihlal edilmesin diye once eski varsayilan kaldirilip
    /// kaydedilir; yeni varsayilan ayni transaction icinde sonra yazilir.
    /// </summary>
    private async Task ClearDefaultAsync(IEnumerable<UserAddress> addresses, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var address in addresses.Where(a => a.IsDefault))
        {
            address.IsDefault = false;
            address.UpdatedAt = UtcNow;
            changed = true;
        }

        if (changed) await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private sealed record AddressValues(string Title, string RecipientName, string Phone, string City, string District, string AddressLine, string? PostalCode);

    private static AddressValues Normalize(UpsertStoreAddressRequest request)
    {
        var phone = NormalizePhone(request.PhoneNumber);
        if (phone is null)
        {
            throw new AppValidationException(nameof(request.PhoneNumber), "Geçerli bir cep telefonu girin (5XX XXX XX XX).");
        }

        var name = Collapse(request.RecipientName);
        if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            throw new AppValidationException(nameof(request.RecipientName), "Ad ve soyadınızı girin.");
        }

        return new AddressValues(Collapse(request.Title), name, phone, Collapse(request.City), Collapse(request.District),
            request.AddressLine.Trim(), string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim());
    }

    /// <summary>Turkiye cep telefonu: 05XXXXXXXXX, 5XXXXXXXXX veya +905XXXXXXXXX; 5XXXXXXXXX olarak saklanir.</summary>
    public static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("90") && digits.Length == 12) digits = digits[2..];
        if (digits.StartsWith('0')) digits = digits[1..];
        return digits.Length == 10 && digits[0] == '5' ? digits : null;
    }

    private static string Collapse(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static void Apply(UserAddress entity, AddressValues values)
    {
        entity.Title = values.Title;
        entity.RecipientName = values.RecipientName;
        entity.PhoneNumber = values.Phone;
        entity.City = values.City;
        entity.District = values.District;
        entity.AddressLine = values.AddressLine;
        entity.PostalCode = values.PostalCode;
    }

    internal static StoreAddressDto ToDto(UserAddress a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        RecipientName = a.RecipientName,
        PhoneNumber = a.PhoneNumber,
        City = a.City,
        District = a.District,
        AddressLine = a.AddressLine,
        PostalCode = a.PostalCode,
        IsDefault = a.IsDefault,
    };
}
