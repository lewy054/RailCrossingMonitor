using RailCrossingMonitor.Application;
using RailCrossingMonitor.Application.Crossing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddHttpClient();
builder.Services.AddHttpClient<RailwayCrossingService>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<TrainStore>();

builder.Services.AddHostedService<TrainsWorker>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
var app = builder.Build();

app.UseCors("AllowAll");

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        ok = true,
        time = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/trains", (TrainStore store) => Results.Ok((object?)store.GetAll()));
app.MapGet("/api/crossings", async (
    RailwayCrossingService service,
    CancellationToken cancellationToken) =>
{
    var crossings = await service.GetCrossingsAsync(cancellationToken);

    return Results.Ok(crossings);
});

app.MapHub<TrainHub>("/hubs/trains");

app.Run("http://localhost:5100");