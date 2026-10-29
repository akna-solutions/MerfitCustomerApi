using System.Security.Cryptography;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// IStoreOrderService'in varsayilan implementasyonu.
/// Siparis olusturma tek transaction'dir: sepet sunucuda yeniden degerlendirilir (fiyat, varyant, stok), stok urun
/// basina tek atomik UPDATE ile dusurulur, siparis + kalemler + durum gecmisi yazilir ve sepet bosaltilir. Herhangi bir
/// adim basarisiz olursa hepsi geri alinir. Odeme siparisten ayridir: kapida odeme/havale "Pending" baslar; hicbir
/// yol siparisi dogrulanmamis bir odemeyle "Paid" yapmaz.
/// </summary>
public class StoreOrderService : IStoreOrderService
{
    public const string PriceChangedCode = "PRICE_CHANGED";
    public const string CartChangedCode = "CART_CHANGED";
    public const string OutOfStockCode = "OUT_OF_STOCK";

    private const string OrderNumberAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int MaxOrderNumberAttempts = 3;

    private readonly IUnitOfWork _unitOfWork;
    private readonly StoreOptions _options;
    private readonly IReadOnlyList<IStorePaymentProvider> _paymentProviders;
    private readonly ILogger<StoreOrderService> _logger;
    private readonly TimeProvider _timeProvider;

