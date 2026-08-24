using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared;
using Payroll.Shared.Data;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// Determines the type of day (Holiday / CompOff / Working / Absent / Half-Day).
    /// Also enforces hire/termination windows.
    /// </summary>
    public class AttendanceDayTypeService
    {
        public DayTypeResult DetectDayType(
            Employee emp,
            DateTime day,
            List<AttendanceLog> orderedPunches,
            LeaveRequest? leaveRecord,
            List<CompanyHoliday> holidays)
        {
            var dateOnly = DateOnly.FromDateTime(day);
            bool insideTenure = IsInsideTenure(emp, day);
            if (!insideTenure)
                return new DayTypeResult { Type = DayType.NonEmployment };

            bool isHoliday = holidays.Any(h => h.HolidayDate == dateOnly);
            bool isCompOffDay = emp.CompOffDayOfWeek.HasValue && day.DayOfWeek == emp.CompOffDayOfWeek.Value;
            bool hasPunches = orderedPunches != null && orderedPunches.Count >= 2;

            // Holiday logic
            if (isHoliday)
            {
                return hasPunches
                    ? new DayTypeResult { Type = DayType.HolidayWithWork }
                    : new DayTypeResult { Type = DayType.Holiday };
            }

            // Comp-off logic
            if (isCompOffDay)
            {
                return hasPunches
                    ? new DayTypeResult { Type = DayType.CompOffWithWork }
                    : new DayTypeResult { Type = DayType.CompOffNoWork };
            }

            // Half-day via APPROVED leave only.
            // Pending requests must not affect attendance.
            if (leaveRecord != null &&
                leaveRecord.IsApproved &&
                leaveRecord.IsHalfDay)
            {
                return new DayTypeResult
                {
                    Type = DayType.HalfDayApproved
                };
            }

            // Auto half-day detection: exactly 2 punches and worked <= 50% handled later
            if (!hasPunches)
                return new DayTypeResult { Type = DayType.WorkingAbsent };

            return new DayTypeResult { Type = DayType.WorkingPresent };
        }

        private bool IsInsideTenure(Employee emp, DateTime day)
        {
            if (emp.HireDate.HasValue && day < emp.HireDate.Value.ToDateTime(TimeOnly.MinValue)) return false;
            if (emp.TerminationDate.HasValue && day > emp.TerminationDate.Value.ToDateTime(TimeOnly.MaxValue)) return false;
            return true;
        }
    }

    public enum DayType
    {
        NonEmployment,
        Holiday,
        HolidayWithWork,
        CompOffNoWork,
        CompOffWithWork,
        WorkingAbsent,
        WorkingPresent,
        HalfDayApproved,
        HalfDayAuto
    }

    public sealed class DayTypeResult
    {
        public DayType Type { get; set; }
    }
}
