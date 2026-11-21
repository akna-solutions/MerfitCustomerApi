using System.Buffers.Binary;
using System.Text;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>Testler icin kucuk, yapisal olarak gecerli gorseller (meta veri iceren). Piksel verisi cozulmez.</summary>
internal static class ReviewTestImages
{
    public const string SecretMarker = "GPS-41.0082N-28.9784E";

    public static byte[] Jpeg(int width = 800, int height = 600, bool withExif = true)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0xFF, 0xD8 });
        Segment(ms, 0xE0, Encoding.ASCII.GetBytes("JFIF\0\u0001\u0001\0\0\u0001\0\u0001\0\0"));
        if (withExif) Segment(ms, 0xE1, Encoding.ASCII.GetBytes("Exif\0\0" + SecretMarker));
        Segment(ms, 0xFE, Encoding.ASCII.GetBytes("comment " + SecretMarker));
        var sof = new byte[15];
        sof[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(1), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(3), (ushort)width);
        sof[5] = 3;
        Segment(ms, 0xC0, sof);
        Segment(ms, 0xDA, new byte[] { 3, 1, 0, 2, 0x11, 3, 0x11, 0, 0x3F, 0 });
        ms.Write(new byte[] { 0x12, 0x34, 0xFF, 0x00, 0x56, 0x78 });
        ms.Write(new byte[] { 0xFF, 0xD9 });
        return ms.ToArray();
    }

    public static byte[] Png(int width = 640, int height = 480, bool animated = false)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk(ms, "IHDR", ihdr);
        if (animated) Chunk(ms, "acTL", new byte[8]);
        Chunk(ms, "tEXt", Encoding.ASCII.GetBytes("Comment\0" + SecretMarker));
        Chunk(ms, "IDAT", new byte[] { 0x78, 0x9C, 0x01, 0x02, 0x03 });
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    public static byte[] Webp(int width = 500, int height = 400)
    {
        var vp8x = new byte[10];
        vp8x[0] = 0x08; // EXIF bayragi
        WriteUInt24(vp8x.AsSpan(4), width - 1);
        WriteUInt24(vp8x.AsSpan(7), height - 1);
        var vp8 = new byte[11];
        vp8[3] = 0x9D; vp8[4] = 0x01; vp8[5] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(vp8.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(vp8.AsSpan(8), (ushort)height);
        using var body = new MemoryStream();
        body.Write("WEBP"u8);
        RiffChunk(body, "VP8X", vp8x);
        RiffChunk(body, "VP8 ", vp8);
        RiffChunk(body, "EXIF", Encoding.ASCII.GetBytes(SecretMarker));
        var bytes = body.ToArray();
        var result = new byte[8 + bytes.Length];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)bytes.Length);
        bytes.CopyTo(result, 8);
        return result;
    }

    private static void Segment(Stream s, byte marker, byte[] payload)
    {
        s.WriteByte(0xFF);
        s.WriteByte(marker);
        var len = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)(payload.Length + 2));
        s.Write(len);
        s.Write(payload);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(data);
        s.Write(new byte[4]); // CRC (cozulmedigi icin dogrulanmaz)
    }

    private static void RiffChunk(Stream s, string fourcc, byte[] data)
    {
        s.Write(Encoding.ASCII.GetBytes(fourcc));
        var len = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(data);
        if (data.Length % 2 == 1) s.WriteByte(0);
    }

    private static void WriteUInt24(Span<byte> span, int value)
    {
        span[0] = (byte)value;
        span[1] = (byte)(value >> 8);
        span[2] = (byte)(value >> 16);
    }
}
