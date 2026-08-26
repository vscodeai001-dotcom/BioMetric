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
    /// PunchTime remains a business-local wall-clock value.
    /// No UTC conversion is performed.
    ///
    /// Attendance calculations are MINUTE BASED.
    /// Seconds and fractional seconds are ignored.
    ///
    /// Example:
    /// 11:30:19 -> 11:30
    /// 11:33:52 -> 11:33
    ///
    /// Attendance rule:
    ///   1st punch = IN
    ///   2nd punch = OUT
    ///   3rd punch = IN
    ///   4th punch = OUT
    ///
    /// Odd punches are preserved.
    /// The calculation engine decides how today's open punch
    /// is handled.
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
            // Normalize attendance timestamps to MINUTE precision.
            //
            // Database values remain untouched.
            // Only the in-memory calculation copy is normalized.
            // --------------------------------------------------------

            var ordered = punches
                .Where(p => p != null)
                .Select(p =>
                {
                    var value = p.PunchTime;

                    p.PunchTime = new DateTime(
                        value.Year,
                        value.Month,
                        value.Day,
                        value.Hour,
                        value.Minute,
                        0,
                        DateTimeKind.Unspecified);

                    return p;
                })
                .OrderBy(p => p.PunchTime)
                .ThenBy(p => p.LogID)
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
            // LAST CONFIRMED OUT
            //
            // Only an even number of punches has a confirmed OUT.
            //
            // Odd punch:
            // final punch remains an open IN.
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