using System;

namespace OlamaMessageAgent
{
    /// <summary>
    /// Represents a single SMS job reserved from the WordPress campaign queue.
    /// The full message body is held in memory only during a single poll cycle
    /// and is never written to disk, never cached across restarts, and never logged.
    /// </summary>
    public class DispatchJob
    {
        /// <summary>Primary key of the queue record on the WordPress side.</summary>
        public int QueueId { get; set; }
        public string AttemptId { get; set; }

        /// <summary>Campaign this job belongs to.</summary>
        public int CampaignId { get; set; }

        /// <summary>E.164-normalised destination phone number.</summary>
        public string PhoneE164 { get; set; } = string.Empty;

        /// <summary>
        /// Fully rendered message body, including any substituted payment URL.
        /// DO NOT log this field — it may contain tokenised payment links.
        /// </summary>
        public string MessageBody { get; set; } = string.Empty;

        /// <summary>Character count of the rendered message body (safe to log).</summary>
        public int MessageLength { get; set; }

        /// <summary>UTC deadline after which the WordPress server reclaims the reservation.</summary>
        public DateTime ExpiresAt { get; set; }

        // ── Logging helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns a masked version of PhoneE164 safe for log output.
        /// E.g. +96279123456 → +96279***456
        /// </summary>
        public string MaskedPhone
        {
            get
            {
                string p = PhoneE164 ?? string.Empty;
                if (p.Length < 7) return "***";
                int suffixLen = 3;
                int prefixLen = p.Length - suffixLen - 3; // hide 3 middle digits minimum
                if (prefixLen < 1) prefixLen = 1;
                return p.Substring(0, prefixLen) + "***" + p.Substring(p.Length - suffixLen);
            }
        }
    }
}
