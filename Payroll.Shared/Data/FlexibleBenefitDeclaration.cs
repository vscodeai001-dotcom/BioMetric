using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("flexible_benefit_declarations")]
    public class FlexibleBenefitDeclaration
    {
        [Key]
        [Column("declaration_id")]
        public int DeclarationId { get; set; }

        [Required]
        [Column("employee_id")]
        public int EmployeeId { get; set; }

        [Required]
        [Column("financial_year")]
        public int FinancialYear { get; set; }

        [Required]
        [StringLength(100)]
        [Column("component_name")] // e.g., HRA, Fuel Allowance, LTA
        public string ComponentName { get; set; } = string.Empty;

        [Required]
        [Column("annual_allocated_amount")]
        public decimal AnnualAllocatedAmount { get; set; }

        [Required]
        [Column("monthly_allocated_amount")] // Annual amount divided by months remaining
        public decimal MonthlyAllocatedAmount { get; set; }

        [Required]
        [Column("status")]
        [StringLength(20)]
        public string Status { get; set; } = "Draft"; // Draft, Submitted, Approved, Locked

        [Column("submission_date")]
        public DateTime SubmissionDate { get; set; } = DateTime.Now;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("admin_remarks")]
        public string? AdminRemarks { get; set; }

        // Unique constraint: one allocation per component per employee per year
        [NotMapped]
        public string UniqueKey => $"{EmployeeId}_{FinancialYear}_{ComponentName}";
    }
}