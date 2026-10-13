using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>POST /api/auth/email-verification/resend yaniti.</summary>
public class ResendVerificationEmailResponse
{
    /// <summary>E-posta zaten dogrulanmissa true; bu durumda e-posta gonderilmez.</summary>
    public bool AlreadyVerified { get; set; }

    /// <summary>Yeni dogrulama e-postasi gonderildiyse true.</summary>
    public bool Sent { get; set; }

    /// <summary>Bir sonraki tekrar gonderim isteginin yapilabilecegi sure (saniye).</summary>
    public int ResendAvailableInSeconds { get; set; }
}

/// <summary>POST /api/auth/email-verification/confirm istegi.</summary>
public class ConfirmEmailRequest
{
    [Required(ErrorMessage = "Doğrulama kodu zorunludur.")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>POST /api/auth/email-verification/confirm yaniti.</summary>
public class ConfirmEmailResponse
{
    public string Email { get; set; } = string.Empty;

    public bool EmailConfirmed { get; set; }
}

/// <summary>POST /api/auth/password/forgot istegi.</summary>
public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "E-posta alanı zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi gir.")]
    [StringLength(500)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>POST /api/auth/password/reset/validate istegi.</summary>
public class VerifyPasswordResetCodeRequest
{
    [Required(ErrorMessage = "E-posta alanı zorunludur.")]
    [StringLength(500)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kod zorunludur.")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>POST /api/auth/password/reset istegi.</summary>
public class ResetPasswordRequest
{
    [Required(ErrorMessage = "E-posta alanı zorunludur.")]
    [StringLength(500)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kod zorunludur.")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Kayittaki parola kuraliyla ayni (bkz. RegisterRequest.Password).</summary>
    [Required(ErrorMessage = "Yeni şifre zorunludur.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre 6-100 karakter arasında olmalıdır.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>Sonucu yalnizca bir mesajdan ibaret olan auth uclarinin yaniti (forgot / reset / validate).</summary>
public class AccountActionResponse
{
    public string Message { get; set; } = string.Empty;
}

public class UsernameAvailabilityResponse
{
    /// <summary>Kontrol edilen kullanici adi (kucuk harfe cevrilmis, bosluklari kirpilmis).</summary>
    public string Username { get; set; } = string.Empty;

    public bool Available { get; set; }

    /// <summary>Kullaniciya gosterilecek kisa aciklama (bicim hatasi veya alinmis).</summary>
    public string? Message { get; set; }
}
