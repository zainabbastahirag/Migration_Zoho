using Microsoft.EntityFrameworkCore;
using OdooToZohoMigration.Core.Interfaces;
using OdooToZohoMigration.Infrastructure.Configuration;
using OdooToZohoMigration.Infrastructure.Data;
using OdooToZohoMigration.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Database ─────────────────────────────────────────────────────────
builder.Services.AddDbContext<MigrationDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => { sql.CommandTimeout(300); sql.EnableRetryOnFailure(3); }));

// ── Config sections ──────────────────────────────────────────────────
builder.Services.Configure<MigrationSettings>(
    builder.Configuration.GetSection("MigrationSettings"));
builder.Services.Configure<ZohoRecruitSettings>(
    builder.Configuration.GetSection("ZohoRecruit"));

// ── Services ─────────────────────────────────────────────────────────
builder.Services.AddHttpClient<IZohoRecruitService, ZohoRecruitService>(c =>
    c.Timeout = TimeSpan.FromMinutes(5));

builder.Services.AddScoped<IMigrationService, MigrationService>();
builder.Services.AddScoped<IBlobStorageService, PlaceholderBlobStorageService>();
builder.Services.AddMemoryCache();

// ── Background worker (the only thing that matters) ──────────────────
builder.Services.AddHostedService<MigrationBackgroundService>();

var app = builder.Build();
app.Run();

// =====================================================================
//  Placeholder blob service – replace with your Azure / S3 / local impl
// =====================================================================
public class PlaceholderBlobStorageService : IBlobStorageService
{
    public Task<(Stream? Stream, string? ContentType, string? FileName)> GetCvWithMetadataAsync(
        string blobUrl, CancellationToken ct = default)
    {
        // TODO: wire up your real blob storage here
        return Task.FromResult<(Stream?, string?, string?)>((null, null, null));
    }
}
