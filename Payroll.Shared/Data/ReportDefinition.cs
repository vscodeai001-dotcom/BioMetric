using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("report_definitions")]
    public class ReportDefinition
    {
        [Key]
        [Column("report_id")]
        public int ReportId { get; set; }

        [Required]
        [StringLength(100)]
        [Column("report_name")]
        public string ReportName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Column("report_type")]
        public string ReportType { get; set; } = string.Empty; // Unique key for service logic (e.g., "ATTENDANCE_MONTHLY", "PAYROLL_VARIANCE")

        [Required]
        [StringLength(50)]
        [Column("target_role")]
        public string TargetRole { get; set; } = "Admin"; // Admin, Employee, All

        [Required]
        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [StringLength(255)]
        [Column("description")]
        public string Description { get; set; } = string.Empty;

     
    }
}