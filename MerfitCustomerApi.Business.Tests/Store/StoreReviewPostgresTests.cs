using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir. Urun yorumlarinin is kurallari (tek aktif yorum, sahiplik, onayli
/// alisveris, oy, rapor, fotograf, ozet) gercek PostgreSQL'de ve gercek unique index/CHECK kisitlariyla dogrulanir.
/// Her test kendi kullanici ve urunlerini olusturur.
/// </summary>
public class StoreReviewPostgresTests
{
    private readonly StoreTestData _data = new();

    private static async Task<T> Run<T>(Func<StoreReviewService, Task<T>> action)
    {
        await using var db = StoreTestData.CreateContext();
        return await action(new StoreReviewService(new UnitOfWork(db)));
    }

    private static async Task Run(Func<StoreReviewService, Task> action)
    {
        await using var db = StoreTestData.CreateContext();
        await action(new StoreReviewService(new UnitOfWork(db)));
    }

    private async Task<long> ProductAsync()
    {
        var category = await _data.CategoryAsync();
        return (await _data.ProductAsync(category, 100m, 10)).Id;
    }

    private static UpsertStoreReviewRequest Review(int? rating = 5, string? comment = "Kaliteli ürün, kesinlikle tavsiye ederim.", string? title = "Harika") =>
        new() { Rating = rating, Comment = comment, Title = title };

    private static async Task<long> OrderAsync(long userId, long productId, StoreOrderStatus status, StorePaymentStatus payment,
        StorePaymentMethod method = StorePaymentMethod.BankTransfer)
    {
        await using var db = StoreTestData.CreateContext();
        var order = new StoreOrder
        {
            OrderNumber = "RV-" + Guid.NewGuid().ToString("N")[..12], UserId = userId, Status = status, PaymentStatus = payment, PaymentMethod = method,
            Subtotal = 100, TotalAmount = 100, ShippingAddressSnapshot = "{}", IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAt = DateTime.UtcNow,
        };
        order.Items.Add(new StoreOrderItem
        {
            ProductId = productId, ProductNameSnapshot = "Urun", SkuSnapshot = "SKU", BrandSnapshot = "Marka", UnitListPrice = 100, UnitPrice = 100,
            Quantity = 1, LineTotal = 100, CreatedAt = DateTime.UtcNow,
        });
        db.StoreOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task SetStatusAsync(long reviewId, ProductReviewStatus status)
    {
        await using var db = StoreTestData.CreateContext();
        await db.ProductReviews.Where(r => r.Id == reviewId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status));
    }

    // ---------------- Olusturma ----------------

    [PostgresFact]
    public async Task LoggedInUser_CreatesPublishedReview_WithoutBadgeWhenNotPurchased()
    {
        var user = await _data.UserAsync();
        var product = await ProductAsync();

        var review = await Run(s => s.CreateAsync(user, product, Review(4, "  Tadı güzel,\r\n\r\n\r\n\r\nçözünürlüğü iyi.  ")));

        Assert.Equal(4, review.Rating);
        Assert.Equal("published", review.Status);
        Assert.False(review.IsVerifiedPurchase);
        Assert.True(review.IsMine);
        Assert.Equal("Tadı güzel,\n\nçözünürlüğü iyi.", review.Comment);
        Assert.Equal("MBFit üyesi", review.Author.DisplayName);
    }

    [PostgresFact]
    public async Task SecondActiveReviewForSameProduct_IsRejected_EvenWhenConcurrent()
    {
        var user = await _data.UserAsync();
        var product = await ProductAsync();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try { await Run(s => s.CreateAsync(user, product, Review())); return true; }
            catch (ConflictException ex) when (ex.Code == "REVIEW_EXISTS") { return false; }
        })));

        Assert.Equal(1, attempts.Count(ok => ok));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Run(s => s.CreateAsync(user, product, Review())));
        Assert.Equal("REVIEW_EXISTS", ex.Code);
        await using var db = StoreTestData.CreateContext();
        Assert.Equal(1, await db.ProductReviews.CountAsync(r => r.UserId == user && r.ProductId == product));
    }

    [PostgresFact]
    public async Task Validation_RequiresRatingAndComment_AndEnforcesLengths()
    {
        var user = await _data.UserAsync();
        var product = await ProductAsync();

        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.CreateAsync(user, product, Review(rating: null))));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.CreateAsync(user, product, Review(rating: 6))));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.CreateAsync(user, product, Review(rating: 0))));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.CreateAsync(user, product, Review(comment: null))));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.CreateAsync(user, product, Review(comment: "   kısa "))));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.CreateAsync(user, product, Review(comment: new string('a', StoreReviewRules.MaxCommentLength + 1)))));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.CreateAsync(user, product, Review(title: new string('b', StoreReviewRules.MaxTitleLength + 1)))));
        // Icerik moderasyonu (mevcut sozluk)
        var profanity = await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.CreateAsync(user, product, Review(comment: "Bu ürün tam bir orospu çocuğu işi"))));
        Assert.Contains("Comment", profanity.Errors.Keys);

        // Hicbiri kayit olusturmadi
        await using var db = StoreTestData.CreateContext();
        Assert.False(await db.ProductReviews.AnyAsync(r => r.UserId == user));
    }

    [PostgresFact]
    public async Task ReviewForInactiveOrMissingProduct_IsNotFound()
    {
        var user = await _data.UserAsync();
        var category = await _data.CategoryAsync();
        var inactive = await _data.ProductAsync(category, 50m, 1, isActive: false);
        await Assert.ThrowsAsync<NotFoundException>(() => Run(s => s.CreateAsync(user, inactive.Id, Review())));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(s => s.CreateAsync(user, long.MaxValue, Review())));
    }

    // ---------------- Duzenleme / silme / sahiplik ----------------

    [PostgresFact]
    public async Task Owner_EditsAndDeletes_ThenCanWriteANewReview()
    {
        var user = await _data.UserAsync();
        var product = await ProductAsync();
        var review = await Run(s => s.CreateAsync(user, product, Review(5)));
        await Run(s => s.AddImageAsync(user, review.Id, new StoreReviewImageUpload(ReviewTestImages.Jpeg(), "image/jpeg")));

        var edited = await Run(s => s.UpdateAsync(user, review.Id, Review(2, "Bir hafta sonra fikrim değişti, ortalama.", title: null)));
        Assert.Equal(2, edited.Rating);
        Assert.Null(edited.Title);
        Assert.NotNull(edited.EditedAt);
        Assert.Single(edited.Images);

        await Run(s => s.DeleteAsync(user, review.Id));
        await Run(s => s.DeleteAsync(user, review.Id)); // idempotent
        Assert.Null(await Run(s => s.GetMineAsync(user, product)));
        await using (var db = StoreTestData.CreateContext())
        {
            Assert.Equal(ProductReviewStatus.Deleted, await db.ProductReviews.Where(r => r.Id == review.Id).Select(r => r.Status).SingleAsync());
            Assert.False(await db.ProductReviewImages.AnyAsync(i => i.ReviewId == review.Id));
        }

        var second = await Run(s => s.CreateAsync(user, product, Review(3)));
        Assert.NotEqual(review.Id, second.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => Run(s => s.UpdateAsync(user, review.Id, Review())));
    }

    [PostgresFact]
    public async Task OtherUser_CannotEditDeleteOrChangePhotos()
    {
        var owner = await _data.UserAsync();
        var other = await _data.UserAsync();
        var product = await ProductAsync();
        var review = await Run(s => s.CreateAsync(owner, product, Review(5)));
        var image = await Run(s => s.AddImageAsync(owner, review.Id, new StoreReviewImageUpload(ReviewTestImages.Png(), "image/png")));

        await Assert.ThrowsAsync<ForbiddenException>(() => Run(s => s.UpdateAsync(other, review.Id, Review(1))));
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(s => s.DeleteAsync(other, review.Id)));
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(s => s.AddImageAsync(other, review.Id, new StoreReviewImageUpload(ReviewTestImages.Png(), "image/png"))));
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(s => s.RemoveImageAsync(other, review.Id, image.Id)));

        var mine = await Run(s => s.GetMineAsync(owner, product));
        Assert.Equal(5, mine!.Rating);
        Assert.Single(mine.Images);
        Assert.Equal("published", mine.Status);
    }

    // ---------------- Onayli alisveris ----------------

    [PostgresFact]
    public async Task VerifiedPurchase_RequiresOwnDeliveredOrderContainingProduct_NotCancelledOrRefunded()
    {
        var product = await ProductAsync();
        var otherProduct = await ProductAsync();

        async Task<bool> VerifiedFor(Func<long, Task> arrange)
        {
            var user = await _data.UserAsync();
            await arrange(user);
            return (await Run(s => s.CreateAsync(user, product, Review()))).IsVerifiedPurchase;
        }

        Assert.True(await VerifiedFor(u => OrderAsync(u, product, StoreOrderStatus.Delivered, StorePaymentStatus.Paid)));
        Assert.True(await VerifiedFor(u => OrderAsync(u, product, StoreOrderStatus.Delivered, StorePaymentStatus.Pending, StorePaymentMethod.CashOnDelivery)));
        // Teslim edilmemis
        Assert.False(await VerifiedFor(u => OrderAsync(u, product, StoreOrderStatus.Shipped, StorePaymentStatus.Paid)));
        Assert.False(await VerifiedFor(u => OrderAsync(u, product, StoreOrderStatus.Received, StorePaymentStatus.Paid)));
        // Iptal / iade
        Assert.False(await VerifiedFor(u => OrderAsync(u, product, StoreOrderStatus.Cancelled, StorePaymentStatus.RefundPending)));
        Assert.False(await VerifiedFor(u => OrderAsync(u, product, StoreOrderStatus.Cancelled, StorePaymentStatus.Cancelled)));
        Assert.False(await VerifiedFor(u => OrderAsync(u, product, StoreOrderStatus.Delivered, StorePaymentStatus.Refunded)));
        Assert.False(await VerifiedFor(u => OrderAsync(u, product, StoreOrderStatus.Delivered, StorePaymentStatus.RefundPending)));
        // Siparis baska urun iceriyor
        Assert.False(await VerifiedFor(u => OrderAsync(u, otherProduct, StoreOrderStatus.Delivered, StorePaymentStatus.Paid)));
        // Siparis baska kullaniciya ait
        Assert.False(await VerifiedFor(async _ => await OrderAsync(await _data.UserAsync(), product, StoreOrderStatus.Delivered, StorePaymentStatus.Paid)));
    }

    [PostgresFact]
    public async Task VerifiedBadge_IsRecomputedOnEdit_AndClientCannotSetIt()
    {
        var user = await _data.UserAsync();
        var product = await ProductAsync();
        var review = await Run(s => s.CreateAsync(user, product, Review()));
        Assert.False(review.IsVerifiedPurchase);

        await OrderAsync(user, product, StoreOrderStatus.Delivered, StorePaymentStatus.Paid);
        var summary = await Run(s => s.GetSummaryAsync(user, product));
        Assert.True(summary.HasVerifiedPurchase);

        var edited = await Run(s => s.UpdateAsync(user, review.Id, Review(4)));
        Assert.True(edited.IsVerifiedPurchase);
    }

    // ---------------- Ozet / liste ----------------

    [PostgresFact]
    public async Task Summary_UsesOnlyPublishedReviews()
    {
        var product = await ProductAsync();
        var viewer = await _data.UserAsync();
        var ratings = new[] { 5, 5, 4, 2, 1 };
        var ids = new List<long>();
        foreach (var rating in ratings)
        {
            var user = await _data.UserAsync();
            ids.Add((await Run(s => s.CreateAsync(user, product, Review(rating)))).Id);
        }

        var hiddenUser = await _data.UserAsync();
        var hidden = await Run(s => s.CreateAsync(hiddenUser, product, Review(1)));
        await SetStatusAsync(hidden.Id, ProductReviewStatus.Hidden);
        var deletedUser = await _data.UserAsync();
        var deleted = await Run(s => s.CreateAsync(deletedUser, product, Review(1)));
        await Run(s => s.DeleteAsync(deletedUser, deleted.Id));

        var summary = await Run(s => s.GetSummaryAsync(viewer, product));

        Assert.Equal(5, summary.ReviewCount);
        Assert.Equal(3.4m, summary.AverageRating); // 17 / 5
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, summary.Distribution.Select(d => d.Rating));
        Assert.Equal(new[] { 2, 1, 0, 1, 1 }, summary.Distribution.Select(d => d.Count));
        Assert.Equal(new[] { 40, 20, 0, 20, 20 }, summary.Distribution.Select(d => d.Percentage));
        Assert.Null(summary.MyReviewId);

        var hiddenSummary = await Run(s => s.GetSummaryAsync(hiddenUser, product));
        Assert.Equal(hidden.Id, hiddenSummary.MyReviewId); // gizlenen yorum sahibine gorunur, ozete girmez

        var list = await Run(s => s.GetListAsync(viewer, product, new StoreReviewQuery { PageSize = 50 }));
        Assert.Equal(5, list.TotalCount);
        Assert.DoesNotContain(list.Items, r => r.Id == hidden.Id || r.Id == deleted.Id);
    }

    [PostgresFact]
    public async Task EmptyProduct_HasZeroSummary()
    {
        var product = await ProductAsync();
        var summary = await Run(async s => await s.GetSummaryAsync(await _data.UserAsync(), product));
        Assert.Equal(0, summary.ReviewCount);
        Assert.Equal(0m, summary.AverageRating);
        Assert.All(summary.Distribution, d => Assert.Equal(0, d.Count));
    }

    [PostgresFact]
    public async Task List_FiltersSortsAndPaginates()
    {
        var product = await ProductAsync();
        var viewer = await _data.UserAsync();
        var buyer = await _data.UserAsync();
        await OrderAsync(buyer, product, StoreOrderStatus.Delivered, StorePaymentStatus.Paid);

        var oldest = await Run(s => s.CreateAsync(buyer, product, Review(5)));
        var photoUser = await _data.UserAsync();
        var withPhoto = await Run(s => s.CreateAsync(photoUser, product, Review(3)));
        await Run(s => s.AddImageAsync(photoUser, withPhoto.Id, new StoreReviewImageUpload(ReviewTestImages.Jpeg(), "image/jpeg")));
        var newestUser = await _data.UserAsync();
        var newest = await Run(s => s.CreateAsync(newestUser, product, Review(1)));
        await Run(s => s.VoteAsync(viewer, withPhoto.Id, new VoteStoreReviewRequest { IsHelpful = true }));
        await Run(s => s.VoteAsync(newestUser, withPhoto.Id, new VoteStoreReviewRequest { IsHelpful = true }));
        await Run(s => s.VoteAsync(viewer, oldest.Id, new VoteStoreReviewRequest { IsHelpful = true }));

        async Task<long[]> Ids(StoreReviewQuery q) => (await Run(s => s.GetListAsync(viewer, product, q))).Items.Select(r => r.Id).ToArray();

        Assert.Equal(new[] { newest.Id, withPhoto.Id, oldest.Id }, await Ids(new StoreReviewQuery()));
        Assert.Equal(new[] { oldest.Id, withPhoto.Id, newest.Id }, await Ids(new StoreReviewQuery { Sort = "oldest" }));
        Assert.Equal(new[] { oldest.Id, withPhoto.Id, newest.Id }, await Ids(new StoreReviewQuery { Sort = "rating_desc" }));
        Assert.Equal(new[] { newest.Id, withPhoto.Id, oldest.Id }, await Ids(new StoreReviewQuery { Sort = "rating_asc" }));
        Assert.Equal(new[] { withPhoto.Id, oldest.Id, newest.Id }, await Ids(new StoreReviewQuery { Sort = "helpful" }));
        Assert.Equal(new[] { withPhoto.Id }, await Ids(new StoreReviewQuery { Rating = 3 }));
        Assert.Equal(new[] { withPhoto.Id }, await Ids(new StoreReviewQuery { WithPhotos = true }));
        Assert.Equal(new[] { oldest.Id }, await Ids(new StoreReviewQuery { VerifiedOnly = true }));
        Assert.Empty(await Ids(new StoreReviewQuery { Rating = 4 }));
        await Assert.ThrowsAsync<AppValidationException>(() => Ids(new StoreReviewQuery { Rating = 9 }));
        await Assert.ThrowsAsync<AppValidationException>(() => Ids(new StoreReviewQuery { Sort = "random" }));

        var page = await Run(s => s.GetListAsync(viewer, product, new StoreReviewQuery { Page = 2, PageSize = 2 }));
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { oldest.Id }, page.Items.Select(r => r.Id));
        var item = page.Items[0];
        Assert.True(item.IsVerifiedPurchase);
        Assert.True(item.MyVote);
        Assert.False(item.IsMine);

        var photoItem = (await Run(s => s.GetListAsync(viewer, product, new StoreReviewQuery { WithPhotos = true }))).Items[0];
        Assert.Equal(2, photoItem.HelpfulCount);
        Assert.StartsWith(StoreReviewRules.ImageRoute, photoItem.Images.Single().Url);
    }

    // ---------------- Oylar ----------------

    [PostgresFact]
    public async Task Votes_CanBeCastChangedAndRemoved_OncePerUser_NotOnOwnReview()
    {
        var owner = await _data.UserAsync();
        var voter = await _data.UserAsync();
        var product = await ProductAsync();
        var review = await Run(s => s.CreateAsync(owner, product, Review()));

        await Assert.ThrowsAsync<ForbiddenException>(() => Run(s => s.VoteAsync(owner, review.Id, new VoteStoreReviewRequest { IsHelpful = true })));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.VoteAsync(voter, review.Id, new VoteStoreReviewRequest())));

        // Ayni oy eszamanli tekrarlansa da tek oy sayilir.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Run(s => s.VoteAsync(voter, review.Id, new VoteStoreReviewRequest { IsHelpful = true })))));
        var afterHelpful = await Run(s => s.VoteAsync(voter, review.Id, new VoteStoreReviewRequest { IsHelpful = true }));
        Assert.Equal((1, 0), (afterHelpful.HelpfulCount, afterHelpful.NotHelpfulCount));

        var changed = await Run(s => s.VoteAsync(voter, review.Id, new VoteStoreReviewRequest { IsHelpful = false }));
        Assert.Equal((0, 1, (bool?)false), (changed.HelpfulCount, changed.NotHelpfulCount, changed.MyVote));

        var removed = await Run(s => s.RemoveVoteAsync(voter, review.Id));
        Assert.Equal((0, 0, (bool?)null), (removed.HelpfulCount, removed.NotHelpfulCount, removed.MyVote));
        await Run(s => s.RemoveVoteAsync(voter, review.Id)); // idempotent

        await using var db = StoreTestData.CreateContext();
        Assert.False(await db.ProductReviewVotes.AnyAsync(v => v.ReviewId == review.Id));
    }

    [PostgresFact]
    public async Task HiddenReview_CannotBeVotedOrReported()
    {
        var owner = await _data.UserAsync();
        var other = await _data.UserAsync();
        var product = await ProductAsync();
        var review = await Run(s => s.CreateAsync(owner, product, Review()));
        await SetStatusAsync(review.Id, ProductReviewStatus.Hidden);

        await Assert.ThrowsAsync<NotFoundException>(() => Run(s => s.VoteAsync(other, review.Id, new VoteStoreReviewRequest { IsHelpful = true })));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(s => s.ReportAsync(other, review.Id, new ReportStoreReviewRequest { Reason = "spam" })));
    }

    // ---------------- Raporlar ----------------

    [PostgresFact]
    public async Task Reports_AreStoredOncePerUser_AndValidated()
    {
        var owner = await _data.UserAsync();
        var reporter = await _data.UserAsync();
        var product = await ProductAsync();
        var review = await Run(s => s.CreateAsync(owner, product, Review()));

        await Assert.ThrowsAsync<ForbiddenException>(() => Run(s => s.ReportAsync(owner, review.Id, new ReportStoreReviewRequest { Reason = "spam" })));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.ReportAsync(reporter, review.Id, new ReportStoreReviewRequest { Reason = "kotu" })));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.ReportAsync(reporter, review.Id, new ReportStoreReviewRequest { Reason = "3" })));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.ReportAsync(reporter, review.Id, new ReportStoreReviewRequest { Reason = "other", Description = " " })));

        var first = await Run(s => s.ReportAsync(reporter, review.Id, new ReportStoreReviewRequest { Reason = "Offensive", Description = "Saldırgan dil" }));
        var again = await Run(s => s.ReportAsync(reporter, review.Id, new ReportStoreReviewRequest { Reason = "spam" }));
        Assert.False(first.AlreadyReported);
        Assert.True(again.AlreadyReported);

        await using var db = StoreTestData.CreateContext();
        var report = await db.ProductReviewReports.SingleAsync(r => r.ReviewId == review.Id);
        Assert.Equal(ProductReviewReportReason.Offensive, report.Reason);
        Assert.Equal(ProductReviewReportStatus.Open, report.Status);
        Assert.Equal(reporter, report.ReporterUserId);

        var listed = (await Run(s => s.GetListAsync(reporter, product, new StoreReviewQuery()))).Items.Single();
        Assert.True(listed.ReportedByMe);
    }

    // ---------------- Fotograflar ----------------

    [PostgresFact]
    public async Task Photos_AreValidatedLimitedStrippedAndServedOnlyWhilePublished()
    {
        var owner = await _data.UserAsync();
        var product = await ProductAsync();
        var review = await Run(s => s.CreateAsync(owner, product, Review()));

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Run(s => s.AddImageAsync(owner, review.Id, new StoreReviewImageUpload("<svg onload=alert(1)>"u8.ToArray(), "image/svg+xml"))));

        var images = new List<StoreReviewImageDto>();
        for (var i = 0; i < StoreReviewRules.MaxImagesPerReview; i++)
        {
            images.Add(await Run(s => s.AddImageAsync(owner, review.Id, new StoreReviewImageUpload(ReviewTestImages.Jpeg(), "image/jpeg"))));
        }

        var tooMany = await Assert.ThrowsAsync<ConflictException>(() =>
            Run(s => s.AddImageAsync(owner, review.Id, new StoreReviewImageUpload(ReviewTestImages.Jpeg(), "image/jpeg"))));
        Assert.Equal("TOO_MANY_IMAGES", tooMany.Code);

        var key = images[0].Url[StoreReviewRules.ImageRoute.Length..];
        Assert.True(StoreReviewService.IsStorageKey(key));
        Assert.Equal(5, images.Select(i => i.Url).Distinct().Count());
        var file = await Run(s => s.GetImageAsync(key));
        Assert.NotNull(file);
        Assert.Equal("image/jpeg", file!.ContentType);
        Assert.DoesNotContain(ReviewTestImages.SecretMarker, System.Text.Encoding.ASCII.GetString(file.Content));

        await Run(s => s.RemoveImageAsync(owner, review.Id, images[1].Id));
        Assert.Equal(4, (await Run(s => s.GetMineAsync(owner, product)))!.Images.Count);

        await SetStatusAsync(review.Id, ProductReviewStatus.Hidden);
        Assert.Null(await Run(s => s.GetImageAsync(key)));
        Assert.Null(await Run(s => s.GetImageAsync("../../etc/passwd")));
    }
}
