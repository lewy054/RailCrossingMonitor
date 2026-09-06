using RailCrossingMonitor.Application;
using RailCrossingMonitor.Application.Crossing;
using RailCrossingMonitor.Application.RailwayTracks;
using RailCrossingMonitor.Application.Trains;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RailwayCrossingOptions>(
    builder.Configuration.GetSection(RailwayCrossingOptions.SectionName));
builder.Services.Configure<TrainsOptions>(
    builder.Configuration.GetSection(TrainsOptions.SectionName));
builder.Services.Configure<RailwayTracksOptions>(
    builder.Configuration.GetSection(RailwayTracksOptions.SectionName));
builder.Services.AddHttpClient("RailwayTracks", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<RailwayCrossingService>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<TrainStore>();
builder.Services.AddSingleton<CrossingStore>();
builder.Services.AddSingleton<RailwayTrackStore>();
builder.Services.AddSingleton<TrainPredictionService>();

builder.Services.AddHostedService<RailwayTrackWorker>();
builder.Services.AddHostedService<TrainsWorker>();
builder.Services.AddHostedService<CrossingWorker>();
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
app.MapGet("/api/trains/predicted", (TrainPredictionService service) => Results.Ok(service.GetAll()));
app.MapGet("/api/crossings/status", async (
    RailwayCrossingService service,
    CancellationToken cancellationToken) =>
{
    var crossings = await service.GetCrossingStatusesAsync(cancellationToken);
    return Results.Ok(crossings);
});


app.Run();