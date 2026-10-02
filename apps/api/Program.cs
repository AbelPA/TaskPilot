using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Endpoints;
using Api.AudioExtractions.Messaging;
using Api.AudioExtractions.Notifications;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddAudioExtractions(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseRateLimiter();

app.MapGet(
    "/health",
    async (Api.AudioExtractions.Persistence.AudioExtractionDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "healthy" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .WithName("Health");
app.MapAudioExtractionEndpoints();
app.MapHub<AudioExtractionsHub>("/hubs/audio-extractions");

if (app.Configuration.GetValue("AudioExtraction:ApplyMigrationsOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<
        Api.AudioExtractions.Persistence.AudioExtractionDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();

public partial class Program;
