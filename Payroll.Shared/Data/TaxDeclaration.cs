using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("tax_declarations")]
    public class TaxDeclaration
    {
        [Key]
        [Column("declaration_id")]
        public int DeclarationId { get; set; }

        [Required]
        [Column("employee_id")]
        public int EmployeeId { get; set; }

        [Required]
        [Column("financial_year")]
        public int FinancialYear { get; set; } // e.g., 2025 means FY 2025-2026

        [Required]
        [StringLength(10)]
        [Column("regime")]
        public string Regime { get; set; } = "New"; // "Old" or "New"

        // --- Exemptions (Old Regime Only) ---

        [Column("section_80c")]
        public decimal Section80C { get; set; } = 0; // PF, PPF, LIC, etc. (Max 1.5L)

        [Column("section_80d")]
        public decimal Section80D { get; set; } = 0; // Medical Insurance

        [Column("hra_rent_paid")]
        public decimal HraRentPaid { get; set; } = 0; // Annual Rent Paid

        [Column("other_exemptions")]
        public decimal OtherExemptions { get; set; } = 0;

        // --- Status ---

        [Required]
        [StringLength(20)]
        [Column("status")]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

        [Column("admin_remarks")]
        public string? AdminRemarks { get; set; }

        [Column("submission_date")]
        public DateTime SubmissionDate { get; set; } = DateTime.Now;

        [Column("approval_date")]
        public DateTime? ApprovalDate { get; set; }

        // Ensure unique declaration per year per employee
        [NotMapped]
        public string UniqueKey => $"{EmployeeId}_{FinancialYear}";
    }
}