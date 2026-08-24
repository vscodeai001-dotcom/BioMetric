using System;
using Payroll.Shared.Data;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// Determines shift bounds, net scheduled (subtract paid break),
    /// and provides grace windows.
    /// </summary>
    public class AttendanceScheduleService
    {
        private readonly AttendanceBoundsService _bounds;

        public AttendanceScheduleService(AttendanceBoundsService bounds)
        {
            _bounds = bounds;
        }

        public ScheduleResult CalculateSchedule(
            Employee emp,
            DateTime day,
            ShiftSchedule? schedule,
            CompanySetting settings,
            int paidBreakMinutes,
            int startGraceMinutes,
            int endGraceMinutes)
        {

            // --- NEW: Pattern Lookup ---
            if (schedule == null)
            {
                // Try to find a recurring pattern shift that matches the current day of the week
                // This requires querying the database for recurring shifts, which is outside the current scope (since 'schedule' is passed)
                // However, we will assume that the calling function (AttendanceCalculator) will pass the correct 'schedule' if found in pattern.

                // For now, let's rely on the Employee's default shift if 'schedule' is null.
            }
            // --- END NEW: Pattern Lookup ---

            var (shiftStart, shiftEnd) = _bounds.DetermineShiftBounds(
                emp, day, schedule, settings.WorkDayCutoffHour);

            bool hasShift = shiftStart != DateTime.MinValue;
            if (!hasShift)
            {
                return new ScheduleResult
                {
                    HasShift = false,
                    ShiftStart = DateTime.MinValue,
                    ShiftEnd = DateTime.MinValue,
                    GrossScheduled = TimeSpan.Zero,
                    NetScheduled = TimeSpan.Zero,
                    StartGraceEnd = null,
                    EndGraceStart = null
                };
            }

            var gross = shiftEnd - shiftStart;
            var paidBreak = TimeSpan.FromMinutes(Math.Max(paidBreakMinutes, 0));
            var net = gross - paidBreak;
            if (net < TimeSpan.Zero) net = TimeSpan.Zero;

            return new ScheduleResult
            {
                HasShift = true,
                ShiftStart = shiftStart,
                ShiftEnd = shiftEnd,
                GrossScheduled = gross,
                NetScheduled = net,
                StartGraceEnd = shiftStart.AddMinutes(Math.Max(startGraceMinutes, 0)),
                EndGraceStart = shiftEnd.AddMinutes(-Math.Max(endGraceMinutes, 0))
            };
        }
    }

    public sealed class ScheduleResult
    {
        public bool HasShift { get; set; }
        public DateTime ShiftStart { get; set; }
        public DateTime ShiftEnd { get; set; }
        public TimeSpan GrossScheduled { get; set; }
        public TimeSpan NetScheduled { get; set; }

        // Grace: late within start grace not penalized; early within end grace not penalized
        public DateTime? StartGraceEnd { get; set; }
        public DateTime? EndGraceStart { get; set; }
    }
}
