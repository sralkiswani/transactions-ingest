using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using transactions_ingest.Core.Import.Mock;

namespace transactions_ingest.Core.Import;

public static class ImportServiceCollectionExtensions
{
    /// <summary>
    /// Registers the import (D1, D8). Requires an <c>IDbContextFactory&lt;TransactionsDbContext&gt;</c>.
    /// A scheduled job resolves <see cref="ImportService"/> the same way the API does and shares the lock.
    /// </summary>
    public static IServiceCollection AddTransactionsImport(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ImportOptions.SectionName);
        services.AddOptions<ImportOptions>().Bind(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ImportOptions>, ImportOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IImportLock, InMemoryImportLock>();
        services.AddScoped<ImportService>();

        services.AddSingleton<MockSourceFiles>();
        var names = section.GetSection("MockSources:Names").Get<string[]>() ?? [];
        foreach (var name in names)
        {
            services.AddSingleton<ITransactionSource>(sp =>
                new JsonFileTransactionSource(name, sp.GetRequiredService<MockSourceFiles>().PathFor(name)));
        }

        services.AddHostedService<SourceRegistrationCheck>();
        return services;
    }
}
