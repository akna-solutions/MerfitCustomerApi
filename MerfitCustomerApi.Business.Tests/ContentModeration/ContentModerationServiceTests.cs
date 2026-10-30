using MerfitCustomerApi.Business.Services.ContentModeration;
using MerfitCustomerApi.Domain.Exceptions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.ContentModeration;

/// <summary>
/// Sozluk tabanli icerik moderasyonu: normal isim/kullanici adlari kabul edilir; kufur, hakaret, cinsel icerik, tehdit,
/// ayrimcilik ve sistem hesabi taklidi; bosluk/noktalama, buyuk-kucuk harf, Turkce karakter, harf tekrari ve rakam
/// ikamesiyle atlatma girisimleri dahil reddedilir. Masum kelimeler (şık, saksofon, götürmek, Amina...) reddedilmez.
/// </summary>
public class ContentModerationServiceTests
{
    private readonly ContentModerationService _service = new();

    private void AssertAllowed(string? content, ContentModerationType type)
    {
        var result = _service.Check(content, type);
        Assert.True(result.IsAllowed, $"'{content}' ({type}) reddedilmemeliydi: {result.DetectedCategory} / {result.MatchedTerm}");
        Assert.Null(result.RejectionReason);
        Assert.Null(result.DetectedCategory);
    }

    private ModerationResult AssertRejected(string content, ContentModerationType type)
    {
        var result = _service.Check(content, type);
        Assert.False(result.IsAllowed, $"'{content}' ({type}) reddedilmeliydi.");
        Assert.False(string.IsNullOrWhiteSpace(result.RejectionReason));
        Assert.NotNull(result.DetectedCategory);
        return result;
    }

    // 1. Normal Turkce isimler
    [Theory]
    [InlineData("Mert")]
    [InlineData("Ayşe Nur")]
    [InlineData("Çağrı")]
    [InlineData("İsmail")]
    [InlineData("Gülşen")]
    [InlineData("Şükrü")]
    [InlineData("Ömer Faruk")]
    [InlineData("Amina")]
    [InlineData("Sıkı")]
    [InlineData("Ali-Rıza")]
    [InlineData("Ece")]
    public void NormalTurkishNames_AreAllowed(string name)
    {
        AssertAllowed(name, ContentModerationType.FirstName);
    }

    [Theory]
    [InlineData("Akpınar")]
    [InlineData("Yılmaz")]
    [InlineData("Şık")]
    [InlineData("Göttingen")]
    [InlineData("Sikke")]
    [InlineData("Aşıkoğlu")]
    [InlineData("Kurtuluş")]
    public void NormalTurkishLastNames_AreAllowed(string lastName)
    {
        AssertAllowed(lastName, ContentModerationType.LastName);
    }

    // 2. Normal kullanici adlari
    [Theory]
    [InlineData("mert")]
    [InlineData("mert_akpinar")]
    [InlineData("ayse1995")]
    [InlineData("fit_gulsen")]
    [InlineData("mbfit_fan")]
    [InlineData("saksofoncu_ali")]
    [InlineData("asiktir_bu")]
    [InlineData("kayarak_gelen")]
    [InlineData("nazik_kiz")]
    [InlineData("nazife_34")]
    [InlineData("nazim")]
    [InlineData("administer")]
    [InlineData("mert_admin")]
    [InlineData("sikayetci")]
    [InlineData("goturen")]
    [InlineData("mahmut1907")]
    [InlineData("kartal_34")]
    public void NormalUsernames_AreAllowed(string username)
    {
        AssertAllowed(username, ContentModerationType.Username);
    }

    // 3. Acik kufur iceren kullanici adi
    [Theory]
    [InlineData("orospu", ModerationCategory.Profanity)]
    [InlineData("amk", ModerationCategory.Profanity)]
    [InlineData("siktir", ModerationCategory.Profanity)]
    [InlineData("mert_amk", ModerationCategory.Profanity)]
    [InlineData("mertorospu123", ModerationCategory.Profanity)]
    [InlineData("pezevenkali", ModerationCategory.Profanity)]
    [InlineData("serefsiz_adam", ModerationCategory.Insult)]
    [InlineData("porno_kral", ModerationCategory.Sexual)]
    [InlineData("hitler_34", ModerationCategory.InappropriateUsername)]
    public void ExplicitProfanityInUsername_IsRejected(string username, ModerationCategory category)
    {
        var result = AssertRejected(username, ContentModerationType.Username);
        Assert.Equal(category, result.DetectedCategory);
        Assert.StartsWith("Kullanıcı adı", result.RejectionReason);
    }

    // 4. Araya bosluk ve noktalama eklenmis kufur
    [Theory]
    [InlineData("a.m.k")]
    [InlineData("a m k")]
    [InlineData("o-r-o-s-p-u")]
    [InlineData("s i k t i r")]
    [InlineData("s.i.k.t.i.r git")]
    [InlineData("or*os*pu")]
    [InlineData("si​ktir")]
    [InlineData("o_r_o_s_p_u")]
    public void ProfanityWithSpacesAndPunctuation_IsRejected(string content)
    {
        AssertRejected(content, ContentModerationType.FirstName);
    }

    // 5. Buyuk/kucuk harf degisikligi
    [Theory]
    [InlineData("OROSPU")]
    [InlineData("OrOsPu")]
    [InlineData("SİKTİR")]
    [InlineData("SIKTIR")]
    [InlineData("AmK")]
    public void MixedCaseProfanity_IsRejected(string content)
    {
        AssertRejected(content, ContentModerationType.FirstName);
    }

    // 6. Turkce karakter farkliliklari
    [Theory]
    [InlineData("şerefsiz")]
    [InlineData("serefsiz")]
    [InlineData("ŞEREFSİZ")]
    [InlineData("yavşak")]
    [InlineData("yavsak")]
    [InlineData("amcık")]
    [InlineData("amcik")]
    [InlineData("göt")]
    [InlineData("gerizekalı")]
    [InlineData("gerizekali")]
    public void TurkishCharacterVariants_AreRejected(string content)
    {
        AssertRejected(content, ContentModerationType.LastName);
    }

    [Theory]
    [InlineData("şık")]
    [InlineData("Şık Giyim")]
    [InlineData("got")]
    [InlineData("götürmek")]
    [InlineData("sıkı çalış")]
    [InlineData("sıkıştı")]
    [InlineData("sıkmak")]
    public void TurkishWordsThatFoldToProfanity_AreAllowed(string content)
    {
        AssertAllowed(content, ContentModerationType.ProfileBio);
    }

    // 7. Tekrarlanan harfler ve rakam/sembol ikameleri
    [Theory]
    [InlineData("orooospuuu")]
    [InlineData("siiiiiktir")]
    [InlineData("aaammmkkk")]
    [InlineData("0r0spu")]
    [InlineData("s1kt1r")]
    [InlineData("4mk")]
    [InlineData("@mk")]
    [InlineData("$erefsiz")]
    [InlineData("s!ktir")]
    [InlineData("y4rr4k")]
    public void RepeatedLettersAndLeetSpeak_AreRejected(string content)
    {
        AssertRejected(content, ContentModerationType.Username);
    }

    // 8. Masum kelimeler
    [Theory]
    [InlineData("Saksofon çalmayı ve koşmayı seviyorum")]
    [InlineData("Şikayet etmeden antrenman yapıyorum")]
    [InlineData("Kayarak, koyarak, sayarak ilerliyorum")]
    [InlineData("Nazik ol, sıkı çalış!")]
    [InlineData("Ankara'dan selamlar, amacım kas yapmak")]
    [InlineData("Katılım önemli")]
    [InlineData("I am a runner and I got this")]
    [InlineData("Malatya, Amasya, Sinop")]
    [InlineData("Haftada 5 gün spor! 100 kg squat")]
    [InlineData("Ali ama sporcu")]
    [InlineData("Aşıktır bu kız spora")]
    [InlineData("Bu tayt çok şıktır")]
    [InlineData("Sıkıştırılmış program")]
    [InlineData("Ölmeden önce maraton koşacağım")]
    [InlineData("Kürtler olsun Türkler olsun herkes spor yapsın")]
    public void InnocentWords_AreNotRejected(string content)
    {
        AssertAllowed(content, ContentModerationType.ProfileBio);
    }

    // 9. Bos ve null degerler: zorunluluk DTO validasyonunun isidir, moderasyon izin verir.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void NullOrEmpty_IsAllowed(string? content)
    {
        foreach (var type in Enum.GetValues<ContentModerationType>())
        {
            AssertAllowed(content, type);
        }
    }

    [Fact]
    public void PunctuationOrDigitsOnly_IsAllowed()
    {
        AssertAllowed("...---___", ContentModerationType.FirstName);
        AssertAllowed("1234567", ContentModerationType.ProfileBio);
    }

    [Fact]
    public void VeryLongContent_IsRejectedWithoutScanning()
    {
        var result = _service.Check(new string('a', ContentModerationService.MaxContentLength + 1), ContentModerationType.ProfileBio);

        Assert.False(result.IsAllowed);
        Assert.Equal(ModerationCategory.TooLong, result.DetectedCategory);
    }