    public StoreOrderService(
        IUnitOfWork unitOfWork,
        IOptions<StoreOptions> options,
        IEnumerable<IStorePaymentProvider> paymentProviders,
        ILogger<StoreOrderService> logger,
        TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _paymentProviders = paymentProviders.ToList();
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public Task<StoreCheckoutOptionsDto> GetCheckoutOptionsAsync(CancellationToken cancellationToken = default)
    {
        var methods = new List<StorePaymentMethodDto>
        {
            new()
            {
                Method = StoreCodes.Method(StorePaymentMethod.CashOnDelivery),
                Label = "Kapıda ödeme",
                Description = "Ödemeyi teslimatta kuryeye yaparsın.",
                Available = _options.CashOnDeliveryEnabled,
                UnavailableReason = _options.CashOnDeliveryEnabled ? null : "Kapıda ödeme şu an kullanılamıyor.",
            },
            new()
            {
                Method = StoreCodes.Method(StorePaymentMethod.BankTransfer),
                Label = "Havale / EFT",
                Description = "Sipariş sonrası gösterilen hesaba, açıklamaya sipariş numarasını yazarak ödersin. Ödeme onaylanınca sipariş hazırlanır.",
                Available = _options.BankTransfer.IsConfigured,
                UnavailableReason = _options.BankTransfer.IsConfigured ? null : "Havale ile ödeme henüz aktif değil.",
            },
            new()
            {
                Method = StoreCodes.Method(StorePaymentMethod.Card),
                Label = "Kart ile ödeme",
                Description = "Güvenli ödeme sayfasında kartla ödersin.",
                Available = _paymentProviders.Count > 0,
                UnavailableReason = _paymentProviders.Count > 0 ? null : "Kartla online ödeme henüz aktif değil.",
            },
        };

        return Task.FromResult(new StoreCheckoutOptionsDto
        {
            PaymentMethods = methods,
            FreeShippingThreshold = _options.FreeShippingThreshold,
            ShippingCost = StorePricing.Round(_options.ShippingCost),
            MaxQuantityPerLine = _options.MaxQuantityPerLine,
            Currency = _options.Currency,
        });
    }

    public async Task<(StoreOrderDto Order, bool Created)> CreateAsync(long userId, CreateStoreOrderRequest request, CancellationToken cancellationToken = default)
    {
        var method = StoreCodes.ParseMethod(request.PaymentMethod)
            ?? throw new AppValidationException(nameof(request.PaymentMethod), "Geçersiz ödeme yöntemi.");
        EnsureMethodAvailable(method);
        var key = request.IdempotencyKey.Trim();

        var previous = await FindByKeyAsync(userId, key, cancellationToken);
        if (previous is not null)
        {
            return (await GetAsync(userId, previous.Value, cancellationToken), false);
        }

        var address = await _unitOfWork.Repository<UserAddress>().GetQueryable()
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Teslimat adresi bulunamadı.");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var orderId = await CreateOnceAsync(userId, request, method, key, address, cancellationToken);
                return (await GetAsync(userId, orderId, cancellationToken), true);
            }
            catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out var constraint))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _unitOfWork.ClearTracking();

                // Ayni anahtarla eszamanli gelen ikinci istek: ilk siparis doner (stok dususu geri alindi).
                var existing = await FindByKeyAsync(userId, key, cancellationToken);
                if (existing is not null)
                {
                    return (await GetAsync(userId, existing.Value, cancellationToken), false);
                }

                if (attempt >= MaxOrderNumberAttempts || constraint?.Contains("OrderNumber", StringComparison.OrdinalIgnoreCase) != true)
                {
                    throw;
                }

                _logger.LogWarning("Siparis numarasi cakismasi; yeniden deneniyor ({Attempt}).", attempt);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                _unitOfWork.ClearTracking();
                throw;
            }
        }
    }

    private async Task<long> CreateOnceAsync(long userId, CreateStoreOrderRequest request, StorePaymentMethod method, string key,
        UserAddress address, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var cartIds = _unitOfWork.Repository<Cart>().GetQueryable(asNoTracking: false).Where(c => c.UserId == userId).Select(c => c.Id);
        var cartItems = await _unitOfWork.Repository<CartItem>().GetQueryable(asNoTracking: false)
            .Where(i => cartIds.Contains(i.CartId))
            .ToListAsync(cancellationToken);
        if (cartItems.Count == 0)
        {
            throw new AppValidationException("cart", "Sepetin boş.");
        }

        var lines = await StoreCartEvaluator.EvaluateAsync(_unitOfWork, cartItems, _options, cancellationToken);
        var unavailable = lines.FirstOrDefault(l => !l.IsAvailable);
        if (unavailable is not null)
        {
            var name = unavailable.Product?.Name ?? "Bir ürün";
            throw new ConflictException($"{name}: {unavailable.UnavailableReason} Sepetini kontrol edip tekrar dene.", CartChangedCode);
        }

        var totals = StoreCartEvaluator.Totals(lines, _options);
        if (request.ExpectedTotal.HasValue && StorePricing.Round(request.ExpectedTotal.Value) != totals.Total)
        {
            throw new ConflictException(
                $"Sepet tutarın güncellendi. Yeni toplam: {totals.Total:N2} ₺. Lütfen kontrol edip tekrar onayla.", PriceChangedCode);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // Kilitlenme (deadlock) riskini azaltmak icin urunler hep ayni sirayla (Id) dusurulur.
        foreach (var group in lines.GroupBy(l => l.Product!.Id).OrderBy(g => g.Key))
        {
            var quantity = group.Sum(l => l.Item.Quantity);
            if (!await StoreStock.TryReserveAsync(_unitOfWork, group.Key, quantity, now, cancellationToken))
            {
                throw new ConflictException($"{group.First().Product!.Name} için yeterli stok kalmadı. Sepetini kontrol edip tekrar dene.", OutOfStockCode);
            }
        }

        var order = new StoreOrder
        {
            OrderNumber = NewOrderNumber(now),
            UserId = userId,
            Status = StoreOrderStatus.Received,
            PaymentStatus = StorePaymentStatus.Pending,
            PaymentMethod = method,
            Subtotal = totals.Subtotal,
            DiscountAmount = totals.Discount,
            ShippingCost = totals.ShippingCost,
            TotalAmount = totals.Total,
            Currency = _options.Currency,
            ShippingAddressSnapshot = StoreJson.Write(StoreAddressSnapshot.From(address)),
            IdempotencyKey = key,
            CreatedAt = now,
            CreatedUser = userId,
            Items = lines.Select(l => new StoreOrderItem
            {
                ProductId = l.Product!.Id,
                ProductNameSnapshot = l.Product.Name,
                SkuSnapshot = l.Product.Sku,
                BrandSnapshot = l.Product.Brand,
                ImageUrlSnapshot = StoreUrls.SafeImage(l.ImageUrl),
                VariantLabel = string.IsNullOrEmpty(l.Variant!.Label) ? null : l.Variant.Label,
                VariantKey = l.Variant.Key,
                UnitListPrice = l.UnitListPrice,
                UnitPrice = l.UnitPrice,
                Quantity = l.Item.Quantity,
                DiscountAmount = StorePricing.Round((l.UnitListPrice - l.UnitPrice) * l.Item.Quantity),
                LineTotal = StorePricing.Round(l.UnitPrice * l.Item.Quantity),
                CreatedAt = now,
            }).ToList(),
        };

        await _unitOfWork.Repository<StoreOrder>().AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _unitOfWork.Repository<StoreOrderStatusHistory>().AddAsync(new StoreOrderStatusHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            ChangedBy = "customer",
            Note = "Sipariş oluşturuldu.",
            CreatedAt = now,
            CreatedUser = userId,
        }, cancellationToken);
        _unitOfWork.Repository<CartItem>().RemoveRange(cartItems);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        _logger.LogInformation("MB Shop siparisi olusturuldu. OrderId: {OrderId}, Kalem: {Lines}", order.Id, order.Items.Count);
        return order.Id;
    }

    public async Task<PagedResult<StoreOrderListItemDto>> GetListAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<StoreOrder>().GetQueryable().Where(o => o.UserId == userId);
        var total = await query.LongCountAsync(cancellationToken);
        var items = _unitOfWork.Repository<StoreOrderItem>().GetQueryable();
        var rows = await query
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(o => new
            {
                Order = o,
                Count = items.Where(i => i.OrderId == o.Id).Sum(i => (int?)i.Quantity) ?? 0,
                Names = items.Where(i => i.OrderId == o.Id).OrderBy(i => i.Id).Select(i => i.ProductNameSnapshot).Take(3).ToList(),
            })
            .ToListAsync(cancellationToken);

        return PagedResult<StoreOrderListItemDto>.Create(rows.Select(r =>
        {
            var dto = new StoreOrderListItemDto();
            StoreOrderMapper.FillListItem(dto, r.Order, r.Count, r.Names);
            return dto;
        }).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<StoreOrderDto> GetAsync(long userId, long orderId, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<StoreOrder>().GetQueryable()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Sipariş bulunamadı.");
        var history = await _unitOfWork.Repository<StoreOrderStatusHistory>().GetQueryable()
            .Where(h => h.OrderId == orderId)
            .OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);
        return ToDto(order, history);
    }

    public async Task<StoreOrderDto> CancelAsync(long userId, long orderId, CancelStoreOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<StoreOrder>().GetQueryable(asNoTracking: false)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Sipariş bulunamadı.");
        if (order.Status != StoreOrderStatus.Received)
        {
            throw new ConflictException(order.Status == StoreOrderStatus.Cancelled
                ? "Bu sipariş zaten iptal edilmiş."
                : "Hazırlanmaya başlayan sipariş uygulamadan iptal edilemez. Destek ekibiyle iletişime geçebilirsin.");
        }

        var now = UtcNow;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            order.Status = StoreOrderStatus.Cancelled;
            order.PaymentStatus = order.PaymentStatus == StorePaymentStatus.Paid ? StorePaymentStatus.RefundPending : StorePaymentStatus.Cancelled;
            order.CancelledAt = now;
            order.CancelReason = string.IsNullOrWhiteSpace(request.Reason) ? "Müşteri tarafından iptal edildi." : request.Reason.Trim();
            order.UpdatedAt = now;
            order.UpdatedUser = userId;
            await _unitOfWork.Repository<StoreOrderStatusHistory>().AddAsync(new StoreOrderStatusHistory
            {
                OrderId = order.Id,
                Status = order.Status,
                PaymentStatus = order.PaymentStatus,
                ChangedBy = "customer",
                Note = order.CancelReason,
                CreatedAt = now,
                CreatedUser = userId,
            }, cancellationToken);

            // Once siparis satiri guncellenir (Status eszamanlilik belirteci): eszamanli ikinci iptal burada durur ve
            // stok iki kez iade edilmez.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await StoreStock.ReleaseAsync(_unitOfWork, order.Items, now, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.ClearTracking();
            throw new ConflictException("Sipariş durumu bu sırada değişti. Lütfen sayfayı yenile.");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.ClearTracking();
            throw;
        }

        return await GetAsync(userId, orderId, cancellationToken);
    }

    private void EnsureMethodAvailable(StorePaymentMethod method)
    {
        var available = method switch
        {
            StorePaymentMethod.CashOnDelivery => _options.CashOnDeliveryEnabled,
            StorePaymentMethod.BankTransfer => _options.BankTransfer.IsConfigured,
            // Kart: odeme saglayicisi entegrasyonu tamamlanana kadar siparis kabul edilmez (sahte odeme yok).
            StorePaymentMethod.Card => false,
            _ => false,
        };
        if (!available)
        {
            throw new AppValidationException("paymentMethod", "Bu ödeme yöntemi şu an kullanılamıyor.");
        }
    }

    private async Task<long?> FindByKeyAsync(long userId, string key, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<StoreOrder>().GetQueryable()
            .Where(o => o.UserId == userId && o.IdempotencyKey == key)
            .Select(o => (long?)o.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>"MBF-yyMMdd-XXXXXX": tarih + 6 karakter kriptografik rastgele (karisan 0/O, 1/I yok).</summary>
    private static string NewOrderNumber(DateTime now)
    {
        Span<char> suffix = stackalloc char[6];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = OrderNumberAlphabet[RandomNumberGenerator.GetInt32(OrderNumberAlphabet.Length)];
        }

        return $"MBF-{now:yyMMdd}-{new string(suffix)}";
    }

    private StoreOrderDto ToDto(StoreOrder order, List<StoreOrderStatusHistory> history)
    {
        var items = order.Items.OrderBy(i => i.Id).ToList();
        var dto = new StoreOrderDto
        {
            Subtotal = order.Subtotal,
            Discount = order.DiscountAmount,
            ShippingCost = order.ShippingCost,
            ShippingAddress = StoreOrderMapper.Address(order.ShippingAddressSnapshot),
            Items = items.Select(StoreOrderMapper.Item).ToList(),
            History = history.Select(h => new StoreOrderHistoryDto
            {
                Status = StoreCodes.Status(h.Status),
                PaymentStatus = StoreCodes.Payment(h.PaymentStatus),
                Note = h.Note,
                CreatedAt = DateTime.SpecifyKind(h.CreatedAt, DateTimeKind.Utc),
            }).ToList(),
            CanCancel = order.Status == StoreOrderStatus.Received,
            CancelledAt = order.CancelledAt is null ? null : DateTime.SpecifyKind(order.CancelledAt.Value, DateTimeKind.Utc),
            CancelReason = order.CancelReason,
        };
        StoreOrderMapper.FillListItem(dto, order, items.Sum(i => i.Quantity), items.Take(3).Select(i => i.ProductNameSnapshot).ToList());

        if (order.PaymentMethod == StorePaymentMethod.BankTransfer
            && order.PaymentStatus == StorePaymentStatus.Pending
            && order.Status != StoreOrderStatus.Cancelled
            && _options.BankTransfer.IsConfigured)
        {
            dto.BankTransfer = new StoreBankTransferInfoDto
            {
                Iban = _options.BankTransfer.Iban!.Trim(),
                AccountName = _options.BankTransfer.AccountName!.Trim(),
                BankName = _options.BankTransfer.BankName,
                Reference = order.OrderNumber,
            };
        }

        return dto;
    }
}
