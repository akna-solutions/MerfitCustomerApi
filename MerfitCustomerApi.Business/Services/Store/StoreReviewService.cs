using System.Security.Cryptography;
using System.Text;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.ContentModeration;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>Yorum kurallari (mobil uygulama ayni sinirlari gosterir).</summary>
public static class StoreReviewRules
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MinCommentLength = 10;
    public const int MaxCommentLength = 2000;
    public const int MaxTitleLength = 100;
    public const int MaxImagesPerReview = 5;
    public const int MaxReportDescriptionLength = 500;
    public const int MinOtherReportDescriptionLength = 5;
    public const string ImageRoute = "/api/store/review-images/";
}

/// <summary>
/// IStoreReviewService'in varsayilan implementasyonu.
/// - Ozet: (ProductId, Status, Rating) indeksi uzerinden tek GROUP BY; ortalama dagilimdan hesaplanir.
/// - Liste: filtre, siralama ve sayfalama veritabaninda; gorsel ve kullanici oylari sayfadaki yorumlar icin tek
///   sorguda okunur (N+1 yok). Gorsel baytlari liste sorgularinda okunmaz.
/// - Oy sayaclari her oy degisikliginden sonra oy tablosundan yeniden sayilir; eszamanli oylarda sapma olmaz.
/// - Metinler duz metin olarak saklanir (HTML yorumlanmaz; mobil Text ve admin React kacislar), kontrol karakterleri
///   atilir ve icerik moderasyon sozlugunden gecirilir.
/// </summary>
public class StoreReviewService : IStoreReviewService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IContentModerationService _moderation;
    private readonly TimeProvider _timeProvider;

    public StoreReviewService(IUnitOfWork unitOfWork, IContentModerationService? moderation = null, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _moderation = moderation ?? new ContentModerationService();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private IQueryable<ProductReview> Reviews(bool tracking = false) => _unitOfWork.Repository<ProductReview>().GetQueryable(asNoTracking: !tracking);

    private IQueryable<ProductReview> Published(long productId) =>
        Reviews().Where(r => r.ProductId == productId && r.Status == ProductReviewStatus.Published);

    // ---------------- Okuma ----------------

    public async Task<StoreReviewSummaryDto> GetSummaryAsync(long userId, long productId, CancellationToken cancellationToken = default)
    {
        await EnsureProductVisibleAsync(productId, cancellationToken);

        var counts = await Published(productId)
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var images = _unitOfWork.Repository<ProductReviewImage>().GetQueryable();
        var withPhotos = await Published(productId).CountAsync(r => images.Any(i => i.ReviewId == r.Id), cancellationToken);
        var verified = await Published(productId).CountAsync(r => r.IsVerifiedPurchase, cancellationToken);
        var myReviewId = await Reviews()
            .Where(r => r.ProductId == productId && r.UserId == userId && r.Status != ProductReviewStatus.Deleted)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var total = counts.Sum(c => c.Count);
        var sum = counts.Sum(c => (long)c.Rating * c.Count);
        return new StoreReviewSummaryDto
        {
            ProductId = productId,
            ReviewCount = total,
            AverageRating = total == 0 ? 0 : Math.Round((decimal)sum / total, 1, MidpointRounding.AwayFromZero),
            Distribution = Enumerable.Range(StoreReviewRules.MinRating, StoreReviewRules.MaxRating).Reverse().Select(star =>
            {
                var count = counts.FirstOrDefault(c => c.Rating == star)?.Count ?? 0;
                return new StoreReviewDistributionItem
                {
                    Rating = star,
                    Count = count,
                    Percentage = total == 0 ? 0 : (int)Math.Round(count * 100m / total, MidpointRounding.AwayFromZero),
                };
            }).ToList(),
            WithPhotosCount = withPhotos,
            VerifiedPurchaseCount = verified,
            MyReviewId = myReviewId,
            HasVerifiedPurchase = await HasVerifiedPurchaseAsync(userId, productId, cancellationToken),
        };
    }

    public async Task<PagedResult<StoreReviewDto>> GetListAsync(long userId, long productId, StoreReviewQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureProductVisibleAsync(productId, cancellationToken);
        if (query.Rating is { } rating && (rating < StoreReviewRules.MinRating || rating > StoreReviewRules.MaxRating))
        {
            throw new AppValidationException("Rating", "Yıldız filtresi 1 ile 5 arasında olmalı.");
        }

        var filtered = Published(productId);
        if (query.Rating is { } star) filtered = filtered.Where(r => r.Rating == star);
        if (query.VerifiedOnly) filtered = filtered.Where(r => r.IsVerifiedPurchase);
        if (query.WithPhotos)
        {
            var images = _unitOfWork.Repository<ProductReviewImage>().GetQueryable();
            filtered = filtered.Where(r => images.Any(i => i.ReviewId == r.Id));
        }

        var total = await filtered.CountAsync(cancellationToken);
        var ordered = (query.Sort?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "newest" => filtered.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            "oldest" => filtered.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),
            "rating_desc" => filtered.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            "rating_asc" => filtered.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            "helpful" => filtered.OrderByDescending(r => r.HelpfulCount).ThenBy(r => r.NotHelpfulCount).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            _ => throw new AppValidationException("Sort", "Geçersiz sıralama seçeneği."),
        };

        var page = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        var items = await ToDtosAsync(userId, page, cancellationToken);
        return PagedResult<StoreReviewDto>.Create(items, query.Page, query.PageSize, total);
    }

    public async Task<StoreReviewDto?> GetMineAsync(long userId, long productId, CancellationToken cancellationToken = default)
    {
        var review = await Reviews()
            .FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId && r.Status != ProductReviewStatus.Deleted, cancellationToken);
        return review is null ? null : (await ToDtosAsync(userId, new List<ProductReview> { review }, cancellationToken))[0];
    }

    public async Task<StoreReviewImageFile?> GetImageAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!IsStorageKey(storageKey)) return null;
        var published = Reviews().Where(r => r.Status == ProductReviewStatus.Published).Select(r => r.Id);
        return await _unitOfWork.Repository<ProductReviewImage>().GetQueryable()
            .Where(i => i.StorageKey == storageKey && published.Contains(i.ReviewId))
            .Select(i => new StoreReviewImageFile(i.Content, i.ContentType))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ---------------- Yorum yazma ----------------

    public async Task<StoreReviewDto> CreateAsync(long userId, long productId, UpsertStoreReviewRequest request, CancellationToken cancellationToken = default)
    {
        var input = Validate(request);
        await EnsureProductVisibleAsync(productId, cancellationToken);

        var repo = _unitOfWork.Repository<ProductReview>();
        if (await repo.AnyAsync(r => r.ProductId == productId && r.UserId == userId && r.Status != ProductReviewStatus.Deleted, cancellationToken))
        {
            throw DuplicateReview();
        }

        var now = UtcNow;
        var review = new ProductReview
        {
            ProductId = productId,
            UserId = userId,
            Rating = input.Rating,
            Title = input.Title,
            Comment = input.Comment,
            Status = ProductReviewStatus.Published,
            IsVerifiedPurchase = await HasVerifiedPurchaseAsync(userId, productId, cancellationToken),
            CreatedAt = now,
            CreatedUser = userId,
        };
        await repo.AddAsync(review, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            // Ayni istegin eszamanli tekrari: filtreli unique index ikinci yorumu engeller.
            _unitOfWork.ClearTracking();
            throw DuplicateReview();
        }

        return (await ToDtosAsync(userId, new List<ProductReview> { review }, cancellationToken))[0];
    }

    public async Task<StoreReviewDto> UpdateAsync(long userId, long reviewId, UpsertStoreReviewRequest request, CancellationToken cancellationToken = default)
    {
        var input = Validate(request);
        var review = await LoadOwnAsync(userId, reviewId, tracking: true, cancellationToken);

        review.Rating = input.Rating;
        review.Title = input.Title;
        review.Comment = input.Comment;
        review.IsVerifiedPurchase = await HasVerifiedPurchaseAsync(userId, review.ProductId, cancellationToken);
        review.EditedAt = UtcNow;
        review.UpdatedAt = review.EditedAt;
        review.UpdatedUser = userId;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return (await ToDtosAsync(userId, new List<ProductReview> { review }, cancellationToken))[0];
    }

    public async Task DeleteAsync(long userId, long reviewId, CancellationToken cancellationToken = default)
    {
        var review = await Reviews(tracking: true).FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken)
            ?? throw new NotFoundException("Yorum bulunamadı.");
        if (review.UserId != userId) throw new ForbiddenException("Yalnızca kendi yorumunu silebilirsin.");
        if (review.Status == ProductReviewStatus.Deleted) return;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            review.Status = ProductReviewStatus.Deleted;
            review.DeletedAt = UtcNow;
            review.UpdatedAt = review.DeletedAt;
            review.UpdatedUser = userId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // Kullanicinin kaldirdigi yorumun fotograflari saklanmaz.
            await _unitOfWork.Repository<ProductReviewImage>().GetQueryable(asNoTracking: false)
                .Where(i => i.ReviewId == reviewId)
                .ExecuteDeleteAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    // ---------------- Fotograflar ----------------

    public async Task<StoreReviewImageDto> AddImageAsync(long userId, long reviewId, StoreReviewImageUpload upload, CancellationToken cancellationToken = default)
    {
        var review = await LoadOwnAsync(userId, reviewId, tracking: false, cancellationToken);
        var images = _unitOfWork.Repository<ProductReviewImage>();
        var existing = await images.GetQueryable().CountAsync(i => i.ReviewId == review.Id, cancellationToken);
        if (existing >= StoreReviewRules.MaxImagesPerReview) throw TooManyImages();

        var processed = StoreReviewImageProcessor.Process(upload.Content, upload.DeclaredContentType);
        var nextOrder = await images.GetQueryable().Where(i => i.ReviewId == review.Id).Select(i => (int?)i.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var image = new ProductReviewImage
        {
            ReviewId = review.Id,
            StorageKey = NewStorageKey(),
            ContentType = processed.ContentType,
            ByteSize = processed.Content.Length,
            Width = processed.Width,
            Height = processed.Height,
            SortOrder = nextOrder + 1,
            Content = processed.Content,
            CreatedAt = UtcNow,
            CreatedUser = userId,
        };
        await images.AddAsync(image, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Eszamanli yuklemelerde sinir asildiysa son eklenen geri alinir.
        if (await images.GetQueryable().CountAsync(i => i.ReviewId == review.Id, cancellationToken) > StoreReviewRules.MaxImagesPerReview)
        {
            await images.GetQueryable(asNoTracking: false).Where(i => i.Id == image.Id).ExecuteDeleteAsync(cancellationToken);
            throw TooManyImages();
        }

        await TouchAsync(review.Id, userId, cancellationToken);
        return ToImageDto(image.Id, image.StorageKey, image.Width, image.Height);
    }

    public async Task RemoveImageAsync(long userId, long reviewId, long imageId, CancellationToken cancellationToken = default)
    {
        var review = await LoadOwnAsync(userId, reviewId, tracking: false, cancellationToken);
        var deleted = await _unitOfWork.Repository<ProductReviewImage>().GetQueryable(asNoTracking: false)
            .Where(i => i.Id == imageId && i.ReviewId == review.Id)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0) await TouchAsync(review.Id, userId, cancellationToken);
    }

    // ---------------- Oylar ----------------

    public async Task<StoreReviewVoteResponse> VoteAsync(long userId, long reviewId, VoteStoreReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (request.IsHelpful is not { } isHelpful)
        {
            throw new AppValidationException("IsHelpful", "Oy seçilmedi.");
        }

        var review = await LoadPublishedForInteractionAsync(userId, reviewId, "Kendi yorumuna oy veremezsin.", cancellationToken);
        var votes = _unitOfWork.Repository<ProductReviewVote>();
        for (var attempt = 0; ; attempt++)
        {
            var vote = await votes.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(v => v.ReviewId == review.Id && v.UserId == userId, cancellationToken);
            if (vote is null)
            {
                await votes.AddAsync(new ProductReviewVote { ReviewId = review.Id, UserId = userId, IsHelpful = isHelpful, CreatedAt = UtcNow, CreatedUser = userId }, cancellationToken);
            }
            else if (vote.IsHelpful != isHelpful)
            {
                vote.IsHelpful = isHelpful;
                vote.UpdatedAt = UtcNow;
                vote.UpdatedUser = userId;
            }

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (attempt == 0 && DbExceptionClassifier.IsUniqueViolation(ex, out _))
            {
                // Ayni kullanicinin eszamanli ikinci oyu: kayit artik var, guncelleme olarak tekrar denenir.
                _unitOfWork.ClearTracking();
            }
        }

        return await RecountVotesAsync(review.Id, isHelpful, cancellationToken);
    }

    public async Task<StoreReviewVoteResponse> RemoveVoteAsync(long userId, long reviewId, CancellationToken cancellationToken = default)
    {
        var exists = await Reviews().AnyAsync(r => r.Id == reviewId && r.Status != ProductReviewStatus.Deleted, cancellationToken);
        if (!exists) throw new NotFoundException("Yorum bulunamadı.");
        await _unitOfWork.Repository<ProductReviewVote>().GetQueryable(asNoTracking: false)
            .Where(v => v.ReviewId == reviewId && v.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        return await RecountVotesAsync(reviewId, null, cancellationToken);
    }

    private async Task<StoreReviewVoteResponse> RecountVotesAsync(long reviewId, bool? myVote, CancellationToken cancellationToken)
    {
        var votes = _unitOfWork.Repository<ProductReviewVote>().GetQueryable();
        await Reviews(tracking: true).Where(r => r.Id == reviewId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.HelpfulCount, r => votes.Count(v => v.ReviewId == r.Id && v.IsHelpful))
            .SetProperty(r => r.NotHelpfulCount, r => votes.Count(v => v.ReviewId == r.Id && !v.IsHelpful)), cancellationToken);
        var counts = await Reviews().Where(r => r.Id == reviewId).Select(r => new { r.HelpfulCount, r.NotHelpfulCount }).FirstAsync(cancellationToken);
        return new StoreReviewVoteResponse { ReviewId = reviewId, HelpfulCount = counts.HelpfulCount, NotHelpfulCount = counts.NotHelpfulCount, MyVote = myVote };
    }

    // ---------------- Raporlar ----------------

    public async Task<ReportStoreReviewResponse> ReportAsync(long userId, long reviewId, ReportStoreReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ProductReviewReportReason>(request.Reason?.Trim(), ignoreCase: true, out var reason) || !Enum.IsDefined(reason)
            || int.TryParse(request.Reason, out _))
        {
            throw new AppValidationException("Reason", "Geçerli bir rapor sebebi seç.");
        }

        var description = CleanText(request.Description, multiline: true);
        if (description?.Length > StoreReviewRules.MaxReportDescriptionLength)
        {
            throw new AppValidationException("Description", $"Açıklama en fazla {StoreReviewRules.MaxReportDescriptionLength} karakter olabilir.");
        }
        if (reason == ProductReviewReportReason.Other && (description?.Length ?? 0) < StoreReviewRules.MinOtherReportDescriptionLength)
        {
            throw new AppValidationException("Description", "\"Diğer\" seçeneği için kısa bir açıklama yaz.");
        }

        var review = await LoadPublishedForInteractionAsync(userId, reviewId, "Kendi yorumunu raporlayamazsın.", cancellationToken);
        var reports = _unitOfWork.Repository<ProductReviewReport>();
        if (await reports.AnyAsync(r => r.ReviewId == review.Id && r.ReporterUserId == userId, cancellationToken))
        {
            return new ReportStoreReviewResponse { AlreadyReported = true };
        }

        await reports.AddAsync(new ProductReviewReport
        {
            ReviewId = review.Id,
            ReporterUserId = userId,
            Reason = reason,
            Description = description,
            Status = ProductReviewReportStatus.Open,
            CreatedAt = UtcNow,
            CreatedUser = userId,
        }, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            _unitOfWork.ClearTracking();
            return new ReportStoreReviewResponse { AlreadyReported = true };
        }

        return new ReportStoreReviewResponse { AlreadyReported = false };
    }

    // ---------------- Yardimcilar ----------------

    private sealed record ReviewInput(int Rating, string? Title, string Comment);

    private ReviewInput Validate(UpsertStoreReviewRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Rating is not { } rating)
        {
            errors["Rating"] = ["Yıldız puanı seçmelisin."];
            rating = 0;
        }
        else if (rating < StoreReviewRules.MinRating || rating > StoreReviewRules.MaxRating)
        {
            errors["Rating"] = ["Yıldız puanı 1 ile 5 arasında olmalı."];
        }

        var title = CleanText(request.Title, multiline: false);
        if (title?.Length > StoreReviewRules.MaxTitleLength)
        {
            errors["Title"] = [$"Başlık en fazla {StoreReviewRules.MaxTitleLength} karakter olabilir."];
        }

        var comment = CleanText(request.Comment, multiline: true);
        if (comment is null)
        {
            errors["Comment"] = ["Yorum metni yazmalısın."];
        }
        else if (comment.Length < StoreReviewRules.MinCommentLength)
        {
            errors["Comment"] = [$"Yorum en az {StoreReviewRules.MinCommentLength} karakter olmalı."];
        }
        else if (comment.Length > StoreReviewRules.MaxCommentLength)
        {
            errors["Comment"] = [$"Yorum en fazla {StoreReviewRules.MaxCommentLength} karakter olabilir."];
        }

        if (errors.Count > 0) throw new AppValidationException(errors);

        _moderation.EnsureAllowed(
            ("Title", title, ContentModerationType.ReviewTitle),
            ("Comment", comment, ContentModerationType.ReviewComment));
        return new ReviewInput(rating, title, comment!);
    }

    /// <summary>
    /// Kirpar, Unicode'u normalize eder, kontrol ve gorunmez yon karakterlerini atar; cok satirli metinde en fazla iki ardisik
    /// bos satir birakir. Bos sonuc null doner.
    /// </summary>
    public static string? CleanText(string? value, bool multiline)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new StringBuilder(normalized.Length);
        var newlines = 0;
        foreach (var ch in normalized)
        {
            if (ch == '\n')
            {
                if (!multiline) { builder.Append(' '); continue; }
                if (++newlines > 2) continue;
                builder.Append('\n');
                continue;
            }

            if (char.IsControl(ch) && ch != '\t') continue;
            // Yon degistirme (bidi) karakterleri metni yaniltici gosterebilir.
            if (ch is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '‎' or '‏' or '﻿') continue;
            newlines = 0;
            builder.Append(ch == '\t' ? ' ' : ch);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private async Task EnsureProductVisibleAsync(long productId, CancellationToken cancellationToken)
    {
        var activeCategories = _unitOfWork.Repository<StoreCategory>().GetQueryable().Where(c => c.IsActive).Select(c => c.Id);
        var visible = await _unitOfWork.Repository<Product>().GetQueryable()
            .AnyAsync(p => p.Id == productId && p.IsActive && activeCategories.Contains(p.CategoryId), cancellationToken);
        if (!visible) throw new NotFoundException("Ürün bulunamadı veya artık satışta değil.");
    }

    private Task<bool> HasVerifiedPurchaseAsync(long userId, long productId, CancellationToken cancellationToken)
    {
        var items = _unitOfWork.Repository<StoreOrderItem>().GetQueryable();
        return StoreReviewPurchaseRules.QualifyingOrders(_unitOfWork.Repository<StoreOrder>().GetQueryable().Where(o => o.UserId == userId))
            .AnyAsync(o => items.Any(i => i.OrderId == o.Id && i.ProductId == productId), cancellationToken);
    }

    private async Task<ProductReview> LoadOwnAsync(long userId, long reviewId, bool tracking, CancellationToken cancellationToken)
    {
        var review = await Reviews(tracking).FirstOrDefaultAsync(r => r.Id == reviewId && r.Status != ProductReviewStatus.Deleted, cancellationToken)
            ?? throw new NotFoundException("Yorum bulunamadı.");
        if (review.UserId != userId) throw new ForbiddenException("Yalnızca kendi yorumunu düzenleyebilirsin.");
        return review;
    }

    private async Task<ProductReview> LoadPublishedForInteractionAsync(long userId, long reviewId, string ownMessage, CancellationToken cancellationToken)
    {
        var review = await Reviews().FirstOrDefaultAsync(r => r.Id == reviewId && r.Status == ProductReviewStatus.Published, cancellationToken)
            ?? throw new NotFoundException("Yorum bulunamadı.");
        if (review.UserId == userId) throw new ForbiddenException(ownMessage);
        return review;
    }

    private Task TouchAsync(long reviewId, long userId, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        return Reviews(tracking: true).Where(r => r.Id == reviewId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.EditedAt, now).SetProperty(r => r.UpdatedAt, now).SetProperty(r => r.UpdatedUser, userId), cancellationToken);
    }

    private async Task<List<StoreReviewDto>> ToDtosAsync(long userId, List<ProductReview> reviews, CancellationToken cancellationToken)
    {
        if (reviews.Count == 0) return new List<StoreReviewDto>();
        var ids = reviews.Select(r => r.Id).ToList();
        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();

        var profiles = await _unitOfWork.Repository<UserProfile>().GetQueryable()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FirstName, p.LastName, p.Username, p.ProfileImageUrl })
            .ToListAsync(cancellationToken);
        var profileByUser = profiles.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First());

        var images = await _unitOfWork.Repository<ProductReviewImage>().GetQueryable()
            .Where(i => ids.Contains(i.ReviewId))
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.ReviewId, i.StorageKey, i.Width, i.Height })
            .ToListAsync(cancellationToken);
        var imagesByReview = images.ToLookup(i => i.ReviewId);

        var myVotes = await _unitOfWork.Repository<ProductReviewVote>().GetQueryable()
            .Where(v => v.UserId == userId && ids.Contains(v.ReviewId))
            .Select(v => new { v.ReviewId, v.IsHelpful })
            .ToDictionaryAsync(v => v.ReviewId, v => v.IsHelpful, cancellationToken);
        var myReports = (await _unitOfWork.Repository<ProductReviewReport>().GetQueryable()
            .Where(r => r.ReporterUserId == userId && ids.Contains(r.ReviewId))
            .Select(r => r.ReviewId)
            .ToListAsync(cancellationToken)).ToHashSet();

        return reviews.Select(r =>
        {
            profileByUser.TryGetValue(r.UserId, out var profile);
            return new StoreReviewDto
            {
                Id = r.Id,
                ProductId = r.ProductId,
                Rating = r.Rating,
                Title = r.Title,
                Comment = r.Comment,
                IsVerifiedPurchase = r.IsVerifiedPurchase,
                Status = r.Status == ProductReviewStatus.Hidden ? "hidden" : "published",
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                EditedAt = r.EditedAt is { } edited ? DateTime.SpecifyKind(edited, DateTimeKind.Utc) : null,
                Author = new StoreReviewAuthorDto
                {
                    DisplayName = DisplayName(profile?.FirstName, profile?.LastName, profile?.Username),
                    AvatarUrl = StoreUrls.SafeImage(profile?.ProfileImageUrl),
                },
                Images = imagesByReview[r.Id].Select(i => ToImageDto(i.Id, i.StorageKey, i.Width, i.Height)).ToList(),
                HelpfulCount = r.HelpfulCount,
                NotHelpfulCount = r.NotHelpfulCount,
                MyVote = myVotes.TryGetValue(r.Id, out var vote) ? vote : null,
                IsMine = r.UserId == userId,
                ReportedByMe = myReports.Contains(r.Id),
            };
        }).ToList();
    }

    /// <summary>"Ayse K." bicimi; ad yoksa kullanici adi; o da yoksa genel ad. E-posta asla gosterilmez.</summary>
    public static string DisplayName(string? firstName, string? lastName, string? username)
    {
        var first = firstName?.Trim();
        if (!string.IsNullOrEmpty(first))
        {
            var last = lastName?.Trim();
            return string.IsNullOrEmpty(last) ? first : $"{first} {char.ToUpper(last[0], new System.Globalization.CultureInfo("tr-TR"))}.";
        }

        var user = username?.Trim();
        return string.IsNullOrEmpty(user) ? "MBFit üyesi" : user;
    }

    private static StoreReviewImageDto ToImageDto(long id, string key, int width, int height) =>
        new() { Id = id, Url = StoreReviewRules.ImageRoute + key, Width = width, Height = height };

    private static string NewStorageKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static bool IsStorageKey(string? value) =>
        value is { Length: 32 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ConflictException DuplicateReview() =>
        new("Bu ürüne zaten bir yorumun var. Mevcut yorumunu düzenleyebilirsin.", "REVIEW_EXISTS");

    private static ConflictException TooManyImages() =>
        new($"Bir yoruma en fazla {StoreReviewRules.MaxImagesPerReview} fotoğraf ekleyebilirsin.", "TOO_MANY_IMAGES");
}
