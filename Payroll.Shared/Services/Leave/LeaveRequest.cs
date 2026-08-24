using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared
{
    [Table("leaverequests")]
    public class LeaveRequest
    {
        [Key]
        [Column("leaverequestid")]
        public int LeaveRequestID { get; set; }

        [Column("employeeid")]
        public int EmployeeID { get; set; }

        [Column("leavedate")]
        public DateTime? LeaveDate { get; set; }

        [Column("leavetype")]
        public string LeaveType { get; set; } = string.Empty; // e.g., 'Sick', 'Vacation'

        // --- NEW FIELD ADDED ---
        [Column("is_half_day")]
        public bool IsHalfDay { get; set; } = false; // True if employee worked part of the day
        // --- END NEW ---

        [Column("isapproved")]
        public bool IsApproved { get; set; } = true;

       

        [Column("notes")]
        public string? Notes { get; set; } // Nullable string

        // --- NEW: Non-Database bound properties for Multi-Day UX ---
        [NotMapped]
        public DateTime? EndDate { get; set; } // Used for capturing date range in UI
        [NotMapped]
        public bool IsMultiDay => LeaveDate.HasValue && EndDate.HasValue && EndDate.Value.Date > LeaveDate.Value.Date;
        // --- END NEW ---

    }
}