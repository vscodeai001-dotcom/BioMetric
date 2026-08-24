using System;
using System.Collections.Generic;
using Payroll.Shared;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// Calculates total break time (sum of gaps between OUT→IN pairs)
    /// and the payable break penalty = max(0, totalBreak - paidBreakAllowance).
    /// NOTE: If the day only has one IN/OUT pair, total gap = 0.
    /// </summary>
    public class AttendanceBreakPenaltyService
    {
        /// <param name="orderedPunches">
        /// Must be the cleaned, chronologically ordered list returned by AttendancePunchProcessor.ProcessPunches
        /// (it guarantees pairs and adds a dummy OUT when day ends with IN).
        /// </param>
        /// <param name="paidBreakMinutes">Paid break allowance for that employee/day.</param>
        public (TimeSpan totalBreak, TimeSpan breakPenalty) CalculateBreakPenalty(
            List<AttendanceLog> orderedPunches,
            int paidBreakMinutes)
        {
            if (orderedPunches == null || orderedPunches.Count < 2)
                return (TimeSpan.Zero, TimeSpan.Zero);

            // Build segments as (IN, OUT) by pairing indices [0,1], [2,3], ...
            var segments = new List<(DateTime In, DateTime Out)>();
            for (int i = 0; i + 1 < orderedPunches.Count; i += 2)
            {
                var start = orderedPunches[i].PunchTime;
                var end = orderedPunches[i + 1].PunchTime;
                if (end > start)
                    segments.Add((start, end));
            }

            // Sum gaps between consecutive segments: gap = next.In - prev.Out (never negative)
            TimeSpan totalGaps = TimeSpan.Zero;
            for (int i = 0; i + 1 < segments.Count; i++)
            {
                var gap = segments[i + 1].In - segments[i].Out;
                if (gap > TimeSpan.Zero)
                    totalGaps += gap;
            }

            // Paid break is a single allowance per day (not per gap)
            var allowance = TimeSpan.FromMinutes(Math.Max(0, paidBreakMinutes));
            var penalty = totalGaps - allowance;
            if (penalty < TimeSpan.Zero) penalty = TimeSpan.Zero;

            return (totalGaps, penalty);
        }
    }
}
