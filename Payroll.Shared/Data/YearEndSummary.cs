using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    // This table stores the annual cumulative totals required for compliance reporting (e.g., Form-16).
    [Table("year_end_summaries")]
    public class YearEndSummary
    {
        [Key]
        [Column("summary_id")]
        public int SummaryID { get; set; }

        [Required]
        [Column("employeeid")]
        public int EmployeeID { get; set; }

        [Required]
        [Column("tax_year")] // The year this summary covers (e.g., 2025)
        public int TaxYear { get; set; }

        // --- Annual Earnings ---
        [Required]
        [Column("gross_taxable_salary")] // Sum of monthly net salaries + statutory earnings
        public decimal GrossTaxableSalary { get; set; } = 0;

        [Required]
        [Column("total_tds_deducted")] // Sum of monthly TDS deductions
        public decimal TotalTdsDeducted { get; set; } = 0;

        [Required]
        [Column("total_pf_contribution_employee")]
        public decimal TotalPfContributionEmployee { get; set; } = 0;

        // --- Other Annual Metrics ---
        [Column("total_annual_absent_days")]
        public int TotalAnnualAbsentDays { get; set; } = 0;

        [Column("total_annual_ot_pay")]
        public decimal TotalAnnualOtPay { get; set; } = 0;

        // This ensures one entry per employee per tax year
        [NotMapped]
        public string UniqueKey => $"{EmployeeID}_{TaxYear}";
    }
}