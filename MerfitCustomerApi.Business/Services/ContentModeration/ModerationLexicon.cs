namespace MerfitCustomerApi.Business.Services.ContentModeration;

/// <summary>Bir sozluk ifadesinin metinle nasil eslestirilecegi.</summary>
public enum TermMatch
{
    /// <summary>Yalnizca kelimenin tamami ("amk" kelimesi; "hamka" degil).</summary>
    Word,

    /// <summary>Kelimenin tamami veya kelime basi; Turkce ekleri yakalar ("orospu" -> "orospunun", "orospuluk").</summary>
    Stem,

    /// <summary>
    /// <see cref="Stem"/>'e ek olarak kullanici adinda bitisik yazilmis halde de aranir ("mertorospu123"). Yalnizca
    /// baska kelimelerin icinde gecmeyecek kadar ayirt edici ifadeler icin kullanilir.
    /// </summary>
    Embedded,
}

/// <summary>Merkezi sozlukteki tek bir ifade.</summary>
/// <param name="Text">Ifadenin dogal yazimi (Turkce karakterlerle). Bosluk iceriyorsa kelime kelime eslesen bir ifade (phrase) olur.</param>
/// <param name="Category">Reddedilme kategorisi.</param>
/// <param name="Match">Eslestirme bicimi.</param>
/// <param name="DiacriticSensitive">
/// true ise Turkce harfler korunarak karsilastirilir: "sik" eslesir, "şık" eslesmez; "göt" eslesir, "got" eslesmez.
/// ASCII'ye katlandiginda masum bir kelimeyle cakisan ifadeler icindir.
/// </param>
/// <param name="KeepDoubleLetters">
/// true ise cift harfler korunarak karsilastirilir ("nigger" eslesir, "niger" eslesmez). Uc ve daha fazla tekrar yine ikiye iner.
/// </param>
/// <param name="UsernameOnly">true ise yalnizca kullanici adinda aranir (orn. "hitler", "porn").</param>
public sealed record ModerationTerm(
    string Text,
    ModerationCategory Category,
    TermMatch Match = TermMatch.Word,
    bool DiacriticSensitive = false,
    bool KeepDoubleLetters = false,
    bool UsernameOnly = false);

/// <summary>
/// MB Fit icerik moderasyonunun TEK sozlugu. Kufur, hakaret, cinsel icerik, tehdit/asagilama, irkci/ayrimci ifadeler,
/// kullanici adina uygun olmayan ifadeler, sistem hesabi adlari ve yanlis pozitifleri onleyen izinli kelimeler burada
/// tutulur. Ifadeler dogal yazimla eklenir; normallestirme (buyuk/kucuk harf, Turkce karakter, tekrar eden harf)
/// <see cref="ContentModerationService"/> acilirken bir kez yapilir.
///
/// Yeni ifade eklerken:
/// <list type="bullet">
/// <item>Kisa veya baska bir kelimenin parcasi olabilecek ifadeleri <see cref="TermMatch.Word"/> olarak ekleyin.</item>
/// <item>ASCII'ye katlandiginda masum bir Turkce kelimeye donusen ifadeler (şık/sik, götürmek/göt) icin
/// <see cref="ModerationTerm.DiacriticSensitive"/> kullanin veya kelimenin eklerini tek tek <see cref="TermMatch.Word"/> olarak ekleyin.</item>
/// <item>Bir <see cref="TermMatch.Stem"/>/<see cref="TermMatch.Embedded"/> ifadesi masum bir kelimeyi yakaliyorsa o kelimeyi
/// <see cref="AllowedWords"/>'e ekleyin ve ContentModerationServiceTests'e bir test yazin.</item>
/// </list>
/// </summary>
public static class ModerationLexicon
{
    public static IReadOnlyList<ModerationTerm> Terms { get; } = BuildTerms();

    /// <summary>
    /// Kok/gomulu eslesmenin yanlislikla yakaladigi masum kelimeler. Bir eslesme tamamen bu kelimelerden birinin icinde
    /// kaliyorsa yok sayilir ("saksofon" -> "sakso", "aşıktır" -> "siktir").
    /// </summary>
    public static IReadOnlyList<string> AllowedWords { get; } =
    [
        "saksofon", "saksafon",
        "aşıktır", "ışıktır", "kasıktır", "basıktır", "yakışıktır", "sıkıştır", "şıktır",
    ];

