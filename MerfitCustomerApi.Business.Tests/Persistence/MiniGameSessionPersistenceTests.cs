using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Configurations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Persistence;

/// <summary>
/// MiniGameSessionConfiguration'in veritabani garantilerini (kullanici basina tek aktif oturum, Status
/// concurrency token'i ile tek odul, puan check constraint'i) gercek konfigurasyonla in-memory SQLite'ta
/// dogrular. Ayni kurallar PostgreSQL migration'inda da vardir (MiniGameSessionPostgresTests).
/// </summary>
public sealed class MiniGameSessionPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly long _userId;

    public MiniGameSessionPersistenceTests()
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

    private sealed class MiniGameTestDbContext : DbContext
    {
        public MiniGameTestDbContext(DbContextOptions<MiniGameTestDbContext> options) : base(options)
        {
        }

        public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
        public DbSet<MiniGameSession> Sessions => Set<MiniGameSession>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ApplicationUser>(b =>
            {
                b.ToTable("ApplicationUser");
                b.HasKey(x => x.Id);
            });
            new MiniGameSessionConfiguration().Configure(modelBuilder.Entity<MiniGameSession>());
            // SQLite'ta sema yoktur.
            modelBuilder.Entity<MiniGameSession>().ToTable("MiniGameSession");
        }
    }

    private MiniGameTestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MiniGameTestDbContext>().UseSqlite(_connection).Options);

    private MiniGameSession NewSession(MiniGameSessionStatus status = MiniGameSessionStatus.Started) => new()
    {
        UserId = _userId, GameKey = "dumbbell-dash", EngineVersion = 1, Seed = 1, Status = status,
        StartedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(15), CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void SecondActiveSessionForSameUser_IsRejected()
    {
        using var context = CreateContext();
        context.Sessions.Add(NewSession());
        context.SaveChanges();

        context.Sessions.Add(NewSession());
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void FinishedSessions_DoNotBlockNewActiveSession()
    {
        using var context = CreateContext();
        context.Sessions.AddRange(NewSession(MiniGameSessionStatus.Completed), NewSession(MiniGameSessionStatus.Abandoned), NewSession());
        context.SaveChanges();

        Assert.Equal(3, context.Sessions.Count());
    }

    [Fact]
    public void ConcurrentCompletion_OnlyFirstWriterSucceeds()
    {
        long id;
        using (var setup = CreateContext())
        {
            var session = NewSession();
            setup.Sessions.Add(session);
            setup.SaveChanges();
            id = session.Id;
        }

        using var first = CreateContext();
        using var second = CreateContext();
        var a = first.Sessions.Single(s => s.Id == id);
        var b = second.Sessions.Single(s => s.Id == id);

        a.Status = MiniGameSessionStatus.Completed;
        a.TierPoints = 5;
        a.AwardedPoints = 5;
        first.SaveChanges();

        b.Status = MiniGameSessionStatus.Completed;
        b.TierPoints = 5;
        b.AwardedPoints = 5;
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    [Fact]
    public void AwardedPointsAboveTier_IsRejectedByCheckConstraint()
    {
        using var context = CreateContext();
        var session = NewSession(MiniGameSessionStatus.Completed);
        session.TierPoints = 1;
        session.AwardedPoints = 5;
        context.Sessions.Add(session);

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    public void Dispose() => _connection.Dispose();
}
