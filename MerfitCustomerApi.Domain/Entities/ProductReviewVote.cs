using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>Yorum faydali oyu (store.ProductReviewVote); (ReviewId, UserId) benzersizdir, oy degistirilebilir.</summary>
public class ProductReviewVote : BaseEntity
{
    public long ReviewId { get; set; }

    public long UserId { get; set; }

    public bool IsHelpful { get; set; }
}
