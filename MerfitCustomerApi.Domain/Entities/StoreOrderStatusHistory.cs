using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>Siparis durum/odeme gecmisi (store.OrderStatusHistory); siparis takibi ve denetim icin yalnizca eklenir.</summary>
public class StoreOrderStatusHistory : BaseEntity
{
    public long OrderId { get; set; }

    public StoreOrderStatus Status { get; set; }

    public StorePaymentStatus PaymentStatus { get; set; }

    /// <summary>"customer" | "admin" | "system" | "payment-provider".</summary>
    public string ChangedBy { get; set; } = string.Empty;

    public string? Note { get; set; }
}
