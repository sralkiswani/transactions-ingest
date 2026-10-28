using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using transactions_ingest.Core.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("Transactions")
    ?? throw new InvalidOperationException("Connection string 'Transactions' is not configured.");

// A relative SQLite path is resolved against the content root, not the process working directory.
var sqlite = new SqliteConnectionStringBuilder(connectionString);
if (sqlite.DataSource != ":memory:" && !Path.IsPathRooted(sqlite.DataSource))
{
    sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
}

builder.Services.AddDbContext<TransactionsDbContext>(options => options.UseSqlite(sqlite.ToString()));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Single instance for now, so migrating on startup is safe (D9).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<TransactionsDbContext>().Database.Migrate();
}

app.UseDefaultFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
