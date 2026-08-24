using System;
using System.Collections.Generic;

namespace Payroll.Shared.Services
{
    public class DailyAttendanceRecord
    {
        
        // ===== BASIC INFO =====
        public int EmployeeID { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string BiometricID { get; set; } = string.Empty;

        public DateOnly Date { get; set; }
        public string Status { get; set; } = "Absent";

        // ===== SHIFT / SCHEDULE =====
        public TimeOnly? ShiftStartTime { get; set; }
        public TimeOnly? ShiftEndTime { get; set; }

        public TimeSpan ScheduledShiftDuration { get; set; }
        public TimeSpan StandardBreakDuration { get; set; } = TimeSpan.Zero;
        public int StandardBreakMinutes
        {
            get => (int)StandardBreakDuration.TotalMinutes;
            set => StandardBreakDuration = TimeSpan.FromMinutes(value);
        }

        // ===== BREAKS & PENALTIES =====
        public TimeSpan TotalBreakTime { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalBreakPenalty { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalLateness { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalEarlyLeave { get; set; } = TimeSpan.Zero;

        public TimeSpan LatenessDuration { get => TotalLateness; set => TotalLateness = value; }
        public TimeSpan EarlyLeavePenalty { get => TotalEarlyLeave; set => TotalEarlyLeave = value; }
        public TimeSpan BreakPenalty { get => TotalBreakPenalty; set => TotalBreakPenalty = value; }

        public TimeSpan TotalPenalty { get => TotalLateness + TotalBreakPenalty + TotalEarlyLeave; set => TotalBreakPenalty = value; }

        // ===== WORK / OVERTIME =====
        public TimeSpan EarnedStandardDuration { get; set; } = TimeSpan.Zero;
        public TimeSpan EarnedStandardDurationPreMidnight { get; set; } = TimeSpan.Zero;
        public TimeSpan EarnedStandardDurationPostMidnight { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalOvertimeDuration { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalOvertimeDurationPreMidnight { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalOvertimeDurationPostMidnight { get; set; } = TimeSpan.Zero;

        public TimeSpan OvertimeDuration { get => TotalOvertimeDuration; set => TotalOvertimeDuration = value; }
        public TimeSpan WorkedHours { get; set; } = TimeSpan.Zero;

        // Numeric/UI friendly conversion
        public decimal FinalWorkedHours
        {
            get => (decimal)WorkedHours.TotalHours;
            set => WorkedHours = TimeSpan.FromHours((double)value);
        }

        // Numeric / UI-friendly conversions
        public decimal TotalWorkedHoursDecimal
        {
            get => (decimal)WorkedHours.TotalHours;
            set => WorkedHours = TimeSpan.FromHours((double)value);
        }

        public double TotalWorkedHoursDouble
        {
            get => WorkedHours.TotalHours;
            set => WorkedHours = TimeSpan.FromHours(value);
        }

        // ===== LUNCH / GAPS =====
        public TimeOnly? LunchOut { get; set; }
        public TimeOnly? LunchIn { get; set; }
        public int GapsCount { get; set; }

        // ===== PUNCH / DEBUG =====
        public List<TimeOnly> PunchTimes { get; set; } = new();
        public TimeOnly? FirstIn { get; set; }
        public TimeOnly? FinalOut { get; set; }

        public DateTime? Debug_ShiftStart_Full { get; set; }
        public DateTime? Debug_ShiftEnd_Full { get; set; }
        public DateTime? Debug_FirstPunchInWindow { get; set; }
        public DateTime? Debug_LastPunchInWindow { get; set; }
        public decimal ShiftAllowanceEarned { get; set; }
        
           
    }
}
