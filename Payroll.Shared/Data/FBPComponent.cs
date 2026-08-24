using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("fbp_components")]
    public class FBPComponent
    {
        [Key]
        [Column("component_id")]
        public int ComponentId { get; set; }

        [Required]
        [StringLength(50)]
        [Column("name")] // Display Name, e.g., "Fuel Allowance"
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column("max_annual_limit")]
        public decimal MaxAnnualLimit { get; set; } // Compliance limit (e.g., 9600 for fuel, 150000 for 80C)

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("is_tax_exempt")]
        public bool IsTaxExempt { get; set; } = true; // Does this component reduce taxable income?
    }
}