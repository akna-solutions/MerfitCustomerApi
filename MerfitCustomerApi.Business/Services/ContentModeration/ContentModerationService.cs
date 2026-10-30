using MerfitCustomerApi.Business.Interfaces.Services;

namespace MerfitCustomerApi.Business.Services.ContentModeration;

/// <summary>
/// <see cref="IContentModerationService"/>'in sozluk tabanli, yapay zekasiz implementasyonu. Durumsuz ve thread-safe'tir;
/// sozluk indeksi uygulama acilirken bir kez kurulur (singleton olarak kaydedilir).
///
/// Algoritma (her metin <see cref="ModerationTextNormalizer"/> ile bir kez normallestirilir):
/// <list type="number">
/// <item>Her kelime ve her birlestirilmis aday ("a.m.k", "s i k") sozlukte tam kelime ve kelime basi (Turkce ekler) olarak
/// aranir. Arama hash tabanlidir: kelime basi icin yalnizca sozlukteki kok uzunluklari denenir (O(kelime uzunlugu)).</item>
/// <item>Cok kelimeli ifadeler (tehditler, irkci ifadeler) ilk kelimeye gore indekslenir ve kelime dizisi uzerinde aranir.</item>
/// <item>Kullanici adinda ayrica ayirt edici ifadeler bitisik yazimin icinde aranir ("mertorospu") ve sistem/resmi hesap
/// adlari engellenir ("admin", "mbfit_destek").</item>
/// <item>Kok/gomulu eslesme <see cref="ModerationLexicon.AllowedWords"/>'teki masum bir kelimenin icinde kaliyorsa yok sayilir.</item>
/// </list>
/// Regex kullanilmaz (ReDoS riski yok); metin uzunlugu <see cref="MaxContentLength"/> ile sinirlidir.
/// </summary>
public sealed class ContentModerationService : IContentModerationService
{
    /// <summary>Bu uzunlugu asan metin kontrol edilmeden reddedilir (DTO sinirlari bunun cok altindadir).</summary>
    public const int MaxContentLength = 4000;

    private static readonly LexiconIndex Index = LexiconIndex.Build();

