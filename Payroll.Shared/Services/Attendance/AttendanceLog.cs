using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared
{
    [Table("attendancelogs")]
    public class AttendanceLog
    {
        [Key]
        [Column("logid")]
        public int LogID { get; set; }

        [Column("employeeid")]
        public int? EmployeeID { get; set; } // Optional in case biometric ID doesn't match

        [Column("biometricid")]
        public string BiometricID { get; set; } = string.Empty;

        [Column("punchtime")]
        public DateTime PunchTime { get; set; }

        // --- ADD THESE TWO PROPERTIES ---
        [StringLength(50)] // Optional: Set a max length
        public string? DeviceID { get; set; } // e.g., "ZKTeco1", "ManualCorrection"

        [StringLength(20)] // Optional: Set a max length
        public string? LogType { get; set; } // e.g., "Punch", "System", "Correction"
        // --- END ADD ---

        [Column("is_approved")] // <-- NEW FIELD
        public bool IsApproved { get; set; } = true;

        // --- NEW: GEO-LOCATION DATA ---
        [Column("latitude")]
        public double? Latitude { get; set; }

        [Column("longitude")]
        public double? Longitude { get; set; }
        // ------------------------------

    }
}