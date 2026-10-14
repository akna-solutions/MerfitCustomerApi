using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Api.Filters;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.FoodAnalysis;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp beslenme ekranindaki "Fotoğrafla ekle" akisi: yemek fotografindan kalori/makro tahmini ve
/// kullanici onayindan sonra gunluge kayit. Kullanici kimligi yalnizca JWT'den alinir; analiz kimligi servis
/// tarafinda sahiplik icin dogrulanir. Analiz sonucu otomatik kaydedilmez.
/// </summary>
[ApiController]
[Route("api/food-analysis")]
[Authorize]
public class FoodAnalysisController : ControllerBase
{
    /// <summary>
    /// Analiz isteginin ust siniri: en buyuk fotograf (FoodAnalysis:MaxImageBytes, varsayilan 4 MB) + multipart payi.
    /// Asilirsa sunucu govdeyi okumadan 413 doner.
    /// </summary>
    public const long MaxAnalyzeRequestBytes = 5 * 1024 * 1024;

    private readonly IFoodAnalysisService _foodAnalysisService;
    private readonly FoodAnalysisOptions _options;

    public FoodAnalysisController(IFoodAnalysisService foodAnalysisService, IOptions<FoodAnalysisOptions> options)
    {
        _foodAnalysisService = foodAnalysisService;
        _options = options.Value;
    }

    /// <summary>
    /// Analiz oncesi bilgi: ozellik acik mi, fotografin saglayiciya gonderildigine dair bilgilendirme metni ve
    /// son 24 saatte kalan analiz hakki. Saglayiciya istek atmaz, kotadan dusmez.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<FoodAnalysisStatusDto>> Status(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _foodAnalysisService.GetStatusAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Yemek fotografini analiz eder (multipart/form-data, alan adi "photo"; JPEG, PNG veya WebP). Kullanici ve IP
    /// basina dakikalik hiz siniri ile kullanici basina gunluk kota uygulanir (429 + Retry-After). Hicbir ogun kaydi
    /// olusturmaz; fotograf saklanmaz.
    /// </summary>
    [HttpPost("analyze")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAnalyzeRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAnalyzeRequestBytes)]
    [DisableFormValueModelBinding]
    public async Task<ActionResult<FoodAnalysisResultDto>> Analyze(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;

        // Bildirilen boyut sinirin ustundeyse govde hic okunmaz. Content-Length'siz (chunked) isteklerde sinir
        // Kestrel'de RequestSizeLimit ile uygulanir. Ikisi de ApiResponse ile 413 doner (bkz. ExceptionHandlingMiddleware).
        if (Request.ContentLength > MaxAnalyzeRequestBytes)
        {
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);
        }

        // Form elle okunur: boyut siniri asiminda model binding'in genel 400'u yerine ApiResponse ile 413 doner.
        var form = await Request.ReadFormAsync(cancellationToken);
        var photo = form.Files.GetFile("photo");
        if (photo is null || photo.Length == 0)
        {
            throw new AppValidationException("Photo", "Fotoğraf seçilmedi ya da boş. Lütfen yeni bir fotoğraf seç.");
        }
        if (photo.Length > _options.MaxImageBytes)
        {
            throw new AppValidationException("Photo", $"Fotoğraf en fazla {_options.MaxImageBytes / (1024 * 1024)} MB olabilir.");
        }

        // Dosya diske yazilmaz ve saklanmaz; yalnizca bu istegin omru boyunca bellekte tutulur.
        var buffer = new byte[photo.Length];
        await using (var stream = photo.OpenReadStream())
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken);
        }

        var result = await _foodAnalysisService.AnalyzeAsync(userId, new FoodImageUpload(buffer, photo.ContentType), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Kullanicinin onayladigi yemekleri ve gramajlari gunluge kaydeder. Besin degerleri sunucudaki analizden okunur.
    /// Ayni analiz ikinci kez kaydedilmez; tekrar gelen istek ilk kaydin sonucunu (alreadyLogged=true) doner.
    /// </summary>
    [HttpPost("{analysisId:long}/confirm")]
    public async Task<ActionResult<ConfirmFoodAnalysisResponse>> Confirm(
        long analysisId,
        [FromBody] ConfirmFoodAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _foodAnalysisService.ConfirmAsync(userId, analysisId, request, cancellationToken);
        return result.AlreadyLogged ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    }
}
