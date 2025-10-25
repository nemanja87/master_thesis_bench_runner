using Microsoft.EntityFrameworkCore;

namespace ResultsService.Data;

public class ResultsDbContext(DbContextOptions<ResultsDbContext> options) : DbContext(options)
{
    public DbSet<BenchmarkRun> BenchmarkRuns => Set<BenchmarkRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<BenchmarkRun>();
        entity.ToTable("benchmark_runs");
        entity.HasKey(run => run.Id);
        entity.Property(run => run.Protocol).HasMaxLength(16).IsRequired();
        entity.Property(run => run.SecurityProfile).HasMaxLength(8).IsRequired();
        entity.Property(run => run.CallPath).HasMaxLength(16).IsRequired();
        entity.Property(run => run.Workload).HasMaxLength(64).IsRequired();
        entity.Property(run => run.Tool).HasMaxLength(16).IsRequired();
        entity.Property(run => run.SummaryPath).HasMaxLength(256);
        entity.HasIndex(run => run.StartedAt);
    }
}
