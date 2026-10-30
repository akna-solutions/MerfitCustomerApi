using System.Globalization;
using System.Text;

namespace MerfitCustomerApi.Business.Services.ContentModeration;

/// <summary>Bir metin parcasinin karsilastirmada kullanilan bicimleri (bkz. <see cref="ModerationTextNormalizer"/>).</summary>
internal enum TextFormKind
{
    /// <summary>ASCII'ye katlanmis, tekrar eden harfleri teke indirilmis bicim ("Şiiiktir" -> "siktir").</summary>
    Folded,

    /// <summary>ASCII'ye katlanmis, tekrar eden harfleri en fazla ikiye indirilmis bicim ("yarrrak" -> "yarrak").</summary>
    FoldedDoubles,

    /// <summary>Turkce harfleri koruyan, tekrar eden harfleri teke indirilmis bicim ("ŞIK" -> "şık", "SİK" -> "sik").</summary>
    Turkish,
}

/// <summary>Bir token'in (veya birlestirilmis token dizisinin) uc karsilastirma bicimi.</summary>
internal readonly record struct TextForms(string Folded, string FoldedDoubles, string Turkish)
{
    public string Get(TextFormKind kind) => kind switch
    {
        TextFormKind.FoldedDoubles => FoldedDoubles,
        TextFormKind.Turkish => Turkish,
        _ => Folded,
    };
}

/// <summary>Normallestirilmis metin: kelime token'lari, ayraclarla bolunmus kelimelerin birlesimleri ve tum metnin bitisik hali.</summary>
internal sealed class NormalizedContent
{
    /// <summary>Bosluk ve ayraclarla (nokta, tire, alt cizgi, ozel karakter) ayrilmis kelimeler, metindeki sirayla.</summary>
    public required IReadOnlyList<TextForms> Tokens { get; init; }

    /// <summary>
    /// Kelime sinirini atlatma girisimlerini yakalamak icin birlestirilmis adaylar: ayni bosluksuz parcadaki token'lar
    /// ("a.m.k" -> "amk") ve art arda gelen tek harfli token'lar ("s i k t i r" -> "siktir").
    /// </summary>
    public required IReadOnlyList<TextForms> Joined { get; init; }

    /// <summary>Tum token'larin bitisik hali (kullanici adinda gomulu ifade ve sistem adi aramasi icin).</summary>
    public required TextForms Concatenated { get; init; }

    /// <summary>Rakamlar leet donusumu yapilmadan atilarak elde edilen bitisik hal ("admin_1" -> "admin").</summary>
    public required string ConcatenatedLettersOnly { get; init; }
}

/// <summary>
/// Moderasyon icin metin normallestirme. Metin bir kez islenir ve tum kontroller ayni sonucu kullanir:
/// <list type="number">
/// <item>Unicode FormC; gorunmez/bicim karakterleri (sifir genislikli bosluk vb.) ve birlesik isaretler atilir.</item>
/// <item>Turkce kurallarla kucuk harf (I -> ı, İ -> i); Kiril/Yunan benzer harfler Latin karsiliklarina cevrilir.</item>
/// <item>Bosluklar parca (chunk) sinirdir; harf/rakam disindaki her karakter (nokta, tire, _, *, ...) parca icinde kelime ayracidir.</item>
/// <item>Harf iceren kelimelerde rakam ve sembol ikameleri harfe cevrilir (0->o, 1->i, 3->e, 4->a, 5->s, 7->t, @->a, $->s);
/// yalnizca rakamdan olusan kelimeler (orn. "1995") atilir. "!" ve "|" yalnizca kelime icindeyse "i" sayilir.</item>
/// <item>Uc bicim uretilir (bkz. <see cref="TextFormKind"/>): ASCII katlama (ç->c, ş->s, ğ->g, ı->i, ö->o, ü->u, â->a)
/// ve tekrar eden harf sadelestirmesi.</item>
/// </list>
/// Kelime icindeki karakterler kontrolsuzce silinmez: once orijinal kelime sinirlariyla token'lar uretilir, birlestirme
/// yalnizca ayni bosluksuz parca icinde ve tek harfli token dizilerinde yapilir.
/// </summary>
internal static class ModerationTextNormalizer
{
    private static readonly char[] FoldTable = BuildFoldTable();

    private static readonly Dictionary<char, char> Homoglyphs = new()
    {
        // Kiril
        ['а'] = 'a', ['в'] = 'b', ['е'] = 'e', ['ё'] = 'e', ['к'] = 'k', ['м'] = 'm', ['н'] = 'h', ['о'] = 'o',
        ['р'] = 'p', ['с'] = 'c', ['т'] = 't', ['у'] = 'y', ['х'] = 'x', ['і'] = 'i', ['ј'] = 'j', ['ѕ'] = 's',
        // Yunan
        ['α'] = 'a', ['β'] = 'b', ['ε'] = 'e', ['ι'] = 'i', ['κ'] = 'k', ['ν'] = 'v', ['ο'] = 'o', ['ρ'] = 'p',
        ['τ'] = 't', ['υ'] = 'u', ['χ'] = 'x',
    };

