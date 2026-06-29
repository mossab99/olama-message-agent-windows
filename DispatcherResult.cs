using System.Collections.Generic;

namespace OlamaMessageAgent
{
    /// <summary>
    /// Result of a single dispatcher poll cycle (LoadConfig or Reserve call).
    /// </summary>
    public class DispatcherResult
    {
        /// <summary>Whether the HTTP request itself succeeded (2xx response).</summary>
        public bool Success { get; set; }

        /// <summary>HTTP status code returned by WordPress, or 0 on network failure.</summary>
        public int StatusCode { get; set; }

        /// <summary>Human-readable summary of the outcome (safe to log).</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Jobs that were reserved in this poll cycle.
        /// In Run 4C (dry-run mode) these are logged but never sent.
        /// </summary>
        public List<DispatchJob> JobsReserved { get; set; } = new List<DispatchJob>();

        // ── Static factory helpers ──────────────────────────────────────────

        public static DispatcherResult Ok(string message, List<DispatchJob> jobs = null)
        {
            return new DispatcherResult
            {
                Success = true,
                StatusCode = 200,
                Message = message,
                JobsReserved = jobs ?? new List<DispatchJob>()
            };
        }

        public static DispatcherResult Fail(int statusCode, string message)
        {
            return new DispatcherResult
            {
                Success = false,
                StatusCode = statusCode,
                Message = message,
                JobsReserved = new List<DispatchJob>()
            };
        }
    }
}