    [Fact]
    public void LongInnocentContent_IsCheckedQuickly()
    {
        var text = string.Join(' ', Enumerable.Repeat("Bugün sıkı bir antrenman yaptım, şikayetim yok!", 80));
        Assert.True(text.Length <= ContentModerationService.MaxContentLength);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            Assert.True(_service.Check(text, ContentModerationType.ProfileBio).IsAllowed);
        }

        Assert.True(watch.ElapsedMilliseconds < 5000, $"50 kontrol {watch.ElapsedMilliseconds} ms surdu.");
    }

    // 12. Sistem kullanici adlari
    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData("ADMINISTRATOR")]
    [InlineData("mbfitadmin")]
    [InlineData("mbfit_admin")]
    [InlineData("mbfitsupport")]
    [InlineData("mbfit_support")]
    [InlineData("mbfitofficial")]
    [InlineData("MBFit_Official")]
    [InlineData("support")]
    [InlineData("system")]
    [InlineData("admin_1")]
    [InlineData("4dm1n")]
    [InlineData("aadmiin")]
    [InlineData("mbfit_destek")]
    [InlineData("merfit_team")]
    [InlineData("mbfit")]
    public void SystemUsernames_AreRejected(string username)
    {
        var result = AssertRejected(username, ContentModerationType.Username);
        Assert.Equal(ModerationCategory.ReservedName, result.DetectedCategory);
    }

    [Fact]
    public void SystemNames_AreOnlyReservedForUsernames()
    {
        AssertAllowed("Admin", ContentModerationType.FirstName);
        AssertAllowed("System", ContentModerationType.LastName);
    }

    [Theory]
    [InlineData("seni öldüreceğim", ModerationCategory.Threat)]
    [InlineData("SENİ GEBERTİRİM", ModerationCategory.Threat)]
    [InlineData("seni.bulacağım", ModerationCategory.Threat)]
    [InlineData("pis suriyeli", ModerationCategory.HateSpeech)]
    [InlineData("Kürtler ölsün", ModerationCategory.HateSpeech)]
    [InlineData("niggerrr", ModerationCategory.HateSpeech)]
    [InlineData("seks partneri arıyorum", ModerationCategory.Sexual)]
    [InlineData("tam bir aptalsın", ModerationCategory.Insult)]
    [InlineData("amına koyayım", ModerationCategory.Profanity)]
    [InlineData("orospunun çocuğu", ModerationCategory.Profanity)]
    public void CategoriesInProfileBio_AreDetected(string content, ModerationCategory category)
    {
        var result = AssertRejected(content, ContentModerationType.ProfileBio);
        Assert.Equal(category, result.DetectedCategory);
        Assert.StartsWith("Profil açıklaması", result.RejectionReason);
    }

    [Fact]
    public void UsernameOnlyTerms_DoNotAffectOtherFields()
    {
        AssertAllowed("Katil balinalar belgeseli", ContentModerationType.ProfileBio);
        AssertRejected("katil", ContentModerationType.Username);
    }

    [Fact]
    public void RejectionReason_NamesFieldAndReason()
    {
        var result = _service.Check("şerefsiz", ContentModerationType.FirstName);

        Assert.Equal("Ad hakaret içeremez. Lütfen düzenleyip tekrar dene.", result.RejectionReason);
        Assert.Equal(ModerationCategory.Insult, result.DetectedCategory);
    }

    [Fact]
    public void EnsureAllowed_ReportsEveryRejectedField()
    {
        var ex = Assert.Throws<AppValidationException>(() => _service.EnsureAllowed(
            ("FirstName", "Mert", ContentModerationType.FirstName),
            ("LastName", "orospu", ContentModerationType.LastName),
            ("Username", "admin", ContentModerationType.Username)));

        Assert.Equal(new[] { "LastName", "Username" }, ex.Errors.Keys.OrderBy(k => k));
        Assert.StartsWith("Soyad", ex.Errors["LastName"].Single());
        Assert.StartsWith("Bu kullanıcı adı sistem", ex.Errors["Username"].Single());
    }

    [Fact]
    public void Lexicon_EveryTermIsDetectedInItsOwnScope()
    {
        // Sozluge eklenen her ifade kendi yaziminda yakalanmali (normallestirme hatasi veya cakisan anahtar kontrolu).
        foreach (var term in ModerationLexicon.Terms)
        {
            var result = _service.Check(term.Text, ContentModerationType.Username);
            Assert.False(result.IsAllowed, $"Sozluk ifadesi yakalanmadi: '{term.Text}'");
        }
    }
}
