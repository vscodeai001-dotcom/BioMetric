using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("geo_punch_audits")]
    public class GeoPunchAudit
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("employee_id")]
        public int EmployeeId { get; set; }

        [Column("session_id")]
        public Guid? SessionId { get; set; }

        [Column("punch_time_utc")]
        public DateTime PunchTimeUtc { get; set; }

        [Column("latitude")]
        public double Latitude { get; set; }

        [Column("longitude")]
        public double Longitude { get; set; }

        [Column("accuracy_meters")]
        public double AccuracyMeters { get; set; }

        [Column("distance_from_office_meters")]
        public double DistanceFromOfficeMeters { get; set; }

        [Column("allowed_radius_meters")]
        public int AllowedRadiusMeters { get; set; }

        [Column("is_within_allowed_radius")]
        public bool IsWithinAllowedRadius { get; set; }

        [Column("success")]
        public bool Success { get; set; }

        [Column("result_message")]
        public string ResultMessage { get; set; } = string.Empty;

        [Column("source")]
        [MaxLength(40)]
        public string Source { get; set; } = "MOBILE_APP";

        [Column("attendance_log_id")]
        public long? AttendanceLogId { get; set; }
    }
}