using Microsoft.EntityFrameworkCore;
using transactions_ingest.Core.Domain;

namespace transactions_ingest.Core.Persistence;

public class TransactionsDbContext(DbContextOptions<TransactionsDbContext> options) : DbContext(options)
{
    /// <summary>Max length for source-provided strings ("length &lt; 20", D2).</summary>
    public const int MaxFieldLength = 19;

    private const int MaxEnumLength = 20;

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionAudit> TransactionAudits => Set<TransactionAudit>();

    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public DbSet<ImportRunSource> ImportRunSources => Set<ImportRunSource>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HaveConversion<DecimalToCentsConverter>();
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();

        configurationBuilder.Properties<TransactionStatus>().HaveConversion<string>().HaveMaxLength(MaxEnumLength);
        configurationBuilder.Properties<AuditChangeType>().HaveConversion<string>().HaveMaxLength(MaxEnumLength);
        configurationBuilder.Properties<ImportRunStatus>().HaveConversion<string>().HaveMaxLength(MaxEnumLength);
        configurationBuilder.Properties<ImportSourceStatus>().HaveConversion<string>().HaveMaxLength(MaxEnumLength);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasIndex(t => new { t.SourceSystem, t.TransactionId }).IsUnique();

            e.Property(t => t.SourceSystem).HasMaxLength(MaxFieldLength);
            e.Property(t => t.CardNumber).HasMaxLength(MaxFieldLength);
            e.Property(t => t.LocationCode).HasMaxLength(MaxFieldLength);
            e.Property(t => t.ProductName).HasMaxLength(MaxFieldLength);
        });

        modelBuilder.Entity<TransactionAudit>(e =>
        {
            // Audit history must outlive any accidental delete of its parent rows.
            e.HasOne(a => a.Transaction)
                .WithMany(t => t.Audits)
                .HasForeignKey(a => a.TransactionPk)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.ImportRun)
                .WithMany()
                .HasForeignKey(a => a.ImportRunId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(a => a.FieldName).HasMaxLength(50);
        });

        modelBuilder.Entity<ImportRunSource>(e =>
        {
            e.HasOne(s => s.ImportRun)
                .WithMany(r => r.Sources)
                .HasForeignKey(s => s.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(s => new { s.ImportRunId, s.SourceSystem }).IsUnique();

            e.Property(s => s.SourceSystem).HasMaxLength(MaxFieldLength);
        });
    }
}
