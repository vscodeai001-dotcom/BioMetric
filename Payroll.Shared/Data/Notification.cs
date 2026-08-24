using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("notifications")]
    public class Notification
    {
        [Key]
        [Column("notification_id")]
        public int NotificationId { get; set; }

        [Required]
        [Column("user_id")]
        public string UserId { get; set; } = string.Empty; // Links to Identity User

        [Required]
        [StringLength(100)]
        [Column("title")]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        [Column("message")]
        public string Message { get; set; } = string.Empty;

        [StringLength(255)]
        [Column("url")]
        public string? Url { get; set; } // Optional link to action (e.g., "Click to Approve")

        [Column("is_read")]
        public bool IsRead { get; set; } = false;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}