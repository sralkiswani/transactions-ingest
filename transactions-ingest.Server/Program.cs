using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using transactions_ingest.Core.Import;
using transactions_ingest.Core.Import.Mock;
using transactions_ingest.Core.Persistence;
using transactions_ingest.Server;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Resolved when the first context is created, so configuration added by tests is seen.
builder.Services.AddDbContextFactory<TransactionsDbContext>((sp, options) =>
    options.UseSqlite(SqliteConnectionString(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>())));
builder.Services.AddTransactionsImport(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Single instance for now, so migrating on startup is safe (D9).
using (var db = app.Services.GetRequiredService<IDbContextFactory<TransactionsDbContext>>().CreateDbContext())
{
    db.Database.Migrate();
}

if (app.Services.GetRequiredService<IOptions<ImportOptions>>().Value.MockSources.GenerateMissing)
{
    app.Services.GetRequiredService<MockSourceFiles>().EnsureGenerated();
}

// Errors are ProblemDetails. Outside Development the handler omits exception details; in Development
// the developer exception page (on by default) includes them.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}
app.UseStatusCodePages();

app.UseDefaultFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Redirect to HTTPS only when the server has an HTTPS endpoint. The container and the API-only run serve plain
// HTTP, where the middleware could only log "Failed to determine the https port for redirect". The bound
// addresses are known once the server has started, so this is decided on the first request.
var hasHttpsEndpoint = new Lazy<bool>(() => HttpsEndpoints.Any(app.Services.GetRequiredService<IServer>()));
app.UseWhen(_ => hasHttpsEndpoint.Value, https => https.UseHttpsRedirection());

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();

// A relative SQLite path is resolved against the content root, not the process working directory.
static string SqliteConnectionString(IConfiguration configuration, IHostEnvironment environment)
{
    var connectionString = configuration.GetConnectionString("Transactions")
        ?? throw new InvalidOperationException("Connection string 'Transactions' is not configured.");

    var sqlite = new SqliteConnectionStringBuilder(connectionString);
    if (sqlite.DataSource != ":memory:" && !Path.IsPathRooted(sqlite.DataSource))
    {
        sqlite.DataSource = Path.Combine(environment.ContentRootPath, sqlite.DataSource);
    }
    return sqlite.ToString();
}

/// <summary>Exposed for WebApplicationFactory in the API tests.</summary>
public partial class Program;
