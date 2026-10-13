using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Configurations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Persistence;

/// <summary>
/// UserAccountTokenConfiguration'in veritabani garantilerini gercek konfigurasyonla in-memory SQLite'ta dogrular:
/// benzersiz jeton ozeti, gecersiz amaç icin check constraint ve UsedAt concurrency token'i ile tek kullanim.
/// </summary>
public sealed class UserAccountTokenPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly long _userId;

    public UserAccountTokenPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
        var user = new ApplicationUser { Email = "a@test.local", UserName = "a", PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow };
        context.Users.Add(user);
        context.SaveChanges();
        _userId = user.Id;
    }

    private sealed class TokenTestDbContext : DbContext
    {
        public TokenTestDbContext(DbContextOptions<TokenTestDbContext> options) : base(options)
        {
        }

        public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
        public DbSet<UserAccountToken> Tokens => Set<UserAccountToken>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ApplicationUser>(b =>
            {
                b.ToTable("ApplicationUser");
                b.HasKey(x => x.Id);
            });
            new UserAccountTokenConfiguration().Configure(modelBuilder.Entity<UserAccountToken>());
            // SQLite'ta sema yoktur.
            modelBuilder.Entity<UserAccountToken>().ToTable("UserAccountToken");
        }
    }

    private TokenTestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TokenTestDbContext>().UseSqlite(_connection).Options);

    private UserAccountToken NewToken(string hash) => new()
    {
        UserId = _userId, Purpose = AccountTokenPurpose.PasswordReset, TokenHash = hash, NormalizedEmail = "A@TEST.LOCAL",
        ExpiresAt = DateTime.UtcNow.AddMinutes(30), CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void DuplicateTokenHash_IsRejected()
    {
        using var context = CreateContext();
        context.Tokens.Add(NewToken(new string('a', 64)));
        context.SaveChanges();

        context.Tokens.Add(NewToken(new string('a', 64)));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void ConcurrentUse_OnlyFirstSucceeds()
    {
        long id;
        using (var setup = CreateContext())
        {
            var token = NewToken(new string('b', 64));
            setup.Tokens.Add(token);
            setup.SaveChanges();
            id = token.Id;
        }

        using var first = CreateContext();
        using var second = CreateContext();
        var a = first.Tokens.Single(t => t.Id == id);
        var b = second.Tokens.Single(t => t.Id == id);

        a.UsedAt = DateTime.UtcNow;
        first.SaveChanges();

        b.UsedAt = DateTime.UtcNow;
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    [Fact]
    public void PurposeIsStoredAsText_AndUnknownValuesAreRejected()
    {
        using var context = CreateContext();
        context.Tokens.Add(NewToken(new string('c', 64)));
        context.SaveChanges();

        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlRaw(
            "INSERT INTO UserAccountToken (UserId, Purpose, TokenHash, NormalizedEmail, ExpiresAt, CreatedAt) VALUES ({0}, 'Other', 'd', 'X', '2030-01-01', '2026-01-01')", _userId));
    }

    public void Dispose() => _connection.Dispose();
}
