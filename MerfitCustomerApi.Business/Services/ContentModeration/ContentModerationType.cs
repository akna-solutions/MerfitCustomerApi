namespace MerfitCustomerApi.Business.Services.ContentModeration;

/// <summary>
/// Kontrol edilen alanin turu. Her tur farkli kurallarla kontrol edilir (bkz. <see cref="ContentModerationService"/>):
/// kullanici adinda bitisik yazim ve sistem hesabi taklidi de aranir, ad/soyad/aciklamada yalnizca kelime sinirlari.
/// </summary>
public enum ContentModerationType
{
    FirstName,
    LastName,
    Username,
    ProfileBio,
}
