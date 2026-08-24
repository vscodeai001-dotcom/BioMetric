using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Payroll.Shared.Models
{
    public class CompanyCumulativeSummary
    {
        public string EmployeeName { get; set; } = "All Employees";
        public int TotalEmployeesProcessed { get; set; }
        public TimeSpan TotalScheduledHours { get; set; }
        public decimal TotalWorkedHours { get; set; } // Use decimal for accuracy
        public TimeSpan TotalOverallPenalty { get; set; }
        public TimeSpan TotalOvertime { get; set; }
        public TimeSpan TotalLateness { get; set; }
        public TimeSpan TotalBreakPenalty { get; set; }
        public TimeSpan TotalEarlyLeave { get; set; }
    }
}
