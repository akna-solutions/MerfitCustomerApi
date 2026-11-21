using System.Buffers.Binary;
using MerfitCustomerApi.Domain.Exceptions;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// Yorum fotografi dogrulama ve temizleme. Harici bir goruntu kutuphanesi kullanmadan:
/// - Turu dosyanin ilk baytlarindan (magic number) tespit eder; istemcinin bildirdigi Content-Type yalnizca izinli
///   turlerden biri olmalidir ve icerikle uyusmalidir (JPEG, PNG, WebP).
/// - Dosya yapisini (segment/chunk uzunluklari) ayristirir; bozuk veya kesik dosyayi reddeder.
/// - Boyutlari basliktan okur; cok kucuk, cok buyuk veya asiri piksel sayili ("decompression bomb") gorseli reddeder.
/// - Hareketli gorselleri (APNG, animasyonlu WebP) reddeder.
/// - Konum (GPS) ve cihaz bilgisi tasiyabilen meta verileri siler: JPEG APP1-APP15 (ICC profili haric) ve COM;
///   PNG tEXt/zTXt/iTXt/eXIf/tIME; WebP EXIF/XMP.
/// Yeniden boyutlandirma ve sikistirma mobil uygulamada yapilir (uzun kenar 1600 px, JPEG); sunucu bayt ve piksel
/// sinirlarini zorunlu tutar, boylece gereksiz buyuk gorsel saklanmaz.
/// </summary>
public static class StoreReviewImageProcessor
{
    public const int MaxBytes = 3 * 1024 * 1024;
    public const int MinDimension = 64;
    public const int MaxDimension = 6000;
    public const long MaxPixels = 30_000_000;

    public static readonly IReadOnlyList<string> AllowedContentTypes = new[] { "image/jpeg", "image/png", "image/webp" };

    public sealed record Result(byte[] Content, string ContentType, int Width, int Height);

