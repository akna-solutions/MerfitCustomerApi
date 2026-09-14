using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace MerfitCustomerApi.Infrastructure.Persistence;

/// <summary>
/// MerfitCustomerApi uygulamasinin Entity Framework Core veritabani baglamini (DbContext) temsil eder.
/// Tum entity'lere ait DbSet'leri barindirir ve MerfitCustomerApi.Infrastructure.Configurations
/// altindaki IEntityTypeConfiguration siniflarini otomatik olarak uygular.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// AppDbContext sinifinin yeni bir ornegini, disaridan saglanan DbContext secenekleri ile olusturur.
    /// </summary>
    /// <param name="options">Baglanti dizesi ve saglayici gibi DbContext yapilandirma secenekleri.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// ApplicationUser kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();

    /// <summary>
    /// UserRefreshToken kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();

    /// <summary>
    /// ExternalLogin kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();

    /// <summary>
    /// UserProfile kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    /// <summary>
    /// UserPreference kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    /// <summary>
    /// UserNotificationSetting kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserNotificationSetting> UserNotificationSettings => Set<UserNotificationSetting>();

    /// <summary>
    /// UserPrivacySetting kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserPrivacySetting> UserPrivacySettings => Set<UserPrivacySetting>();

    /// <summary>
    /// UserDevice kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();

    /// <summary>
    /// Notification kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>
    /// MuscleGroup kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<MuscleGroup> MuscleGroups => Set<MuscleGroup>();

    /// <summary>
    /// Equipment kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Equipment> Equipments => Set<Equipment>();

    /// <summary>
    /// UserEquipment kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserEquipment> UserEquipments => Set<UserEquipment>();

    /// <summary>
    /// Exercise kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Exercise> Exercises => Set<Exercise>();

    /// <summary>
    /// WorkoutCategory kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutCategory> WorkoutCategories => Set<WorkoutCategory>();

    /// <summary>
    /// Workout kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Workout> Workouts => Set<Workout>();

    /// <summary>
    /// WorkoutEquipment kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutEquipment> WorkoutEquipments => Set<WorkoutEquipment>();

    /// <summary>
    /// WorkoutExercise kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutExercise> WorkoutExercises => Set<WorkoutExercise>();

    /// <summary>
    /// WorkoutPlan kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutPlan> WorkoutPlans => Set<WorkoutPlan>();

    /// <summary>
    /// WorkoutPlanDay kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutPlanDay> WorkoutPlanDays => Set<WorkoutPlanDay>();

    /// <summary>
    /// WorkoutPlanExercise kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutPlanExercise> WorkoutPlanExercises => Set<WorkoutPlanExercise>();

    /// <summary>
    /// WorkoutSession kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();

    /// <summary>
    /// WorkoutSessionExercise kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutSessionExercise> WorkoutSessionExercises => Set<WorkoutSessionExercise>();

    /// <summary>
    /// WorkoutSetLog kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WorkoutSetLog> WorkoutSetLogs => Set<WorkoutSetLog>();

    /// <summary>
    /// PersonalRecord kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<PersonalRecord> PersonalRecords => Set<PersonalRecord>();

    /// <summary>
    /// Food kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Food> Foods => Set<Food>();

    /// <summary>
    /// Meal kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Meal> Meals => Set<Meal>();

    /// <summary>
    /// MealItem kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<MealItem> MealItems => Set<MealItem>();

    /// <summary>
    /// NutritionGoal kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<NutritionGoal> NutritionGoals => Set<NutritionGoal>();

    /// <summary>
    /// NutritionPlan kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<NutritionPlan> NutritionPlans => Set<NutritionPlan>();

    /// <summary>
    /// NutritionPlanDay kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<NutritionPlanDay> NutritionPlanDays => Set<NutritionPlanDay>();

    /// <summary>
    /// NutritionPlanMeal kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<NutritionPlanMeal> NutritionPlanMeals => Set<NutritionPlanMeal>();

    /// <summary>
    /// NutritionPlanMealItem kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<NutritionPlanMealItem> NutritionPlanMealItems => Set<NutritionPlanMealItem>();

    /// <summary>
    /// WaterLog kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<WaterLog> WaterLogs => Set<WaterLog>();

    /// <summary>
    /// BodyMeasurement kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<BodyMeasurement> BodyMeasurements => Set<BodyMeasurement>();

    /// <summary>
    /// UserGoal kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserGoal> UserGoals => Set<UserGoal>();

    /// <summary>
    /// GoalHistory kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<GoalHistory> GoalHistories => Set<GoalHistory>();

    /// <summary>
    /// PersonalizationJob kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<PersonalizationJob> PersonalizationJobs => Set<PersonalizationJob>();

    /// <summary>
    /// UserStreak kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserStreak> UserStreaks => Set<UserStreak>();

    /// <summary>
    /// MerfitScore kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<MerfitScore> MerfitScores => Set<MerfitScore>();

    /// <summary>
    /// MerfitScoreBreakdown kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<MerfitScoreBreakdown> MerfitScoreBreakdowns => Set<MerfitScoreBreakdown>();

    /// <summary>
    /// MerfitScoreHistory kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<MerfitScoreHistory> MerfitScoreHistories => Set<MerfitScoreHistory>();

    /// <summary>
    /// Achievement kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Achievement> Achievements => Set<Achievement>();

    /// <summary>
    /// UserAchievement kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserAchievement> UserAchievements => Set<UserAchievement>();

    /// <summary>
    /// LeaderboardPeriod kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<LeaderboardPeriod> LeaderboardPeriods => Set<LeaderboardPeriod>();

    /// <summary>
    /// LeaderboardEntry kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<LeaderboardEntry> LeaderboardEntries => Set<LeaderboardEntry>();

    /// <summary>
    /// Reward kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Reward> Rewards => Set<Reward>();

    /// <summary>
    /// LeaderboardReward kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<LeaderboardReward> LeaderboardRewards => Set<LeaderboardReward>();

    /// <summary>
    /// UserReward kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserReward> UserRewards => Set<UserReward>();

    /// <summary>
    /// SubscriptionProduct kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<SubscriptionProduct> SubscriptionProducts => Set<SubscriptionProduct>();

    /// <summary>
    /// Subscription kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    /// <summary>
    /// SubscriptionTransaction kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<SubscriptionTransaction> SubscriptionTransactions => Set<SubscriptionTransaction>();

    /// <summary>
    /// Feature kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Feature> Features => Set<Feature>();

    /// <summary>
    /// SubscriptionPlanFeature kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<SubscriptionPlanFeature> SubscriptionPlanFeatures => Set<SubscriptionPlanFeature>();

    /// <summary>
    /// AiGenerationRequest kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<AiGenerationRequest> AiGenerationRequests => Set<AiGenerationRequest>();

    /// <summary>
    /// AiGenerationResult kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<AiGenerationResult> AiGenerationResults => Set<AiGenerationResult>();

    /// <summary>
    /// Language kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Language> Languages => Set<Language>();

    /// <summary>
    /// Translation kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<Translation> Translations => Set<Translation>();

    /// <summary>
    /// AppContent kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<AppContent> AppContents => Set<AppContent>();

    /// <summary>
    /// FAQCategory kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<FAQCategory> FAQCategories => Set<FAQCategory>();

    /// <summary>
    /// FAQ kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<FAQ> FAQs => Set<FAQ>();

    /// <summary>
    /// SupportTicket kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();

    /// <summary>
    /// SupportTicketMessage kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<SupportTicketMessage> SupportTicketMessages => Set<SupportTicketMessage>();

    /// <summary>
    /// LegalDocument kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();

    /// <summary>
    /// UserConsent kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<UserConsent> UserConsents => Set<UserConsent>();

    /// <summary>
    /// AuditLog kayitlarina erisim saglayan DbSet.
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Model olustururken MerfitCustomerApi.Infrastructure.Configurations namespace'inde tanimli
    /// tum IEntityTypeConfiguration siniflarini bu assembly uzerinden otomatik olarak uygular.
    /// </summary>
    /// <param name="modelBuilder">EF Core model olusturucusu.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Bu assembly icindeki (MerfitCustomerApi.Infrastructure.Configurations) tum
        // IEntityTypeConfiguration<T> siniflarini otomatik olarak model'e uygular.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // PersonalizationJob'un ayni anda birden fazla background worker instance'i tarafindan
        // (aynen ya da farkli makinelerde) iki kere islenmesini engellemek icin PostgreSQL'in
        // "xmin" sistem kolonunu optimistic concurrency token olarak kullaniyoruz: bir worker
        // Pending -> Processing gecisini kaydederken, ayni satiri okuyup ayni gecisi yapmaya
        // calisan ikinci bir worker'in SaveChanges'i DbUpdateConcurrencyException ile basarisiz
        // olur (bkz. PersonalizationJobProcessor.TryClaimJobAsync). Bu mekanizma yalnizca
        // PostgreSQL'de calisir; testlerde kullanilan Sqlite saglayicisinda atlanir (xmin sistem
        // kolonu Sqlite'ta yoktur) - testler bu senaryoyu ayrica IUnitOfWork transaction'iyla dogrular.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<PersonalizationJob>()
                .Property<uint>("xmin")
                .HasColumnType("xid")
                .IsRowVersion();
        }
    }
}