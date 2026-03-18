using AGONEAIHub.API.Middleware;
using AGONEAIHub.Core.Interfaces;
using AGONEAIHub.Infrastructure.Configuration;
using AGONEAIHub.Infrastructure.Data;
using AGONEAIHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database (EF Core Code First) ────────────────────────────────────
builder.Services.AddDbContext<AIHubDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.CommandTimeout(120);
            sql.EnableRetryOnFailure(3);
            sql.MigrationsHistoryTable("__EFMigrationsHistory", "aihub");
        }));

// ── Configuration ────────────────────────────────────────────────────
builder.Services.Configure<OpenAISettings>(
    builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<AzureDocIntelligenceSettings>(
    builder.Configuration.GetSection("AzureDocumentIntelligence"));
builder.Services.Configure<AzureAISearchSettings>(
    builder.Configuration.GetSection("AzureAISearch"));

// ── Core Services ────────────────────────────────────────────────────
builder.Services.AddScoped<IPromptService, PromptService>();
builder.Services.AddScoped<IChatService, OpenAIChatService>();
builder.Services.AddScoped<IDocumentIntelligenceService, AzureDocumentIntelligenceService>();
builder.Services.AddScoped<IAISearchService, AzureAISearchService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// ── Project-specific Services ────────────────────────────────────────
builder.Services.AddScoped<ISpotService, SpotService>();

// ── AGONESPot — single service for all Spot operations ───────────────
builder.Services.Configure<AGONEAIHub.Infrastructure.Configuration.SpotSettings>(
    builder.Configuration.GetSection("AGONESPot"));
builder.Services.AddSingleton<AGONEAIHub.Infrastructure.Services.Spot.SpotServiceBus>();
builder.Services.AddScoped<ISpotDataService,
    AGONEAIHub.Infrastructure.Services.Spot.SpotDataService>();

// ── API + Swagger ────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "AGONE AI Hub",
        Version = "v1",
        Description = "Centralized AI services for AGONELearn, AGONEWork, AGONESPot, AGONEPulse.\n\n" +
                      "Every request is tagged with a project. All prompts stored in DB.\n" +
                      "Full request/response logging + error notification system."
    });
    c.UseInlineDefinitionsForEnums();
});

var app = builder.Build();

// ── Auto-apply EF migrations in development ──────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AIHubDbContext>();
    db.Database.Migrate();
}

// ── Middleware ────────────────────────────────────────────────────────
app.UseMiddleware<ApiLoggingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AGONE AI Hub v1");
    c.RoutePrefix = string.Empty;
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
