using System.ComponentModel.DataAnnotations;

namespace ResultsService.Models;

public class BenchRunRequest
{
    [Required]
    public string Protocol { get; set; } = string.Empty; // rest | grpc

    [Required]
    public string Security { get; set; } = string.Empty; // S0-S4

    [Required]
    public string Workload { get; set; } = string.Empty; // orders-create

    [Range(1, 100_000)]
    public int Rps { get; set; }

    [Range(1, 10_000)]
    public int Connections { get; set; }

    [Range(1, 86_400)]
    public int Duration { get; set; }

    [Range(0, 3_600)]
    public int Warmup { get; set; }
}