    /// <summary>
    /// Sistem/resmi hesap adlari: kullanici adi normallestirilip (ayraclar, sondaki rakamlar ve harf tekrarlari atilip)
    /// bunlardan birine esitse reddedilir ("Admin", "admin_1", "4dm1n", "s.u.p.p.o.r.t").
    /// </summary>
    public static IReadOnlyList<string> ReservedUsernames { get; } =
    [
        "admin", "administrator", "sysadmin", "superuser", "root", "moderator",
        "support", "system", "sistem", "destek", "yönetici", "official", "staff",
        "mbfit", "merfit", "mbshop",
        "mbfitadmin", "mbfitsupport", "mbfitofficial",
    ];

    /// <summary>Marka adlari: kullanici adinda <see cref="StaffRoleWords"/>'ten biriyle birlikte geciyorsa reddedilir ("mbfit_destek").</summary>
    public static IReadOnlyList<string> BrandNames { get; } = ["mbfit", "merfit", "mbshop"];

    /// <summary>Marka adiyla birlikte kullanildiginda resmi hesap izlenimi veren kelimeler.</summary>
    public static IReadOnlyList<string> StaffRoleWords { get; } =
    [
        "admin", "support", "destek", "official", "resmi", "team", "ekip", "staff", "moderator",
        "yönetici", "yonetim", "system", "sistem", "help", "yardım", "info", "müşteri", "hizmet",
    ];

