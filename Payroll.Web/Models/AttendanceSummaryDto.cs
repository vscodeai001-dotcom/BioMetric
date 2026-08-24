namespace Payroll.Web.Models
{
    public class AttendanceSummaryDto
    {
        public int WorkingDays { get; set; }
        public int PresentDays { get; set; }
        public int LeaveDays { get; set; }
        public int AbsentDays { get; set; }
        public decimal TotalHoursCalculated { get; set; }
    }
}