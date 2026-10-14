using System.Buffers.Binary;
using MerfitCustomerApi.Business.Interfaces.Services;

namespace MerfitCustomerApi.Business.Tests.FoodAnalysis;

/// <summary>Fotograf analizi testleri icin gecerli/bozuk goruntu baytlari ve sahte saglayici.</summary>
internal static class FoodAnalysisTestData
{
    /// <summary>Basliklari gecerli, en kucuk JPEG (SOI, APP0, SOF0, SOS, EOI). Piksel verisi cozulmez.</summary>
    public static byte[] Jpeg(int width = 1280, int height = 960, bool withEnd = true)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 });
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08 });
        bytes.AddRange(BigEndian16(height));
        bytes.AddRange(BigEndian16(width));
        bytes.AddRange(new byte[] { 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01 });
        bytes.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00 });
        bytes.AddRange(Enumerable.Repeat((byte)0x55, 256));
        if (withEnd) bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return bytes.ToArray();
    }

    public static byte[] Png(int width = 800, int height = 600)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndian32(width));
        bytes.AddRange(BigEndian32(height));
        bytes.AddRange(new byte[] { 0x08, 0x02, 0x00, 0x00, 0x00, 0, 0, 0, 0 });
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        bytes.AddRange("IEND"u8.ToArray());
        bytes.AddRange(new byte[] { 0xAE, 0x42, 0x60, 0x82 });
        return bytes.ToArray();
    }

    public static byte[] WebpVp8X(int width = 1024, int height = 768)
    {
        var bytes = new List<byte>();
        bytes.AddRange("RIFF"u8.ToArray());
        bytes.AddRange(new byte[4]);
        bytes.AddRange("WEBP"u8.ToArray());
        bytes.AddRange("VP8X"u8.ToArray());
        bytes.AddRange(new byte[] { 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        bytes.AddRange(Little24(width - 1));
        bytes.AddRange(Little24(height - 1));
        bytes.AddRange(new byte[16]);
        var array = bytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(array.AsSpan(4, 4), (uint)(array.Length - 8));
        return array;
    }

    private static byte[] BigEndian16(int value) => new[] { (byte)(value >> 8), (byte)value };
    private static byte[] BigEndian32(int value) => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
    private static byte[] Little24(int value) => new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16) };

    /// <summary>Iki kalemli gecerli model yaniti: katalogda olan pilav + katalogda olmayan bir yemek.</summary>
    public const string ValidModelJson = """
        {
          "containsFood": true,
          "mealName": "Pilav ve mantı",
          "items": [
            {"name": "Pirinç Pilavı", "genericName": "pirinç pilavı", "estimatedGrams": 180, "minGrams": 140, "maxGrams": 220,
             "caloriesPer100g": 150, "proteinPer100g": 3, "carbsPer100g": 28, "fatPer100g": 3.5, "confidence": 0.9, "portionUncertainty": "low"},
            {"name": "Yoğurtlu mantı", "genericName": "mantı", "estimatedGrams": 250, "minGrams": 150, "maxGrams": 350,
             "caloriesPer100g": 170, "proteinPer100g": 7, "carbsPer100g": 20, "fatPer100g": 7, "confidence": 0.7, "portionUncertainty": "high",
             "note": "Tereyağlı sosun miktarı görünmüyor."}
          ],
          "unidentifiedItems": ["kasede koyu renkli sos"],
          "overallConfidence": 0.8
        }
        """;
}

/// <summary>Sahte goruntu saglayicisi: siradaki yaniti ya da hatayi dondurur, gelen istekleri kaydeder.</summary>
internal sealed class FakeImageProvider : IFoodImageAnalysisProvider
{
    private readonly Queue<Func<FoodImageAnalysisProviderResult>> _responses = new();

    public List<FoodImageAnalysisProviderRequest> Requests { get; } = new();
    public bool Configured { get; set; } = true;

    public string Name => "Fake";
    public bool IsConfigured => Configured;
    public string Model => "fake-vision";

    public FakeImageProvider Returns(string json, bool refused = false)
    {
        _responses.Enqueue(() => new FoodImageAnalysisProviderResult(json, 1200, 300, "STOP", Model, refused, Name));
        return this;
    }

    public FakeImageProvider Throws(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    public Task<FoodImageAnalysisProviderResult> AnalyzeAsync(FoodImageAnalysisProviderRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (_responses.Count == 0) throw new InvalidOperationException("Beklenmeyen saglayici cagrisi.");
        return Task.FromResult(_responses.Dequeue()());
    }
}

internal sealed class FakeClock : TimeProvider
{
    public FakeClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; set; }
    public override DateTimeOffset GetUtcNow() => Now;
}