    public ModerationResult Check(string? content, ContentModerationType type)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ModerationResult.Allowed;
        }

        if (content.Length > MaxContentLength)
        {
            return ModerationResult.Rejected($"{FieldLabel(type)} çok uzun.", ModerationCategory.TooLong);
        }

        var normalized = ModerationTextNormalizer.Normalize(content);
        var isUsername = type == ContentModerationType.Username;

        var term = FindTerm(normalized, isUsername);
        if (term is not null)
        {
            return ModerationResult.Rejected(RejectionMessage(type, term.Category), term.Category, term.Text);
        }

        if (isUsername && Index.FindReservedName(normalized) is { } reserved)
        {
            return ModerationResult.Rejected(
                "Bu kullanıcı adı sistem hesaplarına ayrılmıştır. Lütfen başka bir kullanıcı adı seç.",
                ModerationCategory.ReservedName,
                reserved);
        }

        return ModerationResult.Allowed;
    }

    private static ModerationTerm? FindTerm(NormalizedContent normalized, bool isUsername)
    {
        foreach (var token in normalized.Tokens)
        {
            if (Index.MatchWord(token, isUsername) is { } term) return term;
        }

        foreach (var candidate in normalized.Joined)
        {
            if (Index.MatchWord(candidate, isUsername) is { } term) return term;
        }

        if (Index.MatchPhrase(normalized.Tokens, isUsername) is { } phrase) return phrase;

        if (isUsername)
        {
            if (Index.MatchWord(normalized.Concatenated, isUsername) is { } whole) return whole;
            if (Index.MatchEmbedded(normalized.Concatenated, isUsername) is { } embedded) return embedded;
        }

        return null;
    }

    private static string FieldLabel(ContentModerationType type) => type switch
    {
        ContentModerationType.FirstName => "Ad",
        ContentModerationType.LastName => "Soyad",
        ContentModerationType.Username => "Kullanıcı adı",
        ContentModerationType.ProfileBio => "Profil açıklaması",
        _ => "Metin",
    };

    private static string RejectionMessage(ContentModerationType type, ModerationCategory category)
    {
        var reason = category switch
        {
            ModerationCategory.Profanity => "küfür içeremez",
            ModerationCategory.Insult => "hakaret içeremez",
            ModerationCategory.Sexual => "cinsel içerikli ifadeler içeremez",
            ModerationCategory.Threat => "tehdit veya aşağılayıcı ifadeler içeremez",
            ModerationCategory.HateSpeech => "ırkçı veya ayrımcı ifadeler içeremez",
            _ => "uygunsuz bir ifade içeriyor",
        };

        return $"{FieldLabel(type)} {reason}. Lütfen düzenleyip tekrar dene.";
    }

    /// <summary><see cref="ModerationLexicon"/>'in normallestirilmis, hash tabanli indeksi.</summary>
    private sealed class LexiconIndex
    {
        private readonly Dictionary<TextFormKind, FormIndex> _forms = new()
        {
            [TextFormKind.Folded] = new FormIndex(),
            [TextFormKind.FoldedDoubles] = new FormIndex(),
            [TextFormKind.Turkish] = new FormIndex(),
        };

        private readonly HashSet<string> _allowedFolded = new(StringComparer.Ordinal);
        private readonly HashSet<string> _allowedTurkish = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);
        private readonly List<string> _brands = new();
        private readonly List<string> _staffRoles = new();
        private int _maxAllowedLength;

        public static LexiconIndex Build()
        {
            var index = new LexiconIndex();
            foreach (var term in ModerationLexicon.Terms)
            {
                index.Add(term);
            }

            var folded = index._forms[TextFormKind.Folded];
            foreach (var word in ModerationLexicon.AllowedWords)
            {
                var forms = ModerationTextNormalizer.NormalizeTerm(word);
                index._allowedTurkish.Add(forms.Turkish);

                // ASCII bicimi bir sozluk ifadesinin kendisiyse ("şıktır" -> "siktir") yalnizca Turkce yazimi izinlidir;
                // aksi halde ASCII yazilan kullanici adlari icin katlanmis bicim de izinlidir ("asiktir").
                if (!folded.Contains(forms.Folded))
                {
                    index._allowedFolded.Add(forms.Folded);
                }

                index._maxAllowedLength = Math.Max(index._maxAllowedLength, Math.Max(forms.Folded.Length, forms.Turkish.Length));
            }

            foreach (var name in ModerationLexicon.ReservedUsernames)
            {
                index._reserved.Add(ModerationTextNormalizer.NormalizeTerm(name).Folded);
            }

            index._brands.AddRange(ModerationLexicon.BrandNames.Select(b => ModerationTextNormalizer.NormalizeTerm(b).Folded));
            index._staffRoles.AddRange(ModerationLexicon.StaffRoleWords.Select(r => ModerationTextNormalizer.NormalizeTerm(r).Folded));
            return index;
        }

        private void Add(ModerationTerm term)
        {
            var kind = term.DiacriticSensitive ? TextFormKind.Turkish
                : term.KeepDoubleLetters ? TextFormKind.FoldedDoubles
                : TextFormKind.Folded;
            var form = _forms[kind];
            var words = ModerationTextNormalizer.Normalize(term.Text).Tokens.Select(t => t.Get(kind)).ToArray();

            if (words.Length == 0)
            {
                return;
            }

            if (words.Length > 1)
            {
                form.AddPhrase(words, term);
                return;
            }

            var key = words[0];
            switch (term.Match)
            {
                case TermMatch.Word:
                    form.Exact.TryAdd(key, term);
                    break;
                case TermMatch.Stem:
                    form.AddStem(key, term);
                    break;
                case TermMatch.Embedded:
                    form.AddStem(key, term);
                    form.AddEmbedded(key, term);
                    break;
            }
        }

        public ModerationTerm? MatchWord(TextForms candidate, bool isUsername)
        {
            foreach (var (kind, form) in _forms)
            {
                var value = candidate.Get(kind);
                if (value.Length == 0)
                {
                    continue;
                }

                if (form.Exact.TryGetValue(value, out var exact) && Applies(exact, isUsername))
                {
                    return exact;
                }

                if (form.StemLookup is { } stems && !IsAllowedWord(candidate))
                {
                    var span = value.AsSpan();
                    var max = Math.Min(form.MaxStemLength, span.Length);
                    for (var length = form.MinStemLength; length <= max; length++)
                    {
                        if (stems.TryGetValue(span[..length], out var stem) && Applies(stem, isUsername)
                            && !IsInsideAllowedWord(candidate, 0, length))
                        {
                            return stem;
                        }
                    }
                }
            }

            return null;
        }

        public ModerationTerm? MatchPhrase(IReadOnlyList<TextForms> tokens, bool isUsername)
        {
            foreach (var (kind, form) in _forms)
            {
                if (form.Phrases.Count == 0)
                {
                    continue;
                }

                for (var i = 0; i < tokens.Count; i++)
                {
                    if (!form.Phrases.TryGetValue(tokens[i].Get(kind), out var phrases))
                    {
                        continue;
                    }

                    foreach (var (words, term) in phrases)
                    {
                        if (i + words.Length > tokens.Count || !Applies(term, isUsername))
                        {
                            continue;
                        }

                        var matched = true;
                        for (var w = 1; w < words.Length && matched; w++)
                        {
                            matched = string.Equals(tokens[i + w].Get(kind), words[w], StringComparison.Ordinal);
                        }

                        if (matched)
                        {
                            return term;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>Kullanici adinin bitisik halinde ayirt edici ifadeleri arar (O(uzunluk x en uzun ifade)).</summary>
        public ModerationTerm? MatchEmbedded(TextForms concatenated, bool isUsername)
        {
            foreach (var (kind, form) in _forms)
            {
                if (form.EmbeddedLookup is not { } embedded)
                {
                    continue;
                }

                var span = concatenated.Get(kind).AsSpan();
                for (var start = 0; start < span.Length; start++)
                {
                    var max = Math.Min(form.MaxEmbeddedLength, span.Length - start);
                    for (var length = form.MinEmbeddedLength; length <= max; length++)
                    {
                        if (embedded.TryGetValue(span.Slice(start, length), out var term) && Applies(term, isUsername)
                            && !IsInsideAllowedWord(concatenated, start, length))
                        {
                            return term;
                        }
                    }
                }
            }

            return null;
        }

        public string? FindReservedName(NormalizedContent normalized)
        {
            foreach (var candidate in new[] { normalized.Concatenated.Folded, normalized.ConcatenatedLettersOnly })
            {
                if (_reserved.Contains(candidate))
                {
                    return candidate;
                }

                if (_brands.Any(b => candidate.Contains(b, StringComparison.Ordinal))
                    && _staffRoles.Any(r => candidate.Contains(r, StringComparison.Ordinal)))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool Applies(ModerationTerm term, bool isUsername) => isUsername || !term.UsernameOnly;

        private bool IsAllowedWord(TextForms candidate) =>
            _allowedFolded.Contains(candidate.Folded) || _allowedTurkish.Contains(candidate.Turkish);

        /// <summary>
        /// [start, start+length) araligi metindeki izinli bir kelimenin icinde mi? ("saksofon" icindeki "sakso").
        /// Katlanmis ve Turkce bicimlerin ikisinde de bakilir; konumlar katlanmis bicime goredir (bicimler ayni uzunlukta
        /// degilse Turkce bicim yalnizca bu konum gecerliyse kullanilir).
        /// </summary>
        private bool IsInsideAllowedWord(TextForms candidate, int start, int length) =>
            IsInsideAllowedWord(_allowedFolded, candidate.Folded, start, length)
            || IsInsideAllowedWord(_allowedTurkish, candidate.Turkish, start, length);

        private bool IsInsideAllowedWord(HashSet<string> allowed, string text, int start, int length)
        {
            if (allowed.Count == 0 || start + length > text.Length)
            {
                return false;
            }

            var lookup = allowed.GetAlternateLookup<ReadOnlySpan<char>>();
            var span = text.AsSpan();
            var end = start + length;
            for (var from = Math.Max(0, end - _maxAllowedLength); from <= start; from++)
            {
                var maxLength = Math.Min(_maxAllowedLength, span.Length - from);
                for (var allowedLength = end - from; allowedLength <= maxLength; allowedLength++)
                {
                    if (lookup.Contains(span.Slice(from, allowedLength)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>Tek bir karsilastirma bicimi (<see cref="TextFormKind"/>) icin ifadeler.</summary>
    private sealed class FormIndex
    {
        private readonly Dictionary<string, ModerationTerm> _stems = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ModerationTerm> _embedded = new(StringComparer.Ordinal);

        public Dictionary<string, ModerationTerm> Exact { get; } = new(StringComparer.Ordinal);

        /// <summary>Ilk kelime -> (tum kelimeler, ifade).</summary>
        public Dictionary<string, List<(string[] Words, ModerationTerm Term)>> Phrases { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ModerationTerm>.AlternateLookup<ReadOnlySpan<char>>? StemLookup { get; private set; }

        public Dictionary<string, ModerationTerm>.AlternateLookup<ReadOnlySpan<char>>? EmbeddedLookup { get; private set; }

        public int MinStemLength { get; private set; } = int.MaxValue;

        public int MaxStemLength { get; private set; }

        public int MinEmbeddedLength { get; private set; } = int.MaxValue;

        public int MaxEmbeddedLength { get; private set; }

        /// <summary>Anahtar bir kelime/kok ifadesi olarak kayitli mi?</summary>
        public bool Contains(string key) => Exact.ContainsKey(key) || _stems.ContainsKey(key);

        public void AddStem(string key, ModerationTerm term)
        {
            if (_stems.TryAdd(key, term))
            {
                MinStemLength = Math.Min(MinStemLength, key.Length);
                MaxStemLength = Math.Max(MaxStemLength, key.Length);
                StemLookup = _stems.GetAlternateLookup<ReadOnlySpan<char>>();
            }
        }

        public void AddEmbedded(string key, ModerationTerm term)
        {
            if (_embedded.TryAdd(key, term))
            {
                MinEmbeddedLength = Math.Min(MinEmbeddedLength, key.Length);
                MaxEmbeddedLength = Math.Max(MaxEmbeddedLength, key.Length);
                EmbeddedLookup = _embedded.GetAlternateLookup<ReadOnlySpan<char>>();
            }
        }

        public void AddPhrase(string[] words, ModerationTerm term)
        {
            if (!Phrases.TryGetValue(words[0], out var list))
            {
                list = new List<(string[], ModerationTerm)>();
                Phrases[words[0]] = list;
            }

            list.Add((words, term));
        }
    }
}
