using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Payroll.Shared
{
    [Table("payrollhistory")]
    public class PayrollHistory
    {
        [Key]
        [Column("payrollid")]
        public int PayrollID { get; set; }

        [Column("employeeid")]
        public int EmployeeID { get; set; }

        [Column("paymonth")]
        public int PayMonth { get; set; }

        [Column("payyear")]
        public int PayYear { get; set; }

        // Inside public class PayrollDisplayRow
        public TimeSpan? TotalLateness { get; set; }
        public TimeSpan? TotalBreakPenalty { get; set; }
        public TimeSpan? ScheduledShiftDuration { get; set; }

        [Column("basesalary")]
        public decimal? BaseSalary { get; set; }

        [Column("totalhoursworked")] // Represents Standard Payable Hours (Scheduled - Penalties) + Paid Leave Hours
        public decimal? TotalHoursWorked { get; set; } // Renamed meaning

        [Column("overtimepay")]
        public decimal? OvertimePay { get; set; } // Nullable

        [Column("deductions_hours")]
        public decimal? Deductions_Hours { get; set; } // Nullable

        [Column("deductions_advance")]
        public decimal? Deductions_Advance { get; set; } // Nullable

        // --- NEW: Bonus Column Added ---
        [Column("Bonus")]
        public decimal? Bonus { get; set; } // Nullable

        [Column("netsalary")]
        public decimal NetSalary { get; set; }

        // --- Fields for Enhanced Table ---
        [Column("manualleavedays")]
        public int ManualLeaveDays { get; set; } = 0;

        [Column("absentdays")]
        public int AbsentDays { get; set; } = 0;

        [Column("totalpenaltyduration")]
        public TimeSpan TotalPenaltyDuration { get; set; } = TimeSpan.Zero; // Lateness + Break Penalty

        [Column("totalovertimeduration")]
        public TimeSpan TotalOvertimeDuration { get; set; } = TimeSpan.Zero;

        [Column("HourlyRate")] // This was missing its column attribute in your file
        public decimal HourlyRate { get; set; }
        // --- End New Fields ---

        [Column("basic_salary_component")]
        public decimal BasicComponent { get; set; } // The calculated Basic amount

        [Column("pf_deduction")]
        public decimal PfDeduction { get; set; }

        [Column("esi_deduction")]
        public decimal EsiDeduction { get; set; }

        [Column("employer_pf_contribution")]
        public decimal EmployerPfContribution { get; set; }

        [Column("employer_esi_contribution")]
        public decimal EmployerEsiContribution { get; set; }
        [Column("pt_deduction")]
        public decimal PtDeduction { get; set; }

        [Column("tds_deduction")] 
        public decimal TdsDeduction { get; set; } = 0;

        [Column("TotalShiftAllowance")]
        public decimal TotalShiftAllowance { get; set; } 

        
    }
}