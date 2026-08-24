using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared.Models;
using Payroll.Shared.Services;


namespace Payroll.Shared.Services
{
    /// <summary>
    /// Builds the DailyAttendanceResult used by UI and DailySummary persistence.
    /// </summary>
    public class DailySummaryBuilder
    {
        public DailyAttendanceRecord BuildSummary(
            DateTime day,
            string status,
            ScheduleResult schedule,
            List<TimeOnly> punchTimes,
            TimeSpan totalBreakTime,
            TimeSpan lateness,
            TimeSpan earlyLeave,
            TimeSpan breakPenalty,
            TimeSpan earnedStandardNet,
            TimeSpan overtime)
        {

            var result = new DailyAttendanceRecord
            {
                Date = DateOnly.FromDateTime(day),
                Status = status,

                ShiftStartTime = schedule.HasShift ? TimeOnly.FromDateTime(schedule.ShiftStart) : null,
                ShiftEndTime = schedule.HasShift ? TimeOnly.FromDateTime(schedule.ShiftEnd) : null,

                // Store NET scheduled (shift - paid break)
                ScheduledShiftDuration = schedule.NetScheduled,
                // Paid break minutes (gross - net)
                StandardBreakDuration = schedule.GrossScheduled - schedule.NetScheduled,

                TotalBreakTime = totalBreakTime,
                TotalLateness = lateness,
                TotalEarlyLeave = earlyLeave,
                TotalBreakPenalty = breakPenalty,

                // Worked = Earned (net) + OT  (penalties DO NOT reduce worked hours)
                EarnedStandardDuration = earnedStandardNet,
                TotalOvertimeDuration = overtime,
                WorkedHours = earnedStandardNet + overtime,
                ShiftAllowanceEarned = 0,
                PunchTimes = punchTimes
            };

            return result;
        }
    }
}
