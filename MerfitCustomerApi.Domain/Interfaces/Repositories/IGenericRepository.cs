using System.Linq.Expressions;

namespace MerfitCustomerApi.Domain.Interfaces.Repositories;

/// <summary>
/// Tum entity turleri icin ortak veritabani islemlerini tanimlayan generic repository sozlesmesi.
/// </summary>
/// <typeparam name="TEntity">Repository'nin islem yapacagi entity turu.</typeparam>
public interface IGenericRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// Bu entity turune ait IQueryable'i dondurur; admin panelindeki liste uc noktalarinda
    /// dinamik filtreleme (Where), siralama (OrderBy) ve sayfalama (Skip/Take) database
    /// tarafinda uygulanabilsin diye kullanilir. Tum veriyi memory'e cekmez.
    /// </summary>
    /// <param name="asNoTracking">
    /// true ise (varsayilan) sorgu AsNoTracking() ile calisir; sadece okuma amacli liste/detay
    /// sorgularinda kullanilmalidir. Guncellenecek bir kayit getirilecekse false verilmelidir.
    /// </param>
    IQueryable<TEntity> GetQueryable(bool asNoTracking = true);

    /// <summary>
    /// Verilen birincil anahtar (id) degerine sahip kaydi asenkron olarak getirir.
    /// </summary>
    /// <param name="id">Aranan kaydin birincil anahtar degeri.</param>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task<TEntity?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bu entity turune ait tum kayitlari asenkron olarak getirir.
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen kosula uyan kayitlari asenkron olarak getirir.
    /// </summary>
    /// <param name="predicate">Kayitlari filtrelemek icin kullanilan LINQ ifadesi.</param>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen kosula uyan ilk kaydi asenkron olarak getirir; bulunamazsa null doner.
    /// </summary>
    /// <param name="predicate">Kaydi filtrelemek icin kullanilan LINQ ifadesi.</param>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen kosula uyan kayit olup olmadigini asenkron olarak kontrol eder.
    /// </summary>
    /// <param name="predicate">Kontrol edilecek kosul.</param>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yeni bir kaydi izlemeye (tracking) alir; veritabanina yazmaz, bunun icin SaveChanges cagrilmalidir.
    /// </summary>
    /// <param name="entity">Eklenecek entity.</param>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Birden fazla kaydi izlemeye (tracking) alir; veritabanina yazmaz, bunun icin SaveChanges cagrilmalidir.
    /// </summary>
    /// <param name="entities">Eklenecek entity koleksiyonu.</param>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Var olan bir kaydi guncellenmek uzere isaretler.
    /// </summary>
    /// <param name="entity">Guncellenecek entity.</param>
    void Update(TEntity entity);

    /// <summary>
    /// Bir kaydi silinmek uzere isaretler.
    /// </summary>
    /// <param name="entity">Silinecek entity.</param>
    void Remove(TEntity entity);

    /// <summary>
    /// Birden fazla kaydi silinmek uzere isaretler.
    /// </summary>
    /// <param name="entities">Silinecek entity koleksiyonu.</param>
    void RemoveRange(IEnumerable<TEntity> entities);
}