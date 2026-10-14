using System.Buffers.Binary;

namespace MerfitCustomerApi.Business.Services.FoodAnalysis;

/// <summary>
/// Yuklenen dosyanin gercekten desteklenen bir goruntu oldugunu, istemcinin bildirdigi Content-Type'a degil dosya
/// imzasina (magic bytes) ve basliktaki boyut bilgisine bakarak dogrular. Ek kutuphane kullanmaz; goruntuyu cozmez,
/// yalnizca bicim ve butunluk kontrolu yapar (kesilmis JPEG/PNG reddedilir).
/// </summary>
public static class FoodImageInspector
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";

    public static FoodImageInfo? Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length < 32) return null;

        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return InspectJpeg(data);
        if (data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return InspectPng(data);
        if (data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return InspectWebp(data);
        return null;
    }

    private static FoodImageInfo? InspectJpeg(ReadOnlySpan<byte> data)
    {
        // Dosya sonu isareti (EOI, FF D9): kesilmis yuklemeler burada yakalanir. Bazi cihazlar sona dolgu ekler.
        var tail = data[^Math.Min(data.Length, 4096)..];
        if (tail.LastIndexOf(new byte[] { 0xFF, 0xD9 }) < 0) return null;

        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xFF) return null;
            var marker = data[offset + 1];
            if (marker == 0xFF) { offset++; continue; }
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { offset += 2; continue; }
            if (marker is 0xD9 or 0xDA) return null; // Boyut bilgisinden once veri/son: bozuk dosya.

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 2, 2));
            if (length < 2 || offset + 2 + length > data.Length) return null;

            // SOF0-SOF15 (DHT=C4, JPG=C8, DAC=CC haric): yukseklik ve genislik.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 7) return null;
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 7, 2));
                return width == 0 || height == 0 ? null : new FoodImageInfo(Jpeg, width, height);
            }

            offset += 2 + length;
        }
        return null;
    }

    private static FoodImageInfo? InspectPng(ReadOnlySpan<byte> data)
    {
        // Ilk parca IHDR olmali; son parca IEND (kesilmis dosya kontrolu).
        if (!data.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        if (data[^8..^4].SequenceEqual("IEND"u8) is false) return null;
        var width = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        return width is 0 or > int.MaxValue || height is 0 or > int.MaxValue ? null : new FoodImageInfo(Png, (int)width, (int)height);
    }

    private static FoodImageInfo? InspectWebp(ReadOnlySpan<byte> data)
    {
        // RIFF boyutu dosya boyutuyla uyusmali (kesilmis dosya kontrolu).
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (riffSize + 8L > data.Length || riffSize < 20) return null;

        var chunk = data.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8) && data.Length >= 30)
        {
            var width = 1 + (data[24] | data[25] << 8 | data[26] << 16);
            var height = 1 + (data[27] | data[28] << 8 | data[29] << 16);
            return new FoodImageInfo(Webp, width, height);
        }
        if (chunk.SequenceEqual("VP8 "u8) && data.Length >= 30)
        {
            // Anahtar kare imzasi 9D 01 2A, ardindan 14 bitlik genislik/yukseklik.
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A) return null;
            var width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF;
            return width == 0 || height == 0 ? null : new FoodImageInfo(Webp, width, height);
        }
        if (chunk.SequenceEqual("VP8L"u8) && data.Length >= 25)
        {
            if (data[20] != 0x2F) return null;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(21, 4));
            return new FoodImageInfo(Webp, (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }
        return null;
    }
}

public sealed record FoodImageInfo(string MimeType, int Width, int Height);
