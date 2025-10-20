using System;
using System.Collections.Generic;
using System.Linq;

namespace ResultsService.Models;

public class BenchRunValidationException : Exception
{
    public IReadOnlyCollection<string> Errors { get; }

    public BenchRunValidationException(IEnumerable<string> errors)
        : base("Bench run request is invalid.")
    {
        Errors = errors.ToArray();
    }
}
