using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.TokenService;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using MerfitCustomerApi.Infrastructure.Persistence;
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

// CORS - merfit-admin-app (CRA dev server, localhost:3000) buradan istek atabilsin diye.
// Bearer token header ile calistigimiz (cookie tabanli auth kullanmadigimiz) icin
// AllowCredentials'a ihtiyac yok; origin'i yine de sabit tutuyoruz.
const string AdminAppCorsPolicy = "AdminAppCorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AdminAppCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
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

app.UseHttpsRedirection();

app.UseCors(AdminAppCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();