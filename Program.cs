using IsotopeMix.Domain;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<EventStore>(_ =>
{
    var dbPath = Environment.GetEnvironmentVariable("ISOTOPE_DB") ?? "isotope-mix.db";
    return new EventStore($"Data Source={dbPath}");
});
builder.Services.AddSingleton<AppService>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { ok = true }));
app.Run();
