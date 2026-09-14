using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.Dashboard;
using MerfitCustomerApi.Business.Services.Equipment;
using MerfitCustomerApi.Business.Services.Foods;
using MerfitCustomerApi.Business.Services.Leaderboard;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Services.Personalization;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Services.Profile;
using MerfitCustomerApi.Business.Services.Progress;
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
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

// MerfitNativeApp (mobil musteri uygulamasi) icin musteri-yuzlu servisler.
builder.Services.AddScoped<IWorkoutService, WorkoutService>();
builder.Services.AddScoped<IWorkoutSessionService, WorkoutSessionService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<INutritionService, NutritionService>();
builder.Services.AddScoped<IFoodService, FoodService>();
builder.Services.AddScoped<IProgressService, ProgressService>();
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();

// Kisisellestirme altyapisi (FAZ 2): kurallara dayali (AI/ML KULLANMAYAN) antrenman + beslenme
// programi uretimi, arka planda isleme (worker) ve kisisel plan/durum okuma servisleri.
builder.Services.Configure<WorkoutPlanGenerationOptions>(builder.Configuration.GetSection(WorkoutPlanGenerationOptions.SectionName));
builder.Services.Configure<NutritionPlanGenerationOptions>(builder.Configuration.GetSection(NutritionPlanGenerationOptions.SectionName));
builder.Services.Configure<PersonalizationJobProcessingOptions>(builder.Configuration.GetSection(PersonalizationJobProcessingOptions.SectionName));

builder.Services.AddScoped<IWorkoutPlanGenerator, WorkoutPlanGenerator>();
builder.Services.AddScoped<INutritionPlanGenerator, NutritionPlanGenerator>();
builder.Services.AddScoped<IPersonalizationJobProcessor, PersonalizationJobProcessor>();
builder.Services.AddScoped<IMyPlanService, MyPlanService>();
builder.Services.AddScoped<IMyNutritionPlanService, MyNutritionPlanService>();
builder.Services.AddScoped<IPersonalizationStatusService, PersonalizationStatusService>();

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
app.UseAuthorization();

app.MapControllers();

app.Run();