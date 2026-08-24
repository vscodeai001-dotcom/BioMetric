using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Web.Models
{
    public class PayrollDisplayRow
    {
        public int EmployeeID { get; set; }
        public string EmployeeName { get; set; } = "";
        public decimal? BaseSalary { get; set; }
        public int LeaveDays { get; set; }
        public decimal EarnedStandardHours { get; set; }

        // Inside public class PayrollHistory
        public TimeSpan TotalLateness { get; set; }
        public TimeSpan TotalBreakPenalty { get; set; }
        public TimeSpan ScheduledShiftDuration { get; set; }
        public decimal EarnedPay { get; set; }
        public TimeSpan TotalOvertimeDuration { get; set; }
        public TimeSpan PenaltyDuration { get; set; }
        public decimal PenaltyDeduction { get; set; }
        public int AbsentDays { get; set; }
        public TimeSpan OvertimeDuration { get; set; }
        public decimal OvertimePay { get; set; }
        public decimal AdvanceDeduction { get; set; }
        public decimal Bonus { get; set; }
        public decimal NetPayable { get; set; }
        public decimal HourlyRate { get; set; }

        // Statutory Fields
        public decimal BasicSalary { get; set; }
        public decimal PfDeduction { get; set; }
        public decimal EsiDeduction { get; set; }
        public decimal PtDeduction { get; set; } // PT

        // Employer Shares
        public decimal EmployerPfContribution { get; set; }
        public decimal EmployerEsiContribution { get; set; }

        // Flags for UI display
        public bool IsPfEnabled { get; set; }
        public bool IsEsiEnabled { get; set; }

        public decimal TotalShiftAllowance { get; set; }

        [Column("tds_deduction")] // <-- NEW FIELD
        public decimal TdsDeduction { get; set; } = 0;
        public TimeSpan TotalPenaltyDuration { get; internal set; }
    }
}