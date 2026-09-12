using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Foods;

public class AdminFoodListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? Brand { get; set; }
    public decimal ServingSize { get; set; }
    public string ServingUnit { get; set; } = string.Empty;
    public decimal Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminFoodDetailDto : AdminFoodListItemDto
{
    public decimal? Fiber { get; set; }
    public decimal? Sugar { get; set; }
    public decimal? Sodium { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminFoodListRequest : PagedRequest
{
    public string? Barcode { get; set; }
    public string? Brand { get; set; }
}

public class AdminUpsertFoodRequest
{
    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(300, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Barcode { get; set; }

    [StringLength(200)]
    public string? Brand { get; set; }

    [Range(0.01, 100000, ErrorMessage = "Porsiyon miktari 0'dan buyuk olmalidir.")]
    public decimal ServingSize { get; set; }

    [Required(ErrorMessage = "Porsiyon birimi zorunludur.")]
    [StringLength(20)]
    public string ServingUnit { get; set; } = string.Empty;

    [Range(0, 10000)]
    public decimal Calories { get; set; }

    [Range(0, 1000)]
    public decimal Protein { get; set; }

    [Range(0, 1000)]
    public decimal Carbs { get; set; }

    [Range(0, 1000)]
    public decimal Fat { get; set; }

    [Range(0, 1000)]
    public decimal? Fiber { get; set; }

    [Range(0, 1000)]
    public decimal? Sugar { get; set; }

    [Range(0, 100000)]
    public decimal? Sodium { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir gorsel URL'i giriniz.")]
    public string? ImageUrl { get; set; }
}
