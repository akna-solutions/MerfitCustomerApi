using MerfitCustomerApi.Domain.Interfaces;
using MerfitCustomerApi.Domain.Interfaces.Repositories;
using MerfitCustomerApi.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace MerfitCustomerApi.Infrastructure.Persistence;

/// <summary>
/// IUnitOfWork sozlesmesinin Entity Framework Core uzerinden calisan varsayilan implementasyonu.
/// Repository'leri turlerine gore cache'ler ve degisikliklerin tek bir islemde kaydedilmesini saglar.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Bu Unit of Work'un uzerinde calistigi veritabani baglami.
    /// </summary>
    private readonly AppDbContext _dbContext;

    /// <summary>
    /// Daha once olusturulan generic repository orneklerini entity turune gore saklayan onbellek.
    /// </summary>
    private readonly Dictionary<Type, object> _repositories = new();

    /// <summary>
    /// Aktif veritabani islemini (transaction) tutan alan; islem baslatilmadiysa null olur.
    /// </summary>
    private IDbContextTransaction? _currentTransaction;

    /// <summary>
    /// Nesnenin daha once serbest birakilip birakilmadigini belirtir.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// UnitOfWork sinifinin yeni bir ornegini, disaridan saglanan DbContext ile olusturur.
    /// </summary>
    /// <param name="dbContext">Kullanilacak Entity Framework Core veritabani baglami.</param>
    public UnitOfWork(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Verilen entity turu icin generic repository ornegini dondurur; ayni turden birden
    /// fazla istekte ayni repository ornegi tekrar kullanilir (cache'lenir).
    /// </summary>
    /// <typeparam name="TEntity">Repository'nin islem yapacagi entity turu.</typeparam>
    public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        var entityType = typeof(TEntity);

        if (!_repositories.ContainsKey(entityType))
        {
            _repositories[entityType] = new GenericRepository<TEntity>(_dbContext);
        }

        return (IGenericRepository<TEntity>)_repositories[entityType];
    }

    /// <summary>
    /// Izlenen (tracked) tum degisiklikleri tek bir veritabani islemi icinde asenkron olarak kaydeder.
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    /// <returns>Veritabaninda etkilenen kayit sayisi.</returns>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Veritabani islemini (transaction) asenkron olarak baslatir.
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _currentTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Aktif veritabani islemini (transaction) asenkron olarak onaylar (commit).
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            return;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    /// <summary>
    /// Aktif veritabani islemini (transaction) asenkron olarak geri alir (rollback).
    /// </summary>
    /// <param name="cancellationToken">Islemi iptal etmek icin kullanilan token.</param>
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            return;
        }

        await _currentTransaction.RollbackAsync(cancellationToken);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }

    /// <summary>
    /// Veritabani baglami ve varsa acik islem tarafindan kullanilan kaynaklari serbest birakir.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _currentTransaction?.Dispose();
        _dbContext.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Degisiklik izleyicisindeki tum tracked entity'leri Detached durumuna getirir.
    /// </summary>
    public void ClearTracking()
    {
        _dbContext.ChangeTracker.Clear();
    }
}