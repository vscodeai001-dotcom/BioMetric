using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Payroll.Shared.Models; // For using DailyAttendanceRecord properties

namespace Payroll.Shared.Data
{
    // This table stores the final, calculated attendance result for one day.
    // This prevents historical data from changing when shifts or grace periods are modified.
    [Table("daily_summaries")]
    public class DailySummary
    {
        [Key]
        [Column("summaryid")]
        public int SummaryID { get; set; }

        [Required]
        [Column("employeeid")]
        public int EmployeeID { get; set; }

        [Required]
        [Column("shiftdate")]
        public DateOnly ShiftDate { get; set; }

        // --- Core Status & Duration ---

        [Required]
        [StringLength(50)]
        [Column("status")] // e.g., Present, Absent, Holiday (Diwali)
        public string Status { get; set; } = "Absent";

        [Required]
        [Column("earned_standard_hours")]
        public decimal EarnedStandardHours { get; set; } // Total standard hours earned

        [Required]
        [Column("total_overtime_duration")]
        public TimeSpan TotalOvertimeDuration { get; set; }

        // --- Penalties (Stored as Duration) ---

        [Required]
        [Column("total_penalty_duration")]
        public TimeSpan TotalPenaltyDuration { get; set; }

        [Required]
        [Column("total_lateness")]
        public TimeSpan TotalLateness { get; set; }

        [Required]
        [Column("total_break_penalty")]
        public TimeSpan TotalBreakPenalty { get; set; }

        // --- Metadata for debugging/audit ---

        [Required]
        [Column("scheduled_shift_duration")]
        public TimeSpan ScheduledShiftDuration { get; set; }

        // This index ensures one entry per employee per day
        [NotMapped]
        public string UniqueKey => $"{EmployeeID}_{ShiftDate}";

        [Column("shift_allowance_earned")]
        public decimal ShiftAllowanceEarned { get; set; }

        [Required]
        [Column("is_manual_override")] // <-- NEW FIELD
        public bool IsManualOverride { get; set; } = false;
    }
}