    private static List<ModerationTerm> BuildTerms()
    {
        var terms = new List<ModerationTerm>();

        void Add(ModerationCategory category, TermMatch match, params string[] texts)
        {
            foreach (var text in texts) terms.Add(new ModerationTerm(text, category, match));
        }

        void AddTurkish(ModerationCategory category, TermMatch match, params string[] texts)
        {
            foreach (var text in texts) terms.Add(new ModerationTerm(text, category, match, DiacriticSensitive: true));
        }

        void AddPhrases(ModerationCategory category, params string[] texts) => Add(category, TermMatch.Word, texts);

        // ---------------------------------------------------------------- Kufurler
        Add(ModerationCategory.Profanity, TermMatch.Embedded,
            "orospu", "orosbu", "oruspu", "pezevenk", "siktir", "sikerim", "sikeyim", "sikiyim", "sikicem", "sikecem",
            "sikeceğim", "sikerler", "amcık", "amınakoy", "aminakoy", "yavşak", "kahpe", "gavat", "götveren", "dalyarak",
            "kaltak", "fuck", "bitch", "asshole", "whore");
        Add(ModerationCategory.Profanity, TermMatch.Stem,
            "siker", "sikecek", "sikmek", "hassiktir", "yarrak", "yarrağ", "taşak", "daşşak", "ibne", "götlek",
            "fahişe", "sürtük", "kevaşe", "amcığ", "pezo", "orspu");
        Add(ModerationCategory.Profanity, TermMatch.Word,
            "amk", "amq", "aq", "awk", "mk", "mq", "sg", "sktr", "sktir", "skm", "skrm", "skerim", "skrim", "sikm",
            "amnskm", "amnskym", "amnsktm", "puşt", "pust", "shit", "slut", "pussy", "cunt", "motherfucker");
        AddTurkish(ModerationCategory.Profanity, TermMatch.Word,
            "sik", "sikim", "sikin", "sikti", "sikiyor", "sikik", "sikiş", "sikişmek", "sikmiş",
            "göt", "götü", "götün", "götünü", "götüne", "götler", "götten",
            "amın", "amına", "amını", "oç", "piç", "piçler", "piçin", "piçlik");
        AddTurkish(ModerationCategory.Profanity, TermMatch.Stem, "sikik", "sikiş", "piç");
        AddPhrases(ModerationCategory.Profanity,
            "amına koyayım", "amına koyim", "amına koyarım", "amına koyduğum", "amına koydum", "amına koyam",
            "ana avrat", "ananı avradını", "ananı sikim", "orospu çocuğu", "kafa sikmek");

        // ---------------------------------------------------------------- Hakaretler
        Add(ModerationCategory.Insult, TermMatch.Embedded, "şerefsiz", "gerizekalı", "dangalak");
        Add(ModerationCategory.Insult, TermMatch.Stem,
            "haysiyetsiz", "namussuz", "aptal", "salak", "dallama", "kancık", "embesil", "lavuk", "yobaz");
        Add(ModerationCategory.Insult, TermMatch.Word,
            "beyinsiz", "ahmak", "hödük", "ezik", "soysuz", "moron", "idiot");
        AddPhrases(ModerationCategory.Insult,
            "geri zekalı", "piç kurusu", "it oğlu it", "köpek oğlu", "eşek oğlu eşek", "ananı satayım");

        // ---------------------------------------------------------------- Cinsel icerik
        Add(ModerationCategory.Sexual, TermMatch.Embedded, "porno", "porn", "sakso", "dildo", "hentai", "masturb", "mastürb", "onlyfans");
        Add(ModerationCategory.Sexual, TermMatch.Stem, "orgazm", "domal", "erotik", "pedofil");
        Add(ModerationCategory.Sexual, TermMatch.Word,
            "seks", "sex", "penis", "vajina", "azgın", "nude", "nudes", "escort", "eskort", "horny", "milf", "boobs");
        AddPhrases(ModerationCategory.Sexual,
            "am yala", "am yalarım", "sik yala", "sakso çek", "seks yap", "seks yapalım", "yatağa atayım");

        // ---------------------------------------------------------------- Tehdit ve asagilayici ifadeler
        foreach (var target in new[] { "seni", "sizi", "onu", "hepinizi", "aileni" })
        {
            foreach (var verb in new[]
                     {
                         "öldüreceğim", "öldürürüm", "öldürücem", "öldürcem", "gebertirim", "geberteceğim", "gebertcem",
                         "keserim", "doğrarım", "vururum", "bulacağım", "bulurum", "yakarım", "mahvederim",
                     })
            {
                AddPhrases(ModerationCategory.Threat, $"{target} {verb}");
            }
        }
        AddPhrases(ModerationCategory.Threat,
            "kafanı keserim", "kafanı koparırım", "kafana sıkarım", "evini yakarım", "ölümün benden olacak",
            "kendini öldür", "intihar et", "kill yourself", "çöp insan", "insan müsveddesi", "insan bozuntusu");
        Add(ModerationCategory.Threat, TermMatch.Stem, "tecavüz");
        Add(ModerationCategory.Threat, TermMatch.Word, "gebertirim", "geberteceğim", "geber", "gebersin", "geberin", "kys", "pislik", "aşağılık");

        // ---------------------------------------------------------------- Irkci ve ayrimci ifadeler
        terms.Add(new ModerationTerm("nigger", ModerationCategory.HateSpeech, TermMatch.Stem, KeepDoubleLetters: true));
        terms.Add(new ModerationTerm("nigga", ModerationCategory.HateSpeech, TermMatch.Stem, KeepDoubleLetters: true));
        terms.Add(new ModerationTerm("faggot", ModerationCategory.HateSpeech, TermMatch.Stem, KeepDoubleLetters: true));
        foreach (var group in new[] { "kürt", "arap", "suriyeli", "afgan", "yahudi", "ermeni", "rum", "çingene", "zenci", "alevi", "gavur", "kızılbaş", "eşcinsel" })
        {
            AddPhrases(ModerationCategory.HateSpeech, $"pis {group}");
        }
        foreach (var group in new[] { "kürtler", "araplar", "suriyeliler", "afganlar", "yahudiler", "ermeniler", "rumlar", "çingeneler", "aleviler", "eşcinseller", "zenciler" })
        {
            // "ölsün" ASCII'de "olsun"a katlandigi icin Turkce harf duyarli eslesir.
            terms.Add(new ModerationTerm($"{group} ölsün", ModerationCategory.HateSpeech, DiacriticSensitive: true));
            AddPhrases(ModerationCategory.HateSpeech, $"{group} defolsun", $"kahrolsun {group}");
        }
        AddPhrases(ModerationCategory.HateSpeech, "yahudi domuzu", "ermeni tohumu", "kürt köpeği", "suriyeli köpek");

        // ---------------------------------------------------------------- Kullanici adi olarak uygunsuz ifadeler
        terms.Add(new ModerationTerm("hitler", ModerationCategory.InappropriateUsername, TermMatch.Embedded, UsernameOnly: true));
        terms.Add(new ModerationTerm("pkk", ModerationCategory.InappropriateUsername, KeepDoubleLetters: true, UsernameOnly: true));
        foreach (var text in new[] { "deaş", "daeş", "terörist", "uyuşturucu", "torbacı" })
        {
            terms.Add(new ModerationTerm(text, ModerationCategory.InappropriateUsername, TermMatch.Stem, UsernameOnly: true));
        }
        // Kok olarak aranmaz: "Nazife", "Nazım", "katılım" gibi masum kelimelerin basi.
        foreach (var text in new[] { "nazi", "naziler", "isid", "isis", "katil" })
        {
            terms.Add(new ModerationTerm(text, ModerationCategory.InappropriateUsername, UsernameOnly: true));
        }

        return terms;
    }
}
