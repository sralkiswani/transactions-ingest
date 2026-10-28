using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using transactions_ingest.Core.Persistence;
using transactions_ingest.Tests.Import;
using transactions_ingest.Tests.Import.Service;

namespace transactions_ingest.Tests.Api;

/// <summary>
/// The real app with its own SQLite file and mock-source folder in a temp directory, and a controllable clock.
/// Runs in Development by default, so the mock source files are generated on start like a local run.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string Directory { get; } =
        Path.Combine(Path.GetTempPath(), "transactions-ingest-tests", Guid.NewGuid().ToString("N"));

    internal TestTimeProvider Time { get; } = new(ImportTestData.Now);

    public string EnvironmentName { get; init; } = "Development";

    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Extra configuration values, applied over appsettings.</summary>
    public Dictionary<string, string> Settings { get; init; } = [];

    public string DatabasePath => Path.Combine(Directory, "test.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        System.IO.Directory.CreateDirectory(Directory);
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:Transactions", $"Data Source={DatabasePath}");
        builder.UseSetting("Import:MockSources:Directory", Directory);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            ConfigureServices?.Invoke(services);
        });
    }

    public TransactionsDbContext CreateDbContext() =>
        Services.GetRequiredService<IDbContextFactory<TransactionsDbContext>>().CreateDbContext();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; the folder is under the temp directory.
            }
        }
    }
}
