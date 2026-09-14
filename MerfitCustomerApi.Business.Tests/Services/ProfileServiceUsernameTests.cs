using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Services.Profile;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Services;

/// <summary>
/// PUT /api/profile'daki kullanici adi (Username) benzersizlik kontrolunun, mevcut kullaniciyi
/// yanlislikla "baskasi" gibi gormedigini ve degismeyen bir Username icin gereksiz sorgu/hata
/// uretmedigini dogrular (bkz. ProfileService.ApplyIdentityChangesAsync). Ayrica FirstName/LastName
/// alanlarinin dogru sekilde okunup yazildigini kapsar.
///
/// Not: Bu testler yalnizca uygulama katmanindaki (AnyAsync) kontrolu dogrular; mock repository
/// gercek bir veritabani unique constraint'i uygulamadigindan, UserProfileConfiguration'daki
/// unique index'in (race condition korumasi) es zamanlilik davranisi burada test EDILEMEZ - bu,
/// gercek bir DB'ye karsi calisan bir entegrasyon testi gerektirir.
/// </summary>
public class ProfileServiceUsernameTests
{
    private sealed class Fixture
    {
        public required ProfileService Service { get; init; }
        public required List<ApplicationUser> Users { get; init; }
        public required List<UserProfile> Profiles { get; init; }
    }

    private static Fixture CreateFixture()
    {
        var users = new List<ApplicationUser>();
        var profiles = new List<UserProfile>();
        var goals = new List<UserGoal>();
        var preferences = new List<UserPreference>();
        var notifications = new List<UserNotificationSetting>();
        var privacy = new List<UserPrivacySetting>();
        var equipment = new List<UserEquipment>();
        var sessions = new List<WorkoutSession>();
        var streaks = new List<UserStreak>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<ApplicationUser>()).Returns(MockRepositoryFactory.Create(users).Object);
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(profiles).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(goals).Object);
        unitOfWork.Setup(u => u.Repository<UserPreference>()).Returns(MockRepositoryFactory.Create(preferences).Object);
        unitOfWork.Setup(u => u.Repository<UserNotificationSetting>()).Returns(MockRepositoryFactory.Create(notifications).Object);
        unitOfWork.Setup(u => u.Repository<UserPrivacySetting>()).Returns(MockRepositoryFactory.Create(privacy).Object);
        unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(equipment).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(sessions).Object);
        unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(streaks).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new Fixture
        {
            Service = new ProfileService(unitOfWork.Object),
            Users = users,
            Profiles = profiles,
        };
    }

    private static ApplicationUser MakeUser(long id, string email) => new()
    {
        Id = id,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        PasswordHash = "hashed",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    private static UserProfile MakeProfile(long userId, string firstName, string lastName, string username) => new()
    {
        Id = userId,
        UserId = userId,
        FirstName = firstName,
        LastName = lastName,
        Username = username,
        UnitSystem = UnitSystem.Metric,
        CreatedAt = DateTime.UtcNow,
    };

    // ---- Username ----

    [Fact]
    public async Task UpdateProfileAsync_Succeeds_WhenCurrentUserKeepsSameUsername()
    {
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));

        var result = await fx.Service.UpdateProfileAsync(1, new UpdateProfileRequest { Username = "mert" });

        Assert.Equal("mert", result.Username);
    }

    [Fact]
    public async Task UpdateProfileAsync_Succeeds_WhenUpdatingAnotherFieldOnly()
    {
        // Bu, orijinal bug raporunun tam senaryosudur: Username hic gonderilmeden yalnizca
        // FirstName degistiriliyor - ConflictException FIRLAMAMALI.
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));

        var result = await fx.Service.UpdateProfileAsync(1, new UpdateProfileRequest { FirstName = "Mehmet" });

        Assert.Equal("Mehmet", result.FirstName);
        Assert.Equal("mert", result.Username);
    }

    [Fact]
    public async Task UpdateProfileAsync_Succeeds_WhenChangingToAvailableUsername()
    {
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));

        var result = await fx.Service.UpdateProfileAsync(1, new UpdateProfileRequest { Username = "mertakpinar" });

        Assert.Equal("mertakpinar", result.Username);
    }

    [Fact]
    public async Task UpdateProfileAsync_ThrowsConflict_WhenCurrentUserChangesToAnotherUsersUsername()
    {
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Users.Add(MakeUser(2, "ahmet@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));
        fx.Profiles.Add(MakeProfile(2, "Ahmet", "Yılmaz", "ahmet"));

        await Assert.ThrowsAsync<ConflictException>(
            () => fx.Service.UpdateProfileAsync(1, new UpdateProfileRequest { Username = "ahmet" }));

        Assert.Equal("mert", fx.Profiles.Single(p => p.UserId == 1).Username);
    }

    [Fact]
    public async Task UpdateProfileAsync_ThrowsConflict_WhenDifferentUserTriesToUseExistingUsername()
    {
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Users.Add(MakeUser(2, "ahmet@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));
        fx.Profiles.Add(MakeProfile(2, "Ahmet", "Yılmaz", "ahmet"));

        await Assert.ThrowsAsync<ConflictException>(
            () => fx.Service.UpdateProfileAsync(2, new UpdateProfileRequest { Username = "mert" }));

        Assert.Equal("ahmet", fx.Profiles.Single(p => p.UserId == 2).Username);
    }

    [Fact]
    public async Task UpdateProfileAsync_UsernameComparisonIsCaseInsensitive()
    {
        // "mert" -> "Mert" gonderilmesi, ayni kullanici adiyla es degerdir; conflict beklenmez.
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));

        var result = await fx.Service.UpdateProfileAsync(1, new UpdateProfileRequest { Username = "Mert" });

        Assert.Equal("mert", result.Username);
    }

    // ---- FirstName / LastName ----

    [Fact]
    public async Task GetProfileAsync_ReturnsFirstNameLastNameUsername()
    {
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));

        var result = await fx.Service.GetProfileAsync(1);

        Assert.Equal("Mert", result.FirstName);
        Assert.Equal("Akpınar", result.LastName);
        Assert.Equal("mert", result.Username);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesFirstNameOnly()
    {
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));

        var result = await fx.Service.UpdateProfileAsync(1, new UpdateProfileRequest { FirstName = "Mehmet" });

        Assert.Equal("Mehmet", result.FirstName);
        Assert.Equal("Akpınar", result.LastName);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesLastNameOnly()
    {
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));

        var result = await fx.Service.UpdateProfileAsync(1, new UpdateProfileRequest { LastName = "Yılmaz" });

        Assert.Equal("Mert", result.FirstName);
        Assert.Equal("Yılmaz", result.LastName);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesFirstNameAndLastNameTogether()
    {
        var fx = CreateFixture();
        fx.Users.Add(MakeUser(1, "mert@example.com"));
        fx.Profiles.Add(MakeProfile(1, "Mert", "Akpınar", "mert"));

        var result = await fx.Service.UpdateProfileAsync(
            1,
            new UpdateProfileRequest { FirstName = "Mehmet", LastName = "Yılmaz" });

        Assert.Equal("Mehmet", result.FirstName);
        Assert.Equal("Yılmaz", result.LastName);
    }
}
