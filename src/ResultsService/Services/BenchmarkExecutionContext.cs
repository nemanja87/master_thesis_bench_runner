using ResultsService.Models;
namespace ResultsService.Services;

public record BenchmarkExecutionContext
(
    Guid RunId,
    BenchRunRequest Request,
    string Protocol,
    string SecurityProfile,
    string CallPath,
    string TargetUrl,
    string? JwtToken,
    bool UseMtls,
    bool UseTls,
    string WorkingDirectory
);