    public static Result Process(byte[] data, string? declaredContentType)
    {
        if (data.Length == 0) throw Invalid("Fotoğraf boş.");
        if (data.Length > MaxBytes) throw Invalid($"Fotoğraf en fazla {MaxBytes / (1024 * 1024)} MB olabilir.");

        var declared = NormalizeContentType(declaredContentType);
        if (declared is not null && !AllowedContentTypes.Contains(declared))
        {
            throw Invalid("Yalnızca JPEG, PNG veya WebP fotoğraf yükleyebilirsin.");
        }

        var detected = Detect(data) ?? throw Invalid("Yalnızca JPEG, PNG veya WebP fotoğraf yükleyebilirsin.");
        if (declared is not null && declared != detected)
        {
            throw Invalid("Dosya türü içerikle uyuşmuyor.");
        }

        var (content, width, height) = detected switch
        {
            "image/jpeg" => ProcessJpeg(data),
            "image/png" => ProcessPng(data),
            _ => ProcessWebp(data),
        };

        if (width < MinDimension || height < MinDimension) throw Invalid($"Fotoğraf en az {MinDimension}×{MinDimension} piksel olmalı.");
        if (width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
        {
            throw Invalid("Fotoğrafın çözünürlüğü çok yüksek. Lütfen daha küçük bir fotoğraf seç.");
        }

        return new Result(content, detected, width, height);
    }

    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 8 && data[..8].SequenceEqual(PngSignature)) return "image/png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    private static string? NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var type = value.Split(';')[0].Trim().ToLowerInvariant();
        if (type is "application/octet-stream") return null;
        return type == "image/jpg" || type == "image/pjpeg" ? "image/jpeg" : type;
    }

    private static AppValidationException Invalid(string message) => new("Photo", message);

    // ---------------- JPEG ----------------

    private static (byte[], int, int) ProcessJpeg(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2); // SOI
        var pos = 2;
        int width = 0, height = 0;

        while (true)
        {
            if (pos + 4 > data.Length) throw Invalid("Fotoğraf bozuk veya eksik.");
            if (data[pos] != 0xFF) throw Invalid("Fotoğraf bozuk veya eksik.");
            var marker = data[pos + 1];
            if (marker == 0xFF) { pos++; continue; } // dolgu baytlari
            if (marker == 0xD9) throw Invalid("Fotoğraf bozuk veya eksik.");
            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                output.Write(data, pos, 2);
                pos += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2, 2));
            if (length < 2 || pos + 2 + length > data.Length) throw Invalid("Fotoğraf bozuk veya eksik.");
            var segmentEnd = pos + 2 + length;

            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 7) throw Invalid("Fotoğraf bozuk veya eksik.");
                height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 7, 2));
            }

            var keep = marker switch
            {
                0xE0 => true, // JFIF
                0xE2 => length >= 14 && data.AsSpan(pos + 4, 11).SequenceEqual("ICC_PROFILE"u8), // renk profili
                0xEE => true, // Adobe renk donusumu
                >= 0xE1 and <= 0xEF => false, // EXIF (GPS), XMP, maker notes ...
                0xFE => false, // yorum
                _ => true,
            };
            if (keep) output.Write(data, pos, 2 + length);

            if (marker == 0xDA)
            {
                // Tarama verisi: dosyanin geri kalani (EOI dahil) aynen kopyalanir.
                if (width == 0 || height == 0) throw Invalid("Fotoğraf bozuk veya eksik.");
                if (data.Length < segmentEnd + 2 || data[^2] != 0xFF || data[^1] != 0xD9) throw Invalid("Fotoğraf bozuk veya eksik.");
                output.Write(data, segmentEnd, data.Length - segmentEnd);
                return (output.ToArray(), width, height);
            }

            pos = segmentEnd;
        }
    }

    // ---------------- PNG ----------------

    private static ReadOnlySpan<byte> PngSignature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly HashSet<string> PngDroppedChunks = new(StringComparer.Ordinal) { "tEXt", "zTXt", "iTXt", "eXIf", "tIME" };

    private static (byte[], int, int) ProcessPng(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 8);
        var pos = 8;
        int width = 0, height = 0;
        var first = true;

        while (true)
        {
            if (pos + 12 > data.Length) throw Invalid("Fotoğraf bozuk veya eksik.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
            if (length > int.MaxValue || pos + 12 + (long)length > data.Length) throw Invalid("Fotoğraf bozuk veya eksik.");
            var type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            var total = 12 + (int)length;

            if (first)
            {
                if (type != "IHDR" || length < 8) throw Invalid("Fotoğraf bozuk veya eksik.");
                width = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8, 4)));
                height = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 12, 4)));
                first = false;
            }

            if (type == "acTL") throw Invalid("Hareketli görseller desteklenmiyor.");
            if (!PngDroppedChunks.Contains(type)) output.Write(data, pos, total);
            pos += total;
            if (type == "IEND") return (output.ToArray(), width, height);
        }
    }

    // ---------------- WebP ----------------

    private static (byte[], int, int) ProcessWebp(byte[] data)
    {
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        if (riffSize < 4 || 8 + (long)riffSize > data.Length) throw Invalid("Fotoğraf bozuk veya eksik.");
        var end = 8 + (int)riffSize;

        using var body = new MemoryStream(data.Length);
        body.Write("WEBP"u8);
        var pos = 12;
        int width = 0, height = 0;
        long vp8xFlagsOffset = -1;

        while (pos + 8 <= end)
        {
            var fourcc = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4, 4));
            var padded = size + (size & 1);
            if (pos + 8 + (long)size > end) throw Invalid("Fotoğraf bozuk veya eksik.");
            var payload = data.AsSpan(pos + 8, (int)size);

            switch (fourcc)
            {
                case "VP8X":
                    if (size < 10) throw Invalid("Fotoğraf bozuk veya eksik.");
                    if ((payload[0] & 0x02) != 0) throw Invalid("Hareketli görseller desteklenmiyor.");
                    width = 1 + (payload[4] | payload[5] << 8 | payload[6] << 16);
                    height = 1 + (payload[7] | payload[8] << 8 | payload[9] << 16);
                    vp8xFlagsOffset = body.Position + 8;
                    break;
                case "VP8 ":
                    if (size < 10 || payload[3] != 0x9D || payload[4] != 0x01 || payload[5] != 0x2A) throw Invalid("Fotoğraf bozuk veya eksik.");
                    if (width == 0)
                    {
                        width = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)) & 0x3FFF;
                        height = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)) & 0x3FFF;
                    }
                    break;
                case "VP8L":
                    if (size < 5 || payload[0] != 0x2F) throw Invalid("Fotoğraf bozuk veya eksik.");
                    if (width == 0)
                    {
                        var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4));
                        width = (int)(bits & 0x3FFF) + 1;
                        height = (int)((bits >> 14) & 0x3FFF) + 1;
                    }
                    break;
                case "ANIM":
                case "ANMF":
                    throw Invalid("Hareketli görseller desteklenmiyor.");
            }

            if (fourcc is not ("EXIF" or "XMP "))
            {
                var copied = (int)Math.Min(padded, (uint)(end - pos - 8));
                body.Write(data, pos, 8 + copied);
                if (copied < padded) body.WriteByte(0);
            }

            pos += 8 + (int)padded;
        }

        if (width == 0 || height == 0) throw Invalid("Fotoğraf bozuk veya eksik.");

        var bodyBytes = body.ToArray();
        if (vp8xFlagsOffset >= 0)
        {
            bodyBytes[vp8xFlagsOffset] &= unchecked((byte)~0x0C); // EXIF ve XMP bayraklari
        }

        var result = new byte[8 + bodyBytes.Length];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)bodyBytes.Length);
        bodyBytes.CopyTo(result, 8);
        return (result, width, height);
    }
}
