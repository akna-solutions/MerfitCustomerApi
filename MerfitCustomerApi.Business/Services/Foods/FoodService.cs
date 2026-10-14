using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Foods;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.FoodAnalysis;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;

namespace MerfitCustomerApi.Business.Services.Foods;

/// <summary>IFoodService'in varsayilan implementasyonu. Mevcut Food entity'sini oldugu gibi kullanir.</summary>
public class FoodService : IFoodService
{
    /// <summary>Barkodla eklenen urunlerin referans porsiyonu: degerler 100 g/ml basinadir.</summary>
    private const decimal BarcodeServingSize = 100m;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFoodProductLookup _productLookup;

    public FoodService(IUnitOfWork unitOfWork, IFoodProductLookup productLookup)
    {
        _unitOfWork = unitOfWork;
        _productLookup = productLookup;
    }

    public async Task<PagedResult<CustomerFoodListItemDto>> SearchFoodsAsync(CustomerFoodListRequest request, CancellationToken cancellationToken = default)
    {
        // Fotograf analizinden kullaniciya ozel olusturulan tahmini besinler ortak aramada gosterilmez.
        var query = _unitOfWork.Repository<Food>().GetQueryable()
            .Where(f => f.Brand == null || f.Brand != FoodAnalysisService.EstimatedFoodBrand);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(f => f.Name.ToLower().Contains(term));
        }

        query = query.OrderBy(f => f.Name);

        var paged = await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        var items = paged.Items.Select(ToDto).ToList();

        return PagedResult<CustomerFoodListItemDto>.Create(items, paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<CustomerFoodListItemDto> GetByBarcodeAsync(long userId, string barcode, CancellationToken cancellationToken = default)
    {
        var code = NormalizeBarcode(barcode);

        var existing = await _unitOfWork.Repository<Food>()
            .FirstOrDefaultAsync(f => f.Barcode == code, cancellationToken);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        var product = await _productLookup.FindByBarcodeAsync(code, cancellationToken)
            ?? throw new NotFoundException("Bu barkoda ait urun ya da besin degeri bulunamadi.");

        // Urun katalogga siniflandirilmamis (Category = null) olarak eklenir; boylece yalnizca gunluk
        // kayitta kullanilir, admin siniflandirana kadar kisisel beslenme planina girmez.
        var food = new Food
        {
            Name = product.Name,
            Barcode = code,
            Brand = product.Brand,
            ServingSize = BarcodeServingSize,
            ServingUnit = product.Unit,
            Calories = product.CaloriesPer100,
            Protein = product.ProteinPer100,
            Carbs = product.CarbsPer100,
            Fat = product.FatPer100,
            Fiber = product.FiberPer100,
            Sugar = product.SugarPer100,
            Sodium = product.SodiumMgPer100,
            ImageUrl = product.ImageUrl,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = userId,
        };
        await _unitOfWork.Repository<Food>().AddAsync(food, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(food);
    }

    /// <summary>EAN-8, UPC-A, EAN-13 ve ITF-14 barkodlarini kabul eder (8-14 rakam).</summary>
    private static string NormalizeBarcode(string barcode)
    {
        var code = barcode?.Trim() ?? string.Empty;
        if (code.Length is < 8 or > 14 || !code.All(char.IsAsciiDigit))
        {
            throw new AppValidationException(nameof(barcode), "Barkod 8-14 haneli bir sayi olmalidir.");
        }

        return code;
    }

    private static CustomerFoodListItemDto ToDto(Food f) => new()
    {
        Id = f.Id,
        Name = f.Name,
        Brand = f.Brand,
        ServingSize = f.ServingSize,
        ServingUnit = f.ServingUnit,
        Calories = (int)Math.Round(f.Calories),
        Protein = f.Protein,
        Carbs = f.Carbs,
        Fat = f.Fat,
        ImageUrl = f.ImageUrl,
    };
}
