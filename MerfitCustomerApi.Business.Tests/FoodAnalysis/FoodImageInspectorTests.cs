using System.Text;
using MerfitCustomerApi.Business.Services.FoodAnalysis;
using Xunit;
using static MerfitCustomerApi.Business.Tests.FoodAnalysis.FoodAnalysisTestData;

namespace MerfitCustomerApi.Business.Tests.FoodAnalysis;

/// <summary>Dosya turu Content-Type'a degil imzaya bakilarak belirlenir; kesilmis ve bozuk dosyalar reddedilir.</summary>
public class FoodImageInspectorTests
{
    [Fact]
    public void ValidJpeg_ReturnsTypeAndDimensions()
    {
        var info = FoodImageInspector.Inspect(Jpeg(1280, 960));

        Assert.Equal(new FoodImageInfo("image/jpeg", 1280, 960), info);
    }

    [Fact]
    public void ValidPngAndWebp_AreRecognised()
    {
        Assert.Equal(new FoodImageInfo("image/png", 800, 600), FoodImageInspector.Inspect(Png(800, 600)));
        Assert.Equal(new FoodImageInfo("image/webp", 1024, 768), FoodImageInspector.Inspect(WebpVp8X(1024, 768)));
    }

    [Fact]
    public void TruncatedJpeg_IsRejected()
    {
        Assert.Null(FoodImageInspector.Inspect(Jpeg(withEnd: false)));
        Assert.Null(FoodImageInspector.Inspect(Jpeg()[..40]));
    }

    [Fact]
    public void TruncatedPngAndWebp_AreRejected()
    {
        var png = Png();
        Assert.Null(FoodImageInspector.Inspect(png[..^12]));
        var webp = WebpVp8X();
        Assert.Null(FoodImageInspector.Inspect(webp[..^6]));
    }

    [Theory]
    [InlineData("GIF89a............................................")]
    [InlineData("%PDF-1.7 ..........................................")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>......")]
    public void UnsupportedFormats_AreRejected(string content)
    {
        Assert.Null(FoodImageInspector.Inspect(Encoding.ASCII.GetBytes(content)));
    }

    [Fact]
    public void EmptyOrTinyInput_IsRejected()
    {
        Assert.Null(FoodImageInspector.Inspect(Array.Empty<byte>()));
        Assert.Null(FoodImageInspector.Inspect(new byte[] { 0xFF, 0xD8, 0xFF }));
    }

    [Fact]
    public void JpegWithoutFrameHeader_IsRejected()
    {
        // SOI + SOS + EOI: boyut bilgisi yok.
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00 }
            .Concat(Enumerable.Repeat((byte)0x11, 40)).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
        Assert.Null(FoodImageInspector.Inspect(bytes));
    }
}
