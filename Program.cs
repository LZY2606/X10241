using IsotopeMixing.Cases;
using IsotopeMixing.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRouting();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var dbPath = Path.Combine(app.Environment.ContentRootPath, "data", "isotope.db");
var caseDir = CaseLoader.FindCaseDirectory(app.Configuration["caseDir"])
    ?? Path.Combine(app.Environment.ContentRootPath, "cases", "source-composition-chamber");
var svc = new AppServices(dbPath);
Endpoints.Map(app, svc, caseDir, dbPath);

app.Run();
