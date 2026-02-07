using Microsoft.EntityFrameworkCore;
using OdooToZohoMigration.Core.Interfaces;
using OdooToZohoMigration.Infrastructure.Configuration;
using OdooToZohoMigration.Infrastructure.Data;
using OdooToZohoMigration.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ====================================================================
// DATABASE
// ====================================================================
builder.Services.AddDbContext<MigrationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.CommandTimeout(300); // 5 min for long-running queries
            sqlOptions.EnableRetryOnFailure(3);
        }));

// ====================================================================
// CONFIGURATION
// ====================================================================
builder.Services.Configure<MigrationSettings>(
    builder.Configuration.GetSection("MigrationSettings"));

builder.Services.Configure<ZohoRecruitSettings>(
    builder.Configuration.GetSection("ZohoRecruit"));

// ====================================================================
// SERVICES
// ====================================================================

// Zoho Recruit HTTP client
builder.Services.AddHttpClient<IZohoRecruitService, ZohoRecruitService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Migration service
builder.Services.AddScoped<IMigrationService, MigrationService>();

// Blob storage service - register your implementation here
// For now, a placeholder that throws NotImplementedException
builder.Services.AddScoped<IBlobStorageService, PlaceholderBlobStorageService>();

// Memory cache for Zoho tokens
builder.Services.AddMemoryCache();

// ====================================================================
// API
// ====================================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Allow long-running requests
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
});

var app = builder.Build();

// ====================================================================
// MIDDLEWARE
// ====================================================================
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Odoo to Zoho Migration API v1");
    c.RoutePrefix = string.Empty; // Swagger at root
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// ====================================================================
// PLACEHOLDER: Replace with your actual blob storage implementation
// ====================================================================
public class PlaceholderBlobStorageService : IBlobStorageService
{
    public Task<(Stream? Stream, string? ContentType, string? FileName)> GetCvWithMetadataAsync(
        string blobUrl, CancellationToken ct = default)
    {
        // TODO: Replace with your Azure Blob Storage or S3 implementation
        // This placeholder returns null (CV not found) so migration won't crash
        return Task.FromResult<(Stream?, string?, string?)>((null, null, null));
    }
}
