using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// Normalizes and orders attendance punches.
    ///
    /// BUSINESS REGION:
    /// India
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
    /// Odd punches are preserved.
    ///
    /// The calculation engine decides whether the final open punch
    /// should be calculated against the current business time.
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
            // Attendance timestamps are business-local wall-clock
            // values. Never convert them to UTC here.
            // --------------------------------------------------------

            var ordered = punches
                .Where(p => p != null)
                .Select(p =>
                {
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
            // Only an EVEN number of punches has a confirmed OUT.
            //
            // For an odd number of punches, the last punch remains
            // an open IN. The calculation engine handles that open
            // segment separately.
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