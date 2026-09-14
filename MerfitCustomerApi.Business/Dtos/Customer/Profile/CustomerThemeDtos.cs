using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.Profile;

/// <summary>GET/PUT /api/profile/theme yaniti.</summary>
public class ThemePreferenceResponse
{
    /// <summary>"System" | "Light" | "Dark".</summary>
    public string ThemeMode { get; set; } = string.Empty;
}

/// <summary>PUT /api/profile/theme govdesi.</summary>
public class UpdateThemePreferenceRequest
{
    /// <summary>"System" | "Light" | "Dark" - baska bir deger 400 Bad Request doner.</summary>
    [Required]
    public string ThemeMode { get; set; } = string.Empty;
}
