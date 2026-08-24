using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("professional_tax_slabs")]
    public class ProfessionalTaxSlab
    {
        [Key]
        [Column("slab_id")]
        public int SlabID { get; set; }

        [Required]
        [Column("min_salary")]
        public decimal MinSalary { get; set; }

        [Required]
        [Column("max_salary")]
        public decimal MaxSalary { get; set; }

        [Required]
        [Column("tax_amount")]
        public decimal TaxAmount { get; set; }
    }
}