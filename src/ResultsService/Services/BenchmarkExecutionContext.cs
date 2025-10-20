using ResultsService.Models;
namespace ResultsService.Services;

public record BenchmarkExecutionContext
(
    Guid RunId,
    BenchRunRequest Request,
    string Protocol,
    string SecurityProfile,
    string TargetUrl,
    string? JwtToken,
    bool UseMtls,
    bool UseTls,
    string WorkingDirectory
);
