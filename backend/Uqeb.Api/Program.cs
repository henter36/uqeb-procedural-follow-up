using System.IO.Compression;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Uqeb.Api.Authorization;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Middleware;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;
using Uqeb.Api.Services.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<FeatureFlagsSettings>(builder.Configuration.GetSection(FeatureFlagsSettings.SectionName));
builder.Services.AddOptions<ReportingOptions>()
    .Bind(builder.Configuration.GetSection(ReportingOptions.SectionName))
    .Validate(o => o.MaxPdfDetailRows > 0, "Reporting:MaxPdfDetailRows must be greater than zero.")
    .ValidateOnStart();

var useInMemoryDatabase = builder.Configuration.GetValue<bool>("Testing:UseInMemoryDatabase");
if (!useInMemoryDatabase)
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlServer(connectionString));
    builder.Services.AddScoped(sp =>
        sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITrackingNumberService, TrackingNumberService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IExcelTransactionImportService, ExcelTransactionImportService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IInstitutionalReportService, InstitutionalReportService>();
builder.Services.AddScoped<IInstitutionalReportNumberAllocator, InstitutionalReportNumberAllocator>();
builder.Services.AddSingleton<IInstitutionalReportPdfExporter, InstitutionalReportPlaywrightPdfExporter>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IExternalPartyService, ExternalPartyService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ILetterTemplateService, LetterTemplateService>();
builder.Services.AddScoped<ISecurityAuditService, SecurityAuditService>();
builder.Services.AddScoped<IHealthDatabaseProbe, DbContextHealthDatabaseProbe>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwtSettings.Key))
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException("JWT Key must be configured in Production (Jwt:Key).");

    throw new InvalidOperationException(
        "JWT Key is not configured. Set Jwt:Key in user-secrets or appsettings.Development.json for Development.");
}

if (jwtSettings.Key.Length < 32)
    throw new InvalidOperationException("JWT Key must be at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(UserRole.Admin.ToString()));
    options.AddPolicy(Policies.SupervisorOrAdmin, p => p.RequireRole(
        UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
    options.AddPolicy(Policies.CanEditTransactions, p => p.RequireRole(
        UserRole.Admin.ToString(), UserRole.Supervisor.ToString(), UserRole.DataEntry.ToString()));
    options.AddPolicy(Policies.CanCloseTransactions, p => p.RequireRole(
        UserRole.Admin.ToString(), UserRole.Supervisor.ToString()));
    options.AddPolicy(Policies.CanManageUsers, p => p.RequireRole(UserRole.Admin.ToString()));
});

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheInvalidationService, CacheInvalidationService>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.AddControllers();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins("http://localhost:5173", "http://localhost:8080")
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
        else
        {
            var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
            if (origins == null || origins.Length == 0)
                throw new InvalidOperationException("AllowedOrigins must be configured in Production.");

            policy.WithOrigins(origins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "تعذر الاتصال بقاعدة البيانات أو تنفيذ Seed. تأكد من تشغيل SQL Server وتطبيق Migrations.");
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors();
app.UseResponseCompression();
app.UseRateLimiter();
app.UseMiddleware<Uqeb.Api.Middleware.UnauthorizedAccessLoggingMiddleware>();
app.UseMiddleware<InstitutionalReportsFeatureGateMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program
{
    protected Program()
    {
    }
}
