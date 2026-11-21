using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Api.Filters;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MB Shop urun yorumlari ve degerlendirmeleri. Kullanici kimligi yalnizca JWT'den alinir; govdede UserId, onay veya
/// yayin durumu kabul edilmez. Yazma uclari StoreRateLimits "write" penceresine tabidir.
/// </summary>
[ApiController]
[Route("api/store")]
[Authorize]
public class StoreReviewsController : ControllerBase
{
    /// <summary>En buyuk fotograf + multipart payi; asilirsa govde okunmadan 413 doner.</summary>
    public const long MaxImageRequestBytes = StoreReviewImageProcessor.MaxBytes + 256 * 1024;

    private readonly IStoreReviewService _reviews;

    public StoreReviewsController(IStoreReviewService reviews)
    {
        _reviews = reviews;
    }

    private long UserId => User.GetUserId()!.Value;

    /// <summary>Ortalama puan, toplam yorum ve 5'ten 1'e yildiz dagilimi (yalnizca yayindaki yorumlar).</summary>
    [HttpGet("products/{productId:long}/reviews/summary")]
    public async Task<ActionResult<StoreReviewSummaryDto>> GetSummary(long productId, CancellationToken cancellationToken)
        => Ok(await _reviews.GetSummaryAsync(UserId, productId, cancellationToken));

    /// <summary>Sayfali yorum listesi: page, pageSize, sort (newest|oldest|rating_desc|rating_asc|helpful), rating, withPhotos, verifiedOnly.</summary>
    [HttpGet("products/{productId:long}/reviews")]
    public async Task<ActionResult<PagedResult<StoreReviewDto>>> GetList(long productId, [FromQuery] StoreReviewQuery query, CancellationToken cancellationToken)
        => Ok(await _reviews.GetListAsync(UserId, productId, query, cancellationToken));

    /// <summary>Kullanicinin bu urune ait yorumu; yoksa 204.</summary>
    [HttpGet("products/{productId:long}/reviews/mine")]
    public async Task<ActionResult<StoreReviewDto>> GetMine(long productId, CancellationToken cancellationToken)
    {
        var review = await _reviews.GetMineAsync(UserId, productId, cancellationToken);
        return review is null ? NoContent() : Ok(review);
    }

    /// <summary>201: yorum olusturuldu; 409 REVIEW_EXISTS: bu urune zaten aktif yorum var.</summary>
    [HttpPost("products/{productId:long}/reviews")]
    public async Task<ActionResult<StoreReviewDto>> Create(long productId, [FromBody] UpsertStoreReviewRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await _reviews.CreateAsync(UserId, productId, request, cancellationToken));

    [HttpPut("reviews/{reviewId:long}")]
    public async Task<ActionResult<StoreReviewDto>> Update(long reviewId, [FromBody] UpsertStoreReviewRequest request, CancellationToken cancellationToken)
        => Ok(await _reviews.UpdateAsync(UserId, reviewId, request, cancellationToken));

    [HttpDelete("reviews/{reviewId:long}")]
    public async Task<IActionResult> Delete(long reviewId, CancellationToken cancellationToken)
    {
        await _reviews.DeleteAsync(UserId, reviewId, cancellationToken);
        return NoContent();
    }

    /// <summary>Yoruma fotograf ekler (multipart/form-data, alan adi "photo"; JPEG, PNG veya WebP, en fazla 3 MB, yorum basina 5).</summary>
    [HttpPost("reviews/{reviewId:long}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageRequestBytes)]
    [DisableFormValueModelBinding]
    public async Task<ActionResult<StoreReviewImageDto>> AddImage(long reviewId, CancellationToken cancellationToken)
    {
        if (Request.ContentLength > MaxImageRequestBytes)
        {
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var photo = form.Files.GetFile("photo");
        if (photo is null || photo.Length == 0)
        {
            throw new AppValidationException("Photo", "Fotoğraf seçilmedi ya da boş.");
        }
        if (photo.Length > StoreReviewImageProcessor.MaxBytes)
        {
            throw new AppValidationException("Photo", $"Fotoğraf en fazla {StoreReviewImageProcessor.MaxBytes / (1024 * 1024)} MB olabilir.");
        }

        // Dosya diske yazilmaz; istemcinin dosya adi kullanilmaz.
        var buffer = new byte[photo.Length];
        await using (var stream = photo.OpenReadStream())
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken);
        }

        var image = await _reviews.AddImageAsync(UserId, reviewId, new StoreReviewImageUpload(buffer, photo.ContentType), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, image);
    }

    [HttpDelete("reviews/{reviewId:long}/images/{imageId:long}")]
    public async Task<IActionResult> RemoveImage(long reviewId, long imageId, CancellationToken cancellationToken)
    {
        await _reviews.RemoveImageAsync(UserId, reviewId, imageId, cancellationToken);
        return NoContent();
    }

    /// <summary>{ isHelpful: true|false } - oy verir veya degistirir.</summary>
    [HttpPut("reviews/{reviewId:long}/vote")]
    public async Task<ActionResult<StoreReviewVoteResponse>> Vote(long reviewId, [FromBody] VoteStoreReviewRequest request, CancellationToken cancellationToken)
        => Ok(await _reviews.VoteAsync(UserId, reviewId, request, cancellationToken));

    [HttpDelete("reviews/{reviewId:long}/vote")]
    public async Task<ActionResult<StoreReviewVoteResponse>> RemoveVote(long reviewId, CancellationToken cancellationToken)
        => Ok(await _reviews.RemoveVoteAsync(UserId, reviewId, cancellationToken));

    /// <summary>{ reason: spam|offensive|misleading|irrelevant|other, description }. 201 yeni rapor, 200 daha once raporlanmis.</summary>
    [HttpPost("reviews/{reviewId:long}/reports")]
    public async Task<ActionResult<ReportStoreReviewResponse>> Report(long reviewId, [FromBody] ReportStoreReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _reviews.ReportAsync(UserId, reviewId, request, cancellationToken);
        return result.AlreadyReported ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Yayindaki bir yorumun fotografi. Anahtar 128 bit rastgeledir ve yalnizca yorum listesinden ogrenilir; gorsel
    /// bilesenleri (expo-image, admin img) Authorization basligi gondermedigi icin kimlik istemez. Gizlenen veya silinen
    /// yorumun fotografi 404 doner.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("review-images/{key}")]
    public async Task<IActionResult> GetImage(string key, CancellationToken cancellationToken)
    {
        var file = await _reviews.GetImageAsync(key, cancellationToken);
        if (file is null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=86400";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(file.Content, file.ContentType);
    }
}
