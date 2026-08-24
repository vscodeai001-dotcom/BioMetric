using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared.Data;

namespace Payroll.Shared.Services
{
    public class AttendanceBoundsService
    {
        // 1. Determine Shift Bounds
        public (DateTime shiftStart, DateTime shiftEnd) DetermineShiftBounds(
            Employee emp, DateTime day, ShiftSchedule? schedule, int cutoffHour)
        {
            TimeOnly? effectiveStartTime = schedule?.StartTime ?? emp.ShiftStartTime;
            TimeOnly? effectiveEndTime = schedule?.EndTime ?? emp.ShiftEndTime;
            bool hasShift = effectiveStartTime.HasValue && effectiveEndTime.HasValue;

            if (!hasShift) return (DateTime.MinValue, DateTime.MinValue);

            DateTime shiftStart = day.Date.Add(effectiveStartTime!.Value.ToTimeSpan());
            DateTime shiftEnd = day.Date.Add(effectiveEndTime!.Value.ToTimeSpan());
            DateTime todayCutoffTime = day.Date.AddHours(cutoffHour);

            if (shiftEnd <= shiftStart)
            {
                shiftEnd = shiftEnd.AddDays(1);
            }
            if (shiftEnd > todayCutoffTime)
            {
                shiftEnd = todayCutoffTime;
            }
            return (shiftStart, shiftEnd);
        }

        // 2. Get Punch Window (FIXED: Removed fallback logic)
        public (DateTime? firstInWindow, DateTime? lastOutWindow) GetPunchWindow(
            List<AttendanceLog> punchesForDay, DateTime shiftStart, DateTime shiftEnd)
        {
            DateTime? firstPunchInWindow = null;
            DateTime? lastPunchInWindow = null;

            if (shiftStart == DateTime.MinValue)
            {
                if (!punchesForDay.Any()) return (null, null);
                return (punchesForDay.First().PunchTime, punchesForDay.Last().PunchTime);
            }

            foreach (var punch in punchesForDay)
            {
                // Only consider punches *inside* the shift boundaries
                if (punch.PunchTime >= shiftStart && punch.PunchTime <= shiftEnd)
                {
                    if (firstPunchInWindow == null || punch.PunchTime < firstPunchInWindow) firstPunchInWindow = punch.PunchTime;
                    if (lastPunchInWindow == null || punch.PunchTime > lastPunchInWindow) lastPunchInWindow = punch.PunchTime;
                }
            }

            // CRITICAL FIX: DO NOT FALL BACK to punches outside the window.
            // If no punches are found inside the shift, return (null, null).
            // This prevents false lateness/early-leave penalties.

            return (firstPunchInWindow, lastPunchInWindow);
        }
    }
}