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
builder.Services.Configure<AzureBlobStorageSettings>(
    builder.Configuration.GetSection("AzureBlobStorage"));

// ── Services ─────────────────────────────────────────────────────────
builder.Services.AddHttpClient<IZohoRecruitService, ZohoRecruitService>(c =>
    c.Timeout = TimeSpan.FromMinutes(5));

builder.Services.AddScoped<IMigrationService, MigrationService>();
builder.Services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
builder.Services.AddMemoryCache();

// ── Background worker ────────────────────────────────────────────────
builder.Services.AddHostedService<MigrationBackgroundService>();

var app = builder.Build();
app.Run();
