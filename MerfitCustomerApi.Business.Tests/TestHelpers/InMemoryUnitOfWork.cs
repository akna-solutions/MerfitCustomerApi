using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Interfaces;
using MerfitCustomerApi.Domain.Interfaces.Repositories;
using Moq;

namespace MerfitCustomerApi.Business.Tests.TestHelpers;

/// <summary>
/// Her entity turu icin bellekte bir liste tutan IUnitOfWork. MockRepositoryFactory'yi kullanir;
/// ek olarak SaveChanges'te Id'si 0 olan yeni kayitlara artan Id verir (gercek veritabani gibi), boylece
/// "olusan kaydin kimligi" uzerinden calisan kod (orn. bildirim kutusu kimligi) test edilebilir.
/// </summary>
public sealed class InMemoryUnitOfWork
{
    private readonly Dictionary<Type, object> _lists = new();
    private readonly Dictionary<Type, object> _repositories = new();
    private long _nextId = 1000;

    public Mock<IUnitOfWork> Mock { get; } = new();

    public int SaveCount { get; private set; }

    public IUnitOfWork Object => Mock.Object;

    public InMemoryUnitOfWork()
    {
        Mock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                SaveCount++;
                AssignIds();
                return 1;
            });
    }

    public List<T> Set<T>() where T : BaseEntity
    {
        if (!_lists.TryGetValue(typeof(T), out var list))
        {
            var items = new List<T>();
            _lists[typeof(T)] = items;
            var repo = MockRepositoryFactory.Create(items);
            _repositories[typeof(T)] = repo.Object;
            Mock.Setup(u => u.Repository<T>()).Returns(repo.Object);
            return items;
        }

        return (List<T>)list;
    }

    /// <summary>Kullanilacak tum entity turlerini onceden kaydeder (bos listeler).</summary>
    public InMemoryUnitOfWork Register<T>() where T : BaseEntity
    {
        Set<T>();
        return this;
    }

    private void AssignIds()
    {
        foreach (var list in _lists.Values)
        {
            foreach (var entity in ((System.Collections.IEnumerable)list).Cast<BaseEntity>())
            {
                if (entity.Id == 0)
                {
                    entity.Id = ++_nextId;
                }
            }
        }
    }
}
