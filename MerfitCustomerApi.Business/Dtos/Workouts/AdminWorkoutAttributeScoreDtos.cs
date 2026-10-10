using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Workouts;

/// <summary>Bir antrenmanin tek bir ozellik puani.</summary>
public class AdminWorkoutAttributeScoreItemDto
{
    public string AttributeType { get; set; } = string.Empty;
    public int Score { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Bir antrenmanin tum ozellik puanlari. Henuz puanlanmamis ozellik turleri
/// <see cref="MissingAttributeTypes"/> icinde listelenir (urun incelemesi icin).
/// </summary>
public class AdminWorkoutAttributeScoresDto
{
    public long WorkoutId { get; set; }
    public List<AdminWorkoutAttributeScoreItemDto> Scores { get; set; } = new();
    public List<string> MissingAttributeTypes { get; set; } = new();
}

/// <summary>Tek bir ozellik puani girdisi.</summary>
public class AdminWorkoutAttributeScoreInput
{
    [Required(ErrorMessage = "Ozellik turu zorunludur.")]
    [EnumDataType(typeof(WorkoutAttributeType), ErrorMessage = "Gecersiz ozellik turu.")]
    public WorkoutAttributeType? AttributeType { get; set; }

    [Range(WorkoutAttributeScore.MinScore, WorkoutAttributeScore.MaxScore, ErrorMessage = "Puan 0-100 araliginda olmalidir.")]
    public int Score { get; set; }
}

/// <summary>
/// Antrenmanin ozellik puanlarini tamamen degistirir (replace-all): listede olmayan mevcut puanlar
/// silinir, olanlar eklenir/guncellenir. Bos liste tum puanlari kaldirir.
/// </summary>
public class AdminSetWorkoutAttributeScoresRequest
{
    [Required]
    public List<AdminWorkoutAttributeScoreInput> Scores { get; set; } = new();
}
