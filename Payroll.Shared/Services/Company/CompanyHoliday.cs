using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    // This attribute maps this class to your *existing* "holidays" table
    [Table("holidays", Schema = "public")]
    public class CompanyHoliday
    {
        [Key]
        [Column("holidayid")] // Maps to your "holidayid" column
        public int HolidayID { get; set; }

        [Required]
        [Column("holidaydate")] // Maps to your "holidaydate" column
        public DateOnly HolidayDate { get; set; }

        [Required]
        [StringLength(100)]
        [Column("holidayname")] // Maps to your "holidayname" column
        public string HolidayName { get; set; } = string.Empty;
    }
}