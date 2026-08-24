using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("resignation_requests")]
    public class ResignationRequest
    {
        [Key]
        [Column("request_id")]
        public int RequestId { get; set; }

        [Required]
        [Column("employee_id")]
        public int EmployeeId { get; set; }

        [Required]
        [Column("submission_date")]
        public DateTime SubmissionDate { get; set; } = DateTime.Now;

        [Required]
        [Column("desired_last_working_day")]
        public DateOnly DesiredLastWorkingDay { get; set; }

        [Column("reason")]
        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;

        [Column("status")]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Cancelled

        // --- ADMIN ACTION ---
        [Column("approved_last_working_day")]
        public DateOnly? ApprovedLastWorkingDay { get; set; }

        [Column("admin_remarks")]
        public string? AdminRemarks { get; set; }

        [Column("is_settled")]
        public bool IsSettled { get; set; } = false; // True when FnF is paid
    }
}