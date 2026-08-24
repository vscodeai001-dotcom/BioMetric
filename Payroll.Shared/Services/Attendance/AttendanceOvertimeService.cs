using System;
using Payroll.Shared.Data;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// Computes OT before start and after end. 
    /// Start/end grace does NOT create OT. 
    /// On Holiday/CompOff with punches: all worked time is OT.
    /// </summary>
    public class AttendanceOvertimeService
    {
        public (TimeSpan before, TimeSpan after, TimeSpan total) CalculateOvertime(
            DateTime? firstIn,
            DateTime? lastOut,
            ScheduleResult schedule,
            bool allAsOT)
        {
            if (!firstIn.HasValue || !lastOut.HasValue)
                return (TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

            if (!schedule.HasShift || allAsOT)
            {
                var all = lastOut.Value - firstIn.Value;
                if (all < TimeSpan.Zero) all = TimeSpan.Zero;
                return (TimeSpan.Zero, all, all);
            }

            TimeSpan before = TimeSpan.Zero;
            TimeSpan after = TimeSpan.Zero;

            // Before start (arrival earlier than the official shift start, not grace)
            if (firstIn.Value < schedule.ShiftStart)
                before = schedule.ShiftStart - firstIn.Value;

            // After end (staying later than official shift end, ignore end grace for OT)
            if (lastOut.Value > schedule.ShiftEnd)
                after = lastOut.Value - schedule.ShiftEnd;

            var total = before + after;
            if (total < TimeSpan.Zero) total = TimeSpan.Zero;

            return (before < TimeSpan.Zero ? TimeSpan.Zero : before,
                    after < TimeSpan.Zero ? TimeSpan.Zero : after,
                    total);
        }
    }
}
