using System;

namespace OlamaMessageAgent
{
    /// <summary>
    /// Dispatcher configuration fetched from WordPress /agent/jobs/config.
    /// All fields are server-controlled; the agent treats them as read-only.
    /// </summary>
    public class DispatcherConfig
    {
        /// <summary>How many seconds between reserve-poll attempts.</summary>
        public int PollIntervalSeconds { get; set; } = 10;

        /// <summary>Maximum jobs to request per reserve call.</summary>
        public int MaxJobsPerPoll { get; set; } = 1;

        /// <summary>Minimum seconds the agent must wait between consecutive SMS sends (rate limiter).</summary>
        public int MinSecondsBetweenSms { get; set; } = 10;

        /// <summary>Maximum SMS messages the agent may send within a rolling 60-minute window.</summary>
        public int MaxSmsPerHour { get; set; } = 360;

        /// <summary>How many seconds a reservation is considered valid before WordPress reclaims it.</summary>
        public int ReservationTtlSeconds { get; set; } = 180;

        /// <summary>UTC timestamp when this config was loaded from the server.</summary>
        public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
    }
}
