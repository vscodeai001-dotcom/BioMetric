using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// Normalizes and orders attendance punches.
    ///
    /// BUSINESS TIMEZONE:
    /// Asia/Kolkata
    ///
    /// IMPORTANT:
    /// PunchTime is treated as the attendance/business wall-clock time.
    /// We do NOT convert existing database PunchTime values to UTC here.
    ///
    /// Attendance rule:
    ///   1st punch = IN
    ///   2nd punch = OUT
    ///   3rd punch = IN
    ///   4th punch = OUT
    ///
    /// An odd number of punches is intentionally preserved.
    /// The calculation engine can therefore correctly identify
    /// a Missing Punch instead of receiving a fabricated OUT punch.
    /// </summary>
    public sealed class AttendancePunchProcessor
    {
        public (
            List<AttendanceLog> Ordered,
            DateTime? FirstIn,
            DateTime? LastOut)
            ProcessPunches(
                List<AttendanceLog> punches,
                DateTime day)
        {
            if (punches == null || punches.Count == 0)
            {
                return (
                    new List<AttendanceLog>(),
                    null,
                    null);
            }

            // --------------------------------------------------------
            // Keep the original punch timestamps.
            // Only normalize DateTime Kind and sort.
            // --------------------------------------------------------

            var ordered = punches
                .Where(p => p != null)
                .Select(p =>
                {
                    // Attendance timestamps are business-local values.
                    p.PunchTime =
                        DateTime.SpecifyKind(
                            p.PunchTime,
                            DateTimeKind.Unspecified);

                    return p;
                })
                .OrderBy(p => p.PunchTime)
                .ToList();

            if (ordered.Count == 0)
            {
                return (
                    new List<AttendanceLog>(),
                    null,
                    null);
            }

            // --------------------------------------------------------
            // FIRST IN
            // --------------------------------------------------------

            DateTime? firstIn =
                ordered[0].PunchTime;

            // --------------------------------------------------------
            // LAST OUT
            //
            // Only an even number of punches has a valid OUT.
            // Do NOT treat the final odd punch as OUT.
            // --------------------------------------------------------

            DateTime? lastOut =
                ordered.Count >= 2 &&
                ordered.Count % 2 == 0
                    ? ordered[^1].PunchTime
                    : null;

            return (
                ordered,
                firstIn,
                lastOut);
        }
    }
}