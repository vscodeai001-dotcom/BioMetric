using System;

namespace Payroll.Web.Models
{
    public class AttendanceLogDto
    {
        public int EmployeeID { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public string Status { get; set; } = "Absent";

        // --- Shift Details (New) ---
        public TimeOnly? ShiftStart { get; set; }
        public TimeOnly? ShiftEnd { get; set; }
        public TimeSpan ScheduledShiftDuration { get; set; }

        // --- Worked & OT ---
        public decimal FinalWorkedHours { get; set; }
        public TimeSpan OvertimeDuration { get; set; }

        // --- Penalties ---
        public TimeSpan TotalPenalty { get; set; }
        public TimeSpan LatenessDuration { get; set; }
        public TimeSpan BreakPenalty { get; set; }
        public TimeSpan EarlyLeavePenalty { get; set; }

        // --- Break Details ---
        public TimeSpan TotalBreakTime { get; set; }
        public int GapsCount { get; set; }
        public TimeOnly? LunchIn { get; set; }
        public TimeOnly? LunchOut { get; set; }

        public string Punches { get; set; } = string.Empty;
    }
}