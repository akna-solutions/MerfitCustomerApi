using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Configurations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Persistence;

/// <summary>
/// Beslenme semasinin veritabani kurallarini gercek IEntityTypeConfiguration siniflariyla in-memory
/// SQLite uzerinde dogrular: kullanici basina tek aktif plan (kismi benzersiz index), plan gecmisi,
/// besin siniflandirma ve kullanici tercih alanlarinin saklanmasi. (Ayni kurallar PostgreSQL
/// migration'inda da yer alir; tam AppDbContext SQLite'ta kurulamadigi icin kucuk bir baglam kullanilir.)
/// </summary>
public sealed class NutritionPlanPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public NutritionPlanPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Users.AddRange(
            new ApplicationUser { Id = 1, Email = "a@example.com", NormalizedEmail = "A@EXAMPLE.COM", UserName = "a@example.com", NormalizedUserName = "A@EXAMPLE.COM", PasswordHash = "x", CreatedAt = DateTime.UtcNow },
            new ApplicationUser { Id = 2, Email = "b@example.com", NormalizedEmail = "B@EXAMPLE.COM", UserName = "b@example.com", NormalizedUserName = "B@EXAMPLE.COM", PasswordHash = "x", CreatedAt = DateTime.UtcNow });
        context.SaveChanges();
    }

    private sealed class NutritionTestDbContext : DbContext
    {
        public NutritionTestDbContext(DbContextOptions<NutritionTestDbContext> options) : base(options)
        {
        }

        public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
        public DbSet<UserProfile> Profiles => Set<UserProfile>();
        public DbSet<Food> Foods => Set<Food>();
        public DbSet<NutritionPlan> Plans => Set<NutritionPlan>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ApplicationUserConfiguration());
            modelBuilder.ApplyConfiguration(new UserProfileConfiguration());
            modelBuilder.ApplyConfiguration(new FoodConfiguration());
            modelBuilder.ApplyConfiguration(new NutritionPlanConfiguration());
            modelBuilder.ApplyConfiguration(new NutritionPlanDayConfiguration());
            modelBuilder.ApplyConfiguration(new NutritionPlanMealConfiguration());
            modelBuilder.ApplyConfiguration(new NutritionPlanMealItemConfiguration());
        }
    }

    private NutritionTestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NutritionTestDbContext>().UseSqlite(_connection).Options);

    private static NutritionPlan Plan(long userId, bool active) => new()
    {
        UserId = userId,
        Name = "Plan",
        StartDate = DateTime.UtcNow.Date,
        IsActive = active,
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void SecondActivePlanForSameUser_IsRejectedByDatabase()
    {
        using (var context = CreateContext())
        {
            context.Plans.Add(Plan(1, active: true));
            context.SaveChanges();
        }

        using var second = CreateContext();
        second.Plans.Add(Plan(1, active: true));

        var exception = Assert.Throws<DbUpdateException>(() => second.SaveChanges());
        Assert.Contains("UNIQUE", exception.InnerException!.Message);
    }

    [Fact]
    public void InactiveHistory_AndOtherUsersActivePlans_AreAllowed()
    {
        using var context = CreateContext();
        context.Plans.AddRange(Plan(1, active: false), Plan(1, active: false), Plan(1, active: true), Plan(2, active: true));

        context.SaveChanges();

        Assert.Equal(3, context.Plans.Count(p => p.UserId == 1));
        Assert.Equal(1, context.Plans.Count(p => p.UserId == 1 && p.IsActive));
    }

    [Fact]
    public void DeactivateThenActivate_InOneSave_Succeeds()
    {
        using (var context = CreateContext())
        {
            context.Plans.Add(Plan(1, active: true));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var old = context.Plans.Single(p => p.UserId == 1 && p.IsActive);
            old.IsActive = false;
            context.SaveChanges();
            context.Plans.Add(Plan(1, active: true));
            context.SaveChanges();
        }

        using var verify = CreateContext();
        Assert.Equal(2, verify.Plans.Count(p => p.UserId == 1));
        Assert.Equal(1, verify.Plans.Count(p => p.UserId == 1 && p.IsActive));
    }

    [Fact]
    public void FoodClassification_AndUserPreferences_RoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Foods.Add(new Food
            {
                Name = "Yulaf", ServingSize = 40, ServingUnit = "g", Calories = 150, Protein = 5, Carbs = 27, Fat = 3, CreatedAt = DateTime.UtcNow,
                Category = FoodCategory.GrainStarch,
                MealSuitability = MealSuitability.Breakfast | MealSuitability.Snack,
                Allergens = FoodAllergen.Gluten,
                DietTags = DietTag.Vegetarian | DietTag.Vegan,
            });
            context.Foods.Add(new Food { Name = "Eski kayit", ServingSize = 100, ServingUnit = "g", Calories = 100, CreatedAt = DateTime.UtcNow });
            context.Profiles.Add(new UserProfile
            {
                UserId = 1, FirstName = "A", LastName = "B", Username = "ab", CreatedAt = DateTime.UtcNow,
                DietaryPreference = DietaryPreference.Vegetarian,
                Allergies = FoodAllergen.Milk | FoodAllergen.Peanut,
                ExcludedFoodIds = "3,8",
            });
            context.SaveChanges();
        }

        using var verify = CreateContext();
        var oats = verify.Foods.Single(f => f.Name == "Yulaf");
        Assert.Equal(FoodCategory.GrainStarch, oats.Category);
        Assert.Equal(MealSuitability.Breakfast | MealSuitability.Snack, oats.MealSuitability);
        Assert.Equal(FoodAllergen.Gluten, oats.Allergens);
        Assert.Equal(DietTag.Vegetarian | DietTag.Vegan, oats.DietTags);

        // Mevcut (siniflandirilmamis) kayitlar null kalir; varsayilan deger uydurulmaz.
        var legacy = verify.Foods.Single(f => f.Name == "Eski kayit");
        Assert.Null(legacy.Category);
        Assert.Null(legacy.MealSuitability);
        Assert.Null(legacy.Allergens);
        Assert.Null(legacy.DietTags);

        var profile = verify.Profiles.Single();
        Assert.Equal(DietaryPreference.Vegetarian, profile.DietaryPreference);
        Assert.Equal(FoodAllergen.Milk | FoodAllergen.Peanut, profile.Allergies);
        Assert.Equal("3,8", profile.ExcludedFoodIds);
    }

    public void Dispose() => _connection.Dispose();
}
