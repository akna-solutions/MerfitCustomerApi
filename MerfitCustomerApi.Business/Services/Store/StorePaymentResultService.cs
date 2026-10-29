using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// Dogrulanmis odeme sonuclarinin siparise uygulanmasi (bkz. IStorePaymentProvider). Kurallar:
/// - Yalnizca "Pending" odeme degisir; ayni sonucun tekrari (webhook yeniden denemesi) siparisi degistirmez.
/// - Basarili: PaymentStatus = Paid, PaidAt yazilir.
/// - Basarisiz: PaymentStatus = Failed; siparis henuz kargolanmadiysa iptal edilir ve ayrilan stok iade edilir.
/// </summary>
public class StorePaymentResultService : IStorePaymentResultService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StorePaymentResultService> _logger;
    private readonly TimeProvider _timeProvider;

    public StorePaymentResultService(IUnitOfWork unitOfWork, ILogger<StorePaymentResultService> logger, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ApplyAsync(StorePaymentResult result, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<StoreOrder>().GetQueryable(asNoTracking: false)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == result.OrderNumber, cancellationToken)
            ?? throw new NotFoundException("Sipariş bulunamadı.");

        if (order.PaymentStatus != StorePaymentStatus.Pending)
        {
            _logger.LogInformation("Odeme sonucu yok sayildi: siparis odeme durumu zaten {PaymentStatus}. OrderId: {OrderId}", order.PaymentStatus, order.Id);
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var releaseStock = false;
        order.PaymentProvider = result.Provider;
        order.PaymentReference = result.PaymentReference;
        order.UpdatedAt = now;
        if (result.Succeeded)
        {
            order.PaymentStatus = StorePaymentStatus.Paid;
            order.PaidAt = now;
        }
        else
        {
            order.PaymentStatus = StorePaymentStatus.Failed;
            if (order.Status is StoreOrderStatus.Received or StoreOrderStatus.Preparing)
            {
                order.Status = StoreOrderStatus.Cancelled;
                order.CancelledAt = now;
                order.CancelReason = "Ödeme başarısız oldu.";
                releaseStock = true;
            }
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.Repository<StoreOrderStatusHistory>().AddAsync(new StoreOrderStatusHistory
            {
                OrderId = order.Id,
                Status = order.Status,
                PaymentStatus = order.PaymentStatus,
                ChangedBy = "payment-provider",
                Note = result.Succeeded ? "Ödeme doğrulandı." : $"Ödeme başarısız: {Truncate(result.FailureReason)}",
                CreatedAt = now,
            }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (releaseStock) await StoreStock.ReleaseAsync(_unitOfWork, order.Items, now, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Siparis bu sirada iptal edildi/guncellendi; sonuc tekrar islenirse guncel duruma gore karar verilir.
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.ClearTracking();
            throw new ConflictException("Sipariş durumu bu sırada değişti.");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.ClearTracking();
            throw;
        }
    }

    private static string Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "bilinmiyor" : value.Length > 200 ? value[..200] : value;
}
