using ResultsService.Models;

namespace ResultsService.Services;

public interface IBenchmarkToolRunner
{
    Task<BenchmarkRunResult> RunAsync(BenchmarkExecutionContext context, CancellationToken cancellationToken);
}
