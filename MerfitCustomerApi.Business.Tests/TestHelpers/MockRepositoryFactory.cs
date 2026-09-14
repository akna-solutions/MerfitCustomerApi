using System.Linq.Expressions;
using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Interfaces.Repositories;
using Moq;

namespace MerfitCustomerApi.Business.Tests.TestHelpers;

/// <summary>
/// IGenericRepository&lt;T&gt; icin, bellekteki bir List&lt;T&gt;'yi "veritabani" gibi kullanan
/// ortak mock fabrikasi. ProfileServiceThemeTests'teki tek-entity'lik elle-yazilmis mock kurulumunu
/// (bkz. o dosya) birden fazla entity turu/servis gerektiren testler icin genellestirir.
/// </summary>
public static class MockRepositoryFactory
{
    public static Mock<IGenericRepository<T>> Create<T>(List<T> items) where T : BaseEntity
    {
        var repo = new Mock<IGenericRepository<T>>();

        repo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(() => items.AsAsyncQueryable());

        repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                items.AsQueryable().FirstOrDefault(predicate));

        repo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                items.AsQueryable().Any(predicate));

        repo.Setup(r => r.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) => items.FirstOrDefault(x => x.Id == id));

        repo.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .Callback<T, CancellationToken>((entity, _) => items.Add(entity))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<T>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<T>, CancellationToken>((entities, _) => items.AddRange(entities))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.Update(It.IsAny<T>()));
        repo.Setup(r => r.Remove(It.IsAny<T>())).Callback<T>(e => items.Remove(e));

        return repo;
    }
}
