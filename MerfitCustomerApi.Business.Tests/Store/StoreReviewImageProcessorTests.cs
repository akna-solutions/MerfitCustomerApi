using System.Text;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Domain.Exceptions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>Yorum fotografi dogrulamasi: tur tespiti, MIME uyumu, boyut sinirlari ve meta veri (EXIF/GPS) temizligi.</summary>
public class StoreReviewImageProcessorTests
{
    private static bool ContainsMarker(byte[] data) => Encoding.ASCII.GetString(data).Contains(ReviewTestImages.SecretMarker);

    [Fact]
    public void Jpeg_IsAccepted_DimensionsRead_AndExifAndCommentsStripped()
    {
        var input = ReviewTestImages.Jpeg(1200, 900);
        Assert.True(ContainsMarker(input));

        var result = StoreReviewImageProcessor.Process(input, "image/jpeg");

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal((1200, 900), (result.Width, result.Height));
        Assert.False(ContainsMarker(result.Content));
        Assert.Equal(0xD9, result.Content[^1]);
        Assert.True(result.Content.Length < input.Length);
    }

    [Fact]
    public void Png_IsAccepted_AndTextChunksStripped()
    {
        var result = StoreReviewImageProcessor.Process(ReviewTestImages.Png(640, 480), "image/png");
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal((640, 480), (result.Width, result.Height));
        Assert.False(ContainsMarker(result.Content));
        Assert.Contains("IEND", Encoding.ASCII.GetString(result.Content));
    }

    [Fact]
    public void Webp_IsAccepted_ExifChunkAndFlagRemoved()
    {
        var result = StoreReviewImageProcessor.Process(ReviewTestImages.Webp(500, 400), "image/webp");
        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal((500, 400), (result.Width, result.Height));
        Assert.False(ContainsMarker(result.Content));
        Assert.Equal(0, result.Content[20] & 0x08); // VP8X EXIF bayragi
        Assert.Equal(result.Content.Length - 8, BitConverter.ToInt32(result.Content, 4));
    }

    [Fact]
    public void ContentTypeIsDetectedFromBytes_OctetStreamOrMissingHeaderIsAllowed()
    {
        Assert.Equal("image/png", StoreReviewImageProcessor.Process(ReviewTestImages.Png(), null).ContentType);
        Assert.Equal("image/jpeg", StoreReviewImageProcessor.Process(ReviewTestImages.Jpeg(), "application/octet-stream").ContentType);
    }

    [Theory]
    [InlineData("image/png")] // jpeg icerik, png beyani
    [InlineData("image/gif")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    public void MismatchedOrDisallowedDeclaredType_IsRejected(string declared)
    {
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(ReviewTestImages.Jpeg(), declared));
    }

    [Fact]
    public void NonImageContent_IsRejected_EvenWithImageMimeType()
    {
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(html, "image/jpeg"));
        var gif = Encoding.ASCII.GetBytes("GIF89a......");
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(gif, null));
    }

    [Fact]
    public void TruncatedFiles_AreRejected()
    {
        var jpeg = ReviewTestImages.Jpeg();
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(jpeg[..(jpeg.Length - 2)], "image/jpeg"));
        var png = ReviewTestImages.Png();
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(png[..(png.Length - 12)], "image/png"));
    }

    [Fact]
    public void SizeAndDimensionLimits_AreEnforced()
    {
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(Array.Empty<byte>(), "image/jpeg"));
        var huge = new byte[StoreReviewImageProcessor.MaxBytes + 1];
        ReviewTestImages.Jpeg().CopyTo(huge, 0);
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(huge, "image/jpeg"));
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(ReviewTestImages.Jpeg(20, 20), "image/jpeg"));
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(ReviewTestImages.Png(7000, 100), "image/png"));
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(ReviewTestImages.Png(6000, 6000), "image/png"));
    }

    [Fact]
    public void AnimatedPng_IsRejected()
    {
        Assert.Throws<AppValidationException>(() => StoreReviewImageProcessor.Process(ReviewTestImages.Png(animated: true), "image/png"));
    }
}
