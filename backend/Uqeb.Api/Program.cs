using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Uqeb.Api.Authorization;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITrackingNumberService, TrackingNumberService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IExternalPartyService, ExternalPartyService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrEmpty(jwtSettings.Key))
    jwtSettings.Key = "UqebDevSecretKey_ChangeInProduction_32chars!";

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
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
