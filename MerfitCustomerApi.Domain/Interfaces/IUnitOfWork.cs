
using MerfitCustomerApi.Domain.Interfaces.Repositories;

namespace MerfitCustomerApi.Domain.Interfaces;

/// <summary>
/// Birden fazla repository uzerinden yapilan degisikliklerin tek bir islem (transaction)
/// icinde veritabanina kaydedilmesini saglayan Unit of Work sozlesmesi.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Verilen entity turu icin generic repository ornegini dondurur; ayni turden birden
    /// fazla istekte ayni repository ornegi tekrar kullanilir (cache'lenir).
    /// </summary>
    /// <typeparam name="TEntity">Repository'nin islem yapacagi entity turu.</typeparam>
    IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class;

    /// <summary>
    /// Izlenen (tracked) tum degisiklikleri tek bir veritabani islemi icinde asenkron olarak kaydeder.
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    /// <returns>Veritabaninda etkilenen kayit sayisi.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Veritabani islemini (transaction) asenkron olarak baslatir.
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktif veritabani islemini (transaction) asenkron olarak onaylar (commit).
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktif veritabani islemini (transaction) asenkron olarak geri alir (rollback).
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Degisiklik izleyicisindeki (change tracker) tum tracked entity'leri Detached durumuna
    /// getirir. Basarisiz bir transaction rollback edildikten sonra, o transaction icinde
    /// eklenmis (Added) ama hicbir zaman kalici olmamis entity'lerin bir sonraki SaveChanges
    /// cagrisinda sehven tekrar eklenmeye calisilmasini onlemek icin kullanilir
    /// (bkz. PersonalizationJobProcessor.FailOrRetryJobAsync).
    /// </summary>
    void ClearTracking();
}