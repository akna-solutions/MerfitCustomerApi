using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Configurations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Persistence;

/// <summary>
/// Fotograf analizinin mukerrer kayit korumasi: AiGenerationRequest.UpdatedAt gercek konfigurasyonla eszamanlilik
/// belirtecidir; ayni analizi iki baglam ayni anda "kaydedildi" diye isaretleyemez (in-memory SQLite).
/// </summary>
public sealed class FoodAnalysisPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly long _analysisId;

    public FoodAnalysisPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
        var user = new ApplicationUser { Email = "a@test.local", UserName = "a", PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow };
        context.Users.Add(user);
        context.SaveChanges();
        var analysis = new AiGenerationRequest
        {
            UserId = user.Id, Type = AiRequestType.FoodPhotoAnalysis, Status = AiGenerationStatus.Completed,
            Prompt = "Yemek fotoğrafı analizi (image/jpeg, 1280x960, 240 KB)", CreatedAt = DateTime.UtcNow,
        };
        context.Requests.Add(analysis);
        context.SaveChanges();
        _analysisId = analysis.Id;
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
        {
        }

        public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
        public DbSet<AiGenerationRequest> Requests => Set<AiGenerationRequest>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ApplicationUser>(b =>
            {
                b.ToTable("ApplicationUser");
                b.HasKey(x => x.Id);
            });
            new AiGenerationRequestConfiguration().Configure(modelBuilder.Entity<AiGenerationRequest>());
            modelBuilder.Entity<AiGenerationRequest>().ToTable("AiGenerationRequest");
        }
    }

    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public void SecondConcurrentClaim_FailsWithConcurrencyException()
    {
        using var first = CreateContext();
        using var second = CreateContext();
        var a = first.Requests.Single(r => r.Id == _analysisId);
        var b = second.Requests.Single(r => r.Id == _analysisId);

        a.UpdatedAt = DateTime.UtcNow;
        first.SaveChanges();

        b.UpdatedAt = DateTime.UtcNow.AddSeconds(1);
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());

        using var check = CreateContext();
        Assert.Equal(a.UpdatedAt, check.Requests.Single(r => r.Id == _analysisId).UpdatedAt);
    }

    [Fact]
    public void FoodPhotoAnalysisType_IsStoredAsString()
    {
        using var context = CreateContext();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT \"Type\" FROM \"AiGenerationRequest\" WHERE \"Id\" = {_analysisId}";
        context.Database.OpenConnection();

        Assert.Equal("FoodPhotoAnalysis", command.ExecuteScalar());
    }
}
