using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Configurations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Persistence;

/// <summary>
/// Beslenme asistani tablolarinin veritabani garantileri (kullanici + gun basina tek sohbet, sohbet
/// silinince mesajlarin da silinmesi) gercek konfigurasyonla in-memory SQLite'ta.
/// </summary>
public sealed class NutritionAssistantPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly long _userId;

    public NutritionAssistantPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        using var context = CreateContext();
        context.Database.EnsureCreated();
        var user = new ApplicationUser { Email = "a@test.local", UserName = "a", PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow };
        context.Users.Add(user);
        context.SaveChanges();
        _userId = user.Id;
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
        {
        }

        public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
        public DbSet<NutritionAssistantConversation> Conversations => Set<NutritionAssistantConversation>();
        public DbSet<NutritionAssistantMessage> Messages => Set<NutritionAssistantMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ApplicationUser>(b =>
            {
                b.ToTable("ApplicationUser");
                b.HasKey(x => x.Id);
            });
            new NutritionAssistantConversationConfiguration().Configure(modelBuilder.Entity<NutritionAssistantConversation>());
            new NutritionAssistantMessageConfiguration().Configure(modelBuilder.Entity<NutritionAssistantMessage>());
            // SQLite'ta sema yoktur.
            modelBuilder.Entity<NutritionAssistantConversation>().ToTable("NutritionAssistantConversation");
            modelBuilder.Entity<NutritionAssistantMessage>().ToTable("NutritionAssistantMessage");
        }
    }

    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options);

    private NutritionAssistantConversation Conversation(DateOnly date) => new()
    {
        UserId = _userId, Date = date, LastMessageAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void SecondConversationForSameUserAndDay_IsRejected()
    {
        var day = new DateOnly(2026, 10, 13);
        using (var context = CreateContext())
        {
            context.Conversations.Add(Conversation(day));
            context.SaveChanges();
        }

        using var second = CreateContext();
        second.Conversations.Add(Conversation(day));
        Assert.Throws<DbUpdateException>(() => second.SaveChanges());

        using var third = CreateContext();
        third.Conversations.Add(Conversation(day.AddDays(1)));
        third.SaveChanges();
        Assert.Equal(2, third.Conversations.Count());
    }

    [Fact]
    public void DeletingConversation_DeletesItsMessages_AndContentIsStored()
    {
        long conversationId;
        using (var context = CreateContext())
        {
            var conversation = Conversation(new DateOnly(2026, 10, 13));
            context.Conversations.Add(conversation);
            context.SaveChanges();
            conversationId = conversation.Id;
            context.Messages.AddRange(
                new NutritionAssistantMessage { ConversationId = conversationId, Role = NutritionAssistantMessageRole.User, Content = "Bugün proteinim düşük mü?", CreatedAt = DateTime.UtcNow },
                new NutritionAssistantMessage { ConversationId = conversationId, Role = NutritionAssistantMessageRole.Assistant, Content = "Evet.", InputTokens = 900, OutputTokens = 20, CreatedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        using (var read = CreateContext())
        {
            var assistant = read.Messages.Single(m => m.Role == NutritionAssistantMessageRole.Assistant);
            Assert.Equal(20, assistant.OutputTokens);
            read.Conversations.Remove(read.Conversations.Single());
            read.SaveChanges();
        }

        using var check = CreateContext();
        Assert.Empty(check.Messages);
    }
}
