using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared.Data
{
    [Table("fnf_settlements")]
    public class FnFSettlement
    {
        [Key]
        [Column("settlement_id")]
        public int SettlementId { get; set; }

        [Required]
        [Column("employee_id")]
        public int EmployeeId { get; set; }

        [Required]
        [Column("resignation_request_id")]
        public int ResignationRequestId { get; set; }

        [Column("settlement_date")]
        public DateTime SettlementDate { get; set; } = DateTime.Now;

        // --- EARNINGS ---
        [Column("unpaid_salary")]
        public decimal UnpaidSalary { get; set; } // Salary for days worked in last month

        [Column("leave_encashment_amount")]
        public decimal LeaveEncashment { get; set; }

        [Column("gratuity_amount")]
        public decimal Gratuity { get; set; }

        [Column("bonus_payable")]
        public decimal BonusPayable { get; set; }

        // --- DEDUCTIONS ---
        [Column("notice_period_recovery")]
        public decimal NoticePeriodRecovery { get; set; } // If they leave early

        [Column("asset_recovery_cost")]
        public decimal AssetRecoveryCost { get; set; } // Damaged laptop, etc.

        [Column("outstanding_advances")]
        public decimal OutstandingAdvances { get; set; }

        // --- NET ---
        [Column("net_payable")]
        public decimal NetPayable { get; set; }

        [Column("is_finalized")]
        public bool IsFinalized { get; set; } = false;

        [Column("payment_reference")]
        public string? PaymentReference { get; set; } // Check No / UTR
    }
}