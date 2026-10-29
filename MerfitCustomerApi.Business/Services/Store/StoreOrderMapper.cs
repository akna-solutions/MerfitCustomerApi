using System.Text.Json.Serialization;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>Siparis enum'larinin istemci kodlari ("received", "cash_on_delivery" ...) ve DTO donusumleri.</summary>
public static class StoreCodes
{
    public static string Status(StoreOrderStatus status) => status switch
    {
        StoreOrderStatus.Received => "received",
        StoreOrderStatus.Preparing => "preparing",
        StoreOrderStatus.Shipped => "shipped",
        StoreOrderStatus.Delivered => "delivered",
        _ => "cancelled",
    };

    public static string Payment(StorePaymentStatus status) => status switch
    {
        StorePaymentStatus.Pending => "pending",
        StorePaymentStatus.Paid => "paid",
        StorePaymentStatus.Failed => "failed",
        StorePaymentStatus.RefundPending => "refund_pending",
        StorePaymentStatus.Refunded => "refunded",
        _ => "cancelled",
    };

    public static string Method(StorePaymentMethod method) => method switch
    {
        StorePaymentMethod.CashOnDelivery => "cash_on_delivery",
        StorePaymentMethod.BankTransfer => "bank_transfer",
        _ => "card",
    };

    public static StorePaymentMethod? ParseMethod(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "cash_on_delivery" => StorePaymentMethod.CashOnDelivery,
        "bank_transfer" => StorePaymentMethod.BankTransfer,
        "card" => StorePaymentMethod.Card,
        _ => null,
    };
}

/// <summary>Siparise yazilan adres kopyasi (store.Order.ShippingAddressSnapshot).</summary>
public sealed class StoreAddressSnapshot
{
    public string Title { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? PostalCode { get; set; }

    /// <summary>Kopyanin alindigi adres kaydi (yalnizca iz icin; adres sonradan silinebilir).</summary>
    [JsonPropertyName("sourceAddressId")]
    public long SourceAddressId { get; set; }

    public static StoreAddressSnapshot From(UserAddress a) => new()
    {
        Title = a.Title,
        RecipientName = a.RecipientName,
        PhoneNumber = a.PhoneNumber,
        City = a.City,
        District = a.District,
        AddressLine = a.AddressLine,
        PostalCode = a.PostalCode,
        SourceAddressId = a.Id,
    };
}

internal static class StoreOrderMapper
{
    public static StoreOrderAddressDto Address(string snapshotJson)
    {
        StoreAddressSnapshot? snapshot = null;
        try
        {
            snapshot = System.Text.Json.JsonSerializer.Deserialize<StoreAddressSnapshot>(snapshotJson, StoreJson.Options);
        }
        catch (System.Text.Json.JsonException)
        {
        }

        snapshot ??= new StoreAddressSnapshot();
        return new StoreOrderAddressDto
        {
            Title = snapshot.Title,
            RecipientName = snapshot.RecipientName,
            PhoneNumber = snapshot.PhoneNumber,
            City = snapshot.City,
            District = snapshot.District,
            AddressLine = snapshot.AddressLine,
            PostalCode = snapshot.PostalCode,
        };
    }

    public static void FillListItem(StoreOrderListItemDto dto, StoreOrder o, int itemCount, List<string> names)
    {
        dto.Id = o.Id;
        dto.OrderNumber = o.OrderNumber;
        dto.CreatedAt = DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc);
        dto.Status = StoreCodes.Status(o.Status);
        dto.PaymentStatus = StoreCodes.Payment(o.PaymentStatus);
        dto.PaymentMethod = StoreCodes.Method(o.PaymentMethod);
        dto.Total = o.TotalAmount;
        dto.Currency = o.Currency;
        dto.ItemCount = itemCount;
        dto.ItemNames = names;
    }

    public static StoreOrderItemDto Item(StoreOrderItem i) => new()
    {
        ProductId = i.ProductId,
        Name = i.ProductNameSnapshot,
        Brand = i.BrandSnapshot,
        Sku = i.SkuSnapshot,
        ImageUrl = StoreUrls.SafeImage(i.ImageUrlSnapshot),
        VariantLabel = string.IsNullOrWhiteSpace(i.VariantLabel) ? null : i.VariantLabel,
        Quantity = i.Quantity,
        UnitPrice = i.UnitPrice,
        UnitOriginalPrice = i.UnitListPrice,
        DiscountAmount = i.DiscountAmount,
        LineTotal = i.LineTotal,
    };
}