    public static NormalizedContent Normalize(string content)
    {
        var text = content.IsNormalized(NormalizationForm.FormC) ? content : content.Normalize(NormalizationForm.FormC);

        var tokens = new List<TextForms>();
        var rawTokens = new List<string>();
        var joined = new List<TextForms>();
        var concatenated = new StringBuilder(text.Length);
        var lettersOnly = new StringBuilder(text.Length);

        var chunkRaw = new StringBuilder();
        var chunkTokenCount = 0;
        var token = new StringBuilder();
        var tokenLettersOnly = new StringBuilder();
        var tokenHasLetter = false;
        var singleCharRun = new StringBuilder();
        var singleCharRunLength = 0;

        void FlushSingleCharRun()
        {
            if (singleCharRunLength >= 2)
            {
                joined.Add(CreateForms(singleCharRun.ToString()));
            }
            singleCharRun.Clear();
            singleCharRunLength = 0;
        }

        void EndToken()
        {
            if (token.Length == 0)
            {
                return;
            }

            if (tokenHasLetter)
            {
                var raw = MapLeetDigits(token);
                tokens.Add(CreateForms(raw));
                rawTokens.Add(raw);
                chunkRaw.Append(raw);
                chunkTokenCount++;
                concatenated.Append(raw);
                lettersOnly.Append(tokenLettersOnly);

                if (raw.Length == 1)
                {
                    singleCharRun.Append(raw);
                    singleCharRunLength++;
                }
                else
                {
                    FlushSingleCharRun();
                }
            }
            else
            {
                FlushSingleCharRun();
            }

            token.Clear();
            tokenLettersOnly.Clear();
            tokenHasLetter = false;
        }

        void EndChunk()
        {
            EndToken();
            if (chunkTokenCount >= 2)
            {
                joined.Add(CreateForms(chunkRaw.ToString()));
            }
            chunkRaw.Clear();
            chunkTokenCount = 0;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark)
            {
                // Sifir genislikli karakterler ve birlesik isaretler kelimeyi bolmez ("s​ik" -> "sik").
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                EndChunk();
                continue;
            }

            var lower = ToTurkishLower(c);
            if (Homoglyphs.TryGetValue(lower, out var latin))
            {
                lower = latin;
            }

            if (char.IsLetter(lower))
            {
                token.Append(lower);
                tokenLettersOnly.Append(lower);
                tokenHasLetter = true;
            }
            else if (char.IsAsciiDigit(lower))
            {
                token.Append(lower);
            }
            else if (SymbolLetter(lower) is { } symbolLetter)
            {
                token.Append(symbolLetter);
                tokenLettersOnly.Append(symbolLetter);
                tokenHasLetter = true;
            }
            else if (lower is '!' or '|' && token.Length > 0 && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]))
            {
                // Yalnizca kelime icinde "i" yerine kullanilmis sayilir ("s!ktir"); sondaki unlem ayractir.
                token.Append('i');
                tokenLettersOnly.Append('i');
                tokenHasLetter = true;
            }
            else
            {
                EndToken();
            }
        }

        EndChunk();
        FlushSingleCharRun();

        return new NormalizedContent
        {
            Tokens = tokens,
            Joined = joined,
            Concatenated = CreateForms(concatenated.ToString()),
            ConcatenatedLettersOnly = Collapse(Fold(lettersOnly.ToString()), 1),
        };
    }

    /// <summary>Tek bir sozluk ifadesini (bosluksuz) metinle ayni kurallarla normallestirir.</summary>
    public static TextForms NormalizeTerm(string term)
    {
        var content = Normalize(term);
        return content.Concatenated;
    }

    private static TextForms CreateForms(string raw)
    {
        var folded = Fold(raw);
        return new TextForms(Collapse(folded, 1), Collapse(folded, 2), Collapse(raw, 1));
    }

    private static char ToTurkishLower(char c) => c switch
    {
        'I' => 'ı',
        'İ' => 'i',
        _ => char.ToLowerInvariant(c),
    };

    private static char? SymbolLetter(char c) => c switch
    {
        '@' => 'a',
        '$' => 's',
        '€' => 'e',
        '£' => 'l',
        _ => null,
    };

    private static string MapLeetDigits(StringBuilder token)
    {
        var chars = new char[token.Length];
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            chars[i] = c switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '6' => 'g',
                '7' => 't',
                '8' => 'b',
                '9' => 'g',
                _ => c,
            };
        }

        return new string(chars);
    }

    private static string Fold(string value)
    {
        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c < FoldTable.Length ? FoldTable[c] : c;
            }
        });
    }

    /// <summary>Art arda ayni harften en fazla <paramref name="maxRun"/> tane birakir.</summary>
    private static string Collapse(string value, int maxRun)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var run = 0;
        for (var i = 0; i < value.Length; i++)
        {
            run = i > 0 && value[i] == value[i - 1] ? run + 1 : 1;
            if (run <= maxRun)
            {
                builder.Append(value[i]);
            }
        }

        return builder.Length == value.Length ? value : builder.ToString();
    }

    /// <summary>Latin-1 ve Latin Extended-A/B harflerini aksansiz ASCII karsiliklarina esler (ı -> i dahil).</summary>
    private static char[] BuildFoldTable()
    {
        var table = new char[0x0250];
        for (var i = 0; i < table.Length; i++)
        {
            var c = (char)i;
            table[i] = c;
            if (i < 0x80)
            {
                continue;
            }

            var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            if (decomposed.Length > 0 && char.IsAsciiLetter(decomposed[0]))
            {
                table[i] = char.ToLowerInvariant(decomposed[0]);
            }
        }

        table['ı'] = 'i';
        return table;
    }
}
