using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data;

[Table("user_theme_preferences", Schema = "public")]
public sealed class UserThemePreference
{
    [Key]
    [Column("user_id")]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [Column("theme")]
    [StringLength(20)]
    public string Theme { get; set; } = "light";

    [Column("updated_at_utc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
