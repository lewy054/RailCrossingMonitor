using RailCrossingMonitor.Application;
using RailCrossingMonitor.Application.Crossing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RailCrossingServiceOptions>(
    builder.Configuration.GetSection(RailCrossingServiceOptions.SectionName));
builder.Services.Configure<TrainsOptions>(
    builder.Configuration.GetSection(TrainsOptions.SectionName));
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