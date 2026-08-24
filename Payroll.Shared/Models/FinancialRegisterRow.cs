using System;
using Payroll.Shared.Data;

namespace Payroll.Shared.Models
{
    /// <summary>
    /// Comprehensive DTO used for the Monthly Financial Register Report (R1).
    /// </summary>
    public class FinancialRegisterRow
    {
        // --- Employee & Identity ---
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string PayrollType { get; set; } = string.Empty; // Salary Type (Monthly/Hourly)
        public string BiometricId { get; set; } = string.Empty;
        public string? Email { get; set; }

        // --- Core Compensation & Statutory (Monthly) ---
        public decimal MonthlySalary { get; set; }
        public decimal BaseSalaryComp { get; set; } // Component for PF calculation
        public decimal HourlyRate { get; set; }
        public decimal GrossPayable { get; set; } // Earned Pay + OT + Bonuses + Allowance

        // --- Worked Hours & Leave Summary ---
        public decimal EarnedHours { get; set; } // Standard hours worked/credited
        public TimeSpan TotalOvertime { get; set; }
        public decimal TotalOvertimePay { get; set; }
        public decimal ShiftAllowance { get; set; }
        public int AbsentDays { get; set; }
        public int LeaveDays { get; set; }

        // --- Deductions & Penalties ---
        public decimal PenaltyDeduction { get; set; } // Lateness/Break Penalty (Money)
        public decimal AdvanceDeduction { get; set; }
        public decimal BonusPaid { get; set; } // Bonus amount added this month

        // --- Statutory Deductions (Employee Share) ---
        public decimal PfDeduction { get; set; }
        public decimal EsiDeduction { get; set; }
        public decimal PtDeduction { get; set; }
        public decimal TdsDeduction { get; set; }

        // --- Final Totals ---
        public decimal TotalDeductions { get; set; }
        public decimal NetPayable { get; set; }

        // --- Status Check ---
        public string PayrollStatus { get; set; } = "Completed"; // Paid, Pending, Draft
    }
}