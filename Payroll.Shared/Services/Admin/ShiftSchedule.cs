using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Payroll.Shared.Services;

namespace Payroll.Shared
{
    [Table("shiftschedules")]
    public class ShiftSchedule
    {
        [Key]
        [Column("scheduleid")]
        public int ScheduleID { get; set; }

        [Column("employeeid")]
        public int EmployeeID { get; set; }

        [Column("shiftdate")]
        public DateOnly ShiftDate { get; set; } // Use DateOnly for the date part

        [Column("starttime")]
        public TimeOnly StartTime { get; set; } // Use TimeOnly for the time part

        [Column("endtime")]
        public TimeOnly EndTime { get; set; }   // Use TimeOnly for the time part

        // --- NEW: Dynamic Rostering Fields ---
        [Column("is_recurring_pattern")]
        public bool IsRecurringPattern { get; set; } = false; // Flag if this record defines a weekly/monthly pattern

        [Column("pattern_duration_days")]
        public int PatternDurationDays { get; set; } = 7; // e.g., 7 days (weekly) or 14 days (bi-weekly)

        [Column("applies_to_day_of_week")]
        public DayOfWeek AppliesToDayOfWeek { get; set; } // If recurring, which day of the week this shift applies to
     
    }
}