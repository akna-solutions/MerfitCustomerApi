using MerfitCustomerApi.Business.Services.Notifications;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Achievements;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Business.Services.Calendar;
using MerfitCustomerApi.Business.Services.Campaigns;
using MerfitCustomerApi.Business.Services.Dashboard;
using MerfitCustomerApi.Business.Services.Email;
using MerfitCustomerApi.Business.Services.Equipment;
using MerfitCustomerApi.Business.Services.FoodAnalysis;
using MerfitCustomerApi.Business.Services.Foods;
using MerfitCustomerApi.Business.Services.Leaderboard;
using MerfitCustomerApi.Business.Services.MiniGames;
using MerfitCustomerApi.Business.Services.Scoring;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using MerfitCustomerApi.Business.Services.Personalization;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Services.Profile;
using MerfitCustomerApi.Business.Services.Progress;
using MerfitCustomerApi.Business.Services.Subscriptions;
using MerfitCustomerApi.Business.Services.TokenService;
using MerfitCustomerApi.Business.Services.WorkoutSessions;
using MerfitCustomerApi.Business.Services.Workouts;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using MerfitCustomerApi.Infrastructure.Persistence;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Resend;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Farkli namespace'lerde ayni sinif adini tasiyan DTO'lar (orn. iki farkli modulde
    // "XyzListItemDto") olustugunda Swashbuckle'in varsayilan (sadece sinif adina dayali)
    // schemaId uretimi InvalidOperationException ile cakisabiliyor. Tam tip adini (namespace dahil)
    // kullanarak bu riski kalici olarak ortadan kaldiriyoruz.
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));

    // Swagger UI uzerinden "Authorize" ile Bearer token girilebilmesi icin.
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT access token'i 'Bearer {token}' formatinda giriniz.",
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer",
                },
            },
            Array.Empty<string>()
        },
    });
});

// Veritabani (PostgreSQL / EF Core)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repository / Unit of Work
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Auth
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Kisisellestirme altyapisi (FAZ 1): deterministik beslenme hedefi hesaplayicisi.
// Hem AuthService (register sirasinda ilk NutritionGoal) hem de NutritionService
// (sonradan eksik NutritionGoal olusturma) tarafindan kullanilir.
builder.Services.Configure<NutritionCalculationOptions>(builder.Configuration.GetSection(NutritionCalculationOptions.SectionName));
builder.Services.AddScoped<INutritionCalculator, NutritionCalculator>();

// Vucut olcumleri ve yag orani degerlendirmesi (yas/cinsiyete gore araliklar, olcum gecerlilik suresi).
builder.Services.Configure<BodyCompositionOptions>(builder.Configuration.GetSection(BodyCompositionOptions.SectionName));
builder.Services.Configure<ProgressiveOverloadOptions>(builder.Configuration.GetSection(ProgressiveOverloadOptions.SectionName));
builder.Services.AddScoped<IBodyCompositionService, BodyCompositionService>();

// MerfitNativeApp (mobil musteri uygulamasi) icin musteri-yuzlu servisler.
builder.Services.AddScoped<IWorkoutService, WorkoutService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
builder.Services.AddScoped<INotificationPreferenceReader>(sp => sp.GetRequiredService<NotificationService>());
builder.Services.AddScoped<IWorkoutSessionService, WorkoutSessionService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<INutritionService, NutritionService>();
builder.Services.AddScoped<IFoodService, FoodService>();
// Barkodla urun sorgusu: ucretsiz Open Food Facts API'si.
builder.Services.Configure<OpenFoodFactsOptions>(builder.Configuration.GetSection(OpenFoodFactsOptions.SectionName));
builder.Services.AddHttpClient<IFoodProductLookup, OpenFoodFactsProductLookup>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OpenFoodFactsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
});
// Beslenme asistani (beslenme ekranindaki sohbet penceresi): saglayiciya yalnizca backend baglanir.
// Varsayilan saglayici Groq; Gemini istege bagli yedek, Anthropic ucretli secenek. Anahtarlar appsettings'e
// yazilmaz; GROQ_API_KEY / GEMINI_API_KEY / ANTHROPIC_API_KEY ortam degiskenleri ile verilir.
builder.Services.Configure<NutritionAssistantOptions>(builder.Configuration.GetSection(NutritionAssistantOptions.SectionName));
var nutritionAssistantOptions = builder.Configuration.GetSection(NutritionAssistantOptions.SectionName).Get<NutritionAssistantOptions>() ?? new NutritionAssistantOptions();
builder.Services.AddSingleton<NutritionAssistantMetrics>();
builder.Services.AddSingleton<NutritionAiProviderState>();
// Typed client'lar: baglanti havuzu paylasilir, DNS degisiklikleri icin baglantilar periyodik yenilenir.
// Zaman asimi saglayici icinde istek bazinda uygulanir; HttpClient.Timeout yalnizca son guvencedir.
static SocketsHttpHandler NutritionAiHandler() => new()
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer = 32,
};
builder.Services.AddHttpClient<GroqNutritionAiProvider>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<NutritionAssistantOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.Groq.BaseUrl)) client.BaseAddress = new Uri(options.Groq.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds) + 10);
}).ConfigurePrimaryHttpMessageHandler(NutritionAiHandler);
builder.Services.AddHttpClient<GeminiNutritionAiProvider>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<NutritionAssistantOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.Gemini.BaseUrl)) client.BaseAddress = new Uri(options.Gemini.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds) + 10);
}).ConfigurePrimaryHttpMessageHandler(NutritionAiHandler);
builder.Services.AddSingleton<AnthropicNutritionAiProvider>();
builder.Services.AddTransient<INutritionAiProvider>(sp => sp.GetRequiredService<GroqNutritionAiProvider>());
builder.Services.AddTransient<INutritionAiProvider>(sp => sp.GetRequiredService<GeminiNutritionAiProvider>());
builder.Services.AddTransient<INutritionAiProvider>(sp => sp.GetRequiredService<AnthropicNutritionAiProvider>());
builder.Services.AddScoped<INutritionAssistantModelClient, NutritionAiProviderRouter>();
// Yemek fotografindan kalori analizi: goruntu modeli (ilk saglayici Gemini) IFoodImageAnalysisProvider arkasinda.
// Anahtar appsettings'e yazilmaz; FoodAnalysis__Gemini__ApiKey ya da (asistanla ortak) GEMINI_API_KEY ortam degiskeni.
// Anahtar yoksa yalnizca analiz ucu 503 doner. Fotograf saklanmaz.
builder.Services.Configure<FoodAnalysisOptions>(builder.Configuration.GetSection(FoodAnalysisOptions.SectionName));
var foodAnalysisOptions = builder.Configuration.GetSection(FoodAnalysisOptions.SectionName).Get<FoodAnalysisOptions>() ?? new FoodAnalysisOptions();
builder.Services.AddSingleton<FoodAnalysisGate>();
builder.Services.AddHttpClient<IFoodImageAnalysisProvider, GeminiFoodImageAnalysisProvider>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<FoodAnalysisOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.Gemini.BaseUrl)) client.BaseAddress = new Uri(options.Gemini.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds) + 10);
}).ConfigurePrimaryHttpMessageHandler(NutritionAiHandler);
builder.Services.AddScoped<IFoodAnalysisService, FoodAnalysisService>();
// E-posta dogrulama / sifre sifirlama: saglayici (Resend) tek bir siniftadir (ResendEmailSender).
// API anahtari appsettings'e yazilmaz: yerelde User Secrets "Resend:ApiKey", production'da Resend__ApiKey
// ortam degiskeni. Anahtar yoksa uygulama yine acilir; yalnizca e-posta gerektiren uclar 503 doner.
builder.Services.Configure<ResendEmailOptions>(builder.Configuration.GetSection(ResendEmailOptions.SectionName));
builder.Services.Configure<AccountEmailOptions>(builder.Configuration.GetSection(AccountEmailOptions.SectionName));
var accountEmailOptions = builder.Configuration.GetSection(AccountEmailOptions.SectionName).Get<AccountEmailOptions>() ?? new AccountEmailOptions();
var resendEmailOptions = builder.Configuration.GetSection(ResendEmailOptions.SectionName).Get<ResendEmailOptions>() ?? new ResendEmailOptions();
builder.Services.AddResend(options =>
{
    options.ApiToken = builder.Configuration[$"{ResendEmailOptions.SectionName}:{nameof(ResendEmailOptions.ApiKey)}"] ?? string.Empty;
    options.ThrowExceptions = true;
}).ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(Math.Max(3, resendEmailOptions.TimeoutSeconds)));
builder.Services.AddScoped<IEmailSender, ResendEmailSender>();
builder.Services.AddScoped<IAccountEmailService, AccountEmailService>();
// Google ile giris/kayit: istemci kimlikleri GoogleAuth:ClientIds (production'da GoogleAuth__ClientIds__0, __1 ...).
// Tanimli degilse uygulama acilir; yalnizca /api/auth/google uclari 503 doner.
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();

// MB Shop (e-ticaret): katalog, kalici sepet, favoriler, adresler ve siparisler. Urun/siparis yonetimi admin
// panelindedir (MerfitApi). Kargo kurali ve odeme yontemleri "Store" bolumunden; banka bilgisi ortam degiskeni ile
// (Store__BankTransfer__Iban, Store__BankTransfer__AccountName). Kartla odeme icin IStorePaymentProvider kaydi YOKTUR:
// bir saglayici entegre edilene kadar "card" yontemi kapali gorunur.
builder.Services.Configure<StoreOptions>(builder.Configuration.GetSection(StoreOptions.SectionName));
var storeOptions = builder.Configuration.GetSection(StoreOptions.SectionName).Get<StoreOptions>() ?? new StoreOptions();
builder.Services.AddScoped<IStoreCatalogService, StoreCatalogService>();
builder.Services.AddScoped<IStoreCartService, StoreCartService>();
builder.Services.AddScoped<IStoreFavoriteService, StoreFavoriteService>();
builder.Services.AddScoped<IStoreAddressService, StoreAddressService>();
builder.Services.AddScoped<IStoreOrderService, StoreOrderService>();
builder.Services.AddScoped<IStorePaymentResultService, StorePaymentResultService>();

// Sohbet ve fotograf analizi uclari icin kullanici ve IP basina dakikalik hiz siniri; e-posta dogrulama /
// sifre sifirlama uclari icin IP basina pencere siniri; MB Shop yazma uclari icin kullanici basina sinir.
// Diger uclar etkilenmez.
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        NutritionAssistantRateLimits.Create(nutritionAssistantOptions),
        AccountEmailRateLimits.Create(accountEmailOptions),
        FoodAnalysisRateLimits.Create(foodAnalysisOptions),
        StoreRateLimits.Create(storeOptions));
    options.OnRejected = (context, cancellationToken) =>
        AccountEmailRateLimits.IsLimitedPath(context.HttpContext) ? AccountEmailRateLimits.OnRejectedAsync(context, cancellationToken)
        : FoodAnalysisRateLimits.IsLimitedPath(context.HttpContext) ? FoodAnalysisRateLimits.OnRejectedAsync(context, cancellationToken)
        : StoreRateLimits.IsLimitedPath(context.HttpContext) ? StoreRateLimits.OnRejectedAsync(context, cancellationToken)
        : NutritionAssistantRateLimits.OnRejectedAsync(context, cancellationToken);
});
builder.Services.AddScoped<NutritionAssistantContextBuilder>();
builder.Services.AddScoped<INutritionAssistantService, NutritionAssistantService>();
builder.Services.AddScoped<IProgressService, ProgressService>();
// Ilerleme takvimi / ana sayfa haftalik aktivite, kilo hatirlatmasi, basarilar ve kampanyalar.
builder.Services.AddScoped<IActivityCalendarService, ActivityCalendarService>();
builder.Services.AddScoped<IWeightReminderService, WeightReminderService>();
builder.Services.AddScoped<IAchievementService, AchievementService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
// MB FIT puan motoru: kullanicinin aksiyonlarindan puan, siralama ve basari hesaplar.
builder.Services.Configure<MerfitScoringOptions>(builder.Configuration.GetSection(MerfitScoringOptions.SectionName));
builder.Services.AddScoped<IMerfitScoreService, MerfitScoreService>();

// Mini oyun (Dumbbell Dash): sunucuda dogrulanan, limitli ve idempotent MB FIT puan odulu.
// Hatali odul ayarinda uygulama baslangicta durur (bkz. MiniGameRewardOptionsValidator).
builder.Services.AddOptions<MiniGameRewardOptions>()
    .Bind(builder.Configuration.GetSection(MiniGameRewardOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MiniGameRewardOptions>, MiniGameRewardOptionsValidator>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddScoped<IMiniGameService, MiniGameService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IFitnessProfileService, FitnessProfileService>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();

// Antrenman ozellik puanlarinin (WorkoutAttributeScore) admin yonetimi.
builder.Services.AddScoped<IWorkoutAttributeScoreService, WorkoutAttributeScoreService>();

// Kisisellestirme altyapisi (FAZ 2): kurallara dayali (AI/ML KULLANMAYAN) antrenman + beslenme
// programi uretimi, arka planda isleme (worker) ve kisisel plan/durum okuma servisleri.
// Hedef agirlik profilleri (PrimaryAttributeWeightsByGoal) hatali/eksikse uygulama baslangicta acik bir
// hatayla durur; sessizce yanlis puanlama yapilmaz (bkz. WorkoutPlanGenerationOptionsValidator).
// Eski "AttributeWeightsByGoal" anahtari (alti ozellikli eski agirliklar) artik okunmaz: sunucuda kalan eski
// bir ayar dosyasi yeni uc ozellikli agirliklari ezip uygulamanin acilmasini engellemesin diye anahtar
// yeniden adlandirildi; eski anahtar bulunursa acilista uyari loglanir.
builder.Services.AddOptions<WorkoutPlanGenerationOptions>()
    .Bind(builder.Configuration.GetSection(WorkoutPlanGenerationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<WorkoutPlanGenerationOptions>, WorkoutPlanGenerationOptionsValidator>();
builder.Services.Configure<NutritionPlanGenerationOptions>(builder.Configuration.GetSection(NutritionPlanGenerationOptions.SectionName));
builder.Services.Configure<PersonalizationJobProcessingOptions>(builder.Configuration.GetSection(PersonalizationJobProcessingOptions.SectionName));

builder.Services.AddScoped<IWorkoutPlanGenerator, WorkoutPlanGenerator>();
builder.Services.AddScoped<INutritionPlanGenerator, NutritionPlanGenerator>();
builder.Services.AddScoped<IPersonalizationJobProcessor, PersonalizationJobProcessor>();
builder.Services.AddScoped<IMyPlanService, MyPlanService>();
builder.Services.AddScoped<IMyNutritionPlanService, MyNutritionPlanService>();
builder.Services.AddScoped<IPersonalizationStatusService, PersonalizationStatusService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// Bildirim motoru: cihaz kaydi, kural planlayici, tek gonderici (outbox) ve Expo push saglayicisi.
// Ayarlar "Notifications" bolumunden; Expo erisim jetonu yalnizca ortam degiskeni ile verilir
// (Notifications__Expo__AccessToken). Bkz. NotificationOptions.
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddHttpClient<IPushNotificationService, ExpoPushNotificationService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value.Expo;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddScoped<INotificationDeviceService, NotificationDeviceService>();
builder.Services.AddScoped<INotificationEligibilityService, NotificationEligibilityService>();
builder.Services.AddScoped<INotificationReminderCanceller, NotificationReminderCanceller>();
builder.Services.AddScoped<INotificationQueueStore, NotificationQueueStore>();
builder.Services.AddScoped<INotificationRulePlanner, NotificationRulePlanner>();
builder.Services.AddScoped<INotificationDispatchProcessor, NotificationDispatchProcessor>();
builder.Services.AddHostedService<NotificationBackgroundWorker>();

// PersonalizationJob(Pending) kayitlarini periyodik olarak isleyen arka plan calisani.
// Composition Root'ta (API projesi) kayitlidir - bkz. PersonalizationBackgroundWorker XML docs
// (Infrastructure projesi bilinçli olarak Business'a referans vermiyor).
builder.Services.AddHostedService<PersonalizationBackgroundWorker>();

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
        ClockSkew = TimeSpan.FromMinutes(1),
    };
});

builder.Services.AddAuthorization(options =>
{
    // Admin API'nin tamami bu policy ile korunur (bkz. AdminControllerBase).
    // Hem Admin hem SuperAdmin rolundeki kullanicilar admin panelini kullanabilir;
    // normal "User" rolundeki mobil uygulama kullanicilari 403 Forbidden alir.
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole(UserRole.Admin.ToString(), UserRole.SuperAdmin.ToString()));
});

// CORS - gelistirme ortaminda Expo/web ve mobil istemcilerin erisebilmesi,
// production'da ise yalnizca tanimli origin'lerin erisebilmesi icin yapilandirilir.
const string AdminAppCorsPolicy = "AdminAppCorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AdminAppCorsPolicy, policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins("http://localhost:3000")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

if (builder.Configuration.GetSection($"{WorkoutPlanGenerationOptions.SectionName}:AttributeWeightsByGoal").Exists())
{
    // Eski alti ozellikli agirliklar yok sayilir; gecerli agirliklar PrimaryAttributeWeightsByGoal veya kod varsayilanlaridir.
    app.Logger.LogWarning("WorkoutPlanGeneration:AttributeWeightsByGoal artik kullanilmiyor ve yok sayildi. " +
        "Sunucudaki ayar dosyasindan bu bolumu kaldirin; agirliklar icin WorkoutPlanGeneration:PrimaryAttributeWeightsByGoal kullanilir.");
}

if (string.IsNullOrWhiteSpace(resendEmailOptions.ApiKey))
{
    // Uygulama acilisi bozulmaz; e-posta dogrulama ve sifre sifirlama e-postalari gonderilemez (503).
    app.Logger.LogWarning("Resend:ApiKey tanimli degil. E-posta dogrulama ve sifre sifirlama e-postalari gonderilemeyecek. " +
        "Yerelde: dotnet user-secrets set \"Resend:ApiKey\" \"re_...\"; production'da: Resend__ApiKey ortam degiskeni.");
}

if (!app.Services.GetRequiredService<IGoogleIdTokenValidator>().IsConfigured)
{
    app.Logger.LogWarning("GoogleAuth:ClientIds tanimli degil. Google ile giris/kayit 503 donecek. " +
        "Production'da: GoogleAuth__ClientIds__0 (Web istemcisi) ve GoogleAuth__ClientIds__1 (iOS istemcisi) ortam degiskenleri.");
}

// Pipeline'daki tum istisnalari yakalayip tutarli bir ApiResponse govdesine ceviren middleware;
// dogru HTTP status kodlarinin donmesi icin (bkz. madde 38) pipeline'in en basina eklenir.
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MerfitCustomerApi v1");
        options.RoutePrefix = "swagger"; // https://localhost:{port}/swagger
    });
}
else
{
    // Mobil ve gelistirme ortamlarinda HTTP erisimi ve self-signed SSL guven sorunlarini onlemek icin
    // HTTPS zorunlulugu yalnizca production ortaminda devreye girer.
    app.UseHttpsRedirection();
}

app.UseCors(AdminAppCorsPolicy);

app.UseAuthentication();
// Hiz siniri kullanici kimligini token'dan okudugu icin kimlik dogrulamadan sonra calisir.
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();