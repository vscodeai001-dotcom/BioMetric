using Payroll.Shared.Models;
using System.Globalization;
using System.Text;
using Payroll.Shared;
using Payroll.Shared.Data;
using Payroll.Shared.Services;


namespace Payroll.Web.Services
{
    public class CsvExportService
    {
        // --- This method is from the previous step ---
        public string GeneratePayrollHistoryCsv(List<PayrollHistory> history, List<Employee> employees)
        {
            var sb = new StringBuilder();
            var inrCulture = new CultureInfo("en-IN");

            // Header
            sb.AppendLine("EmployeeID,EmployeeName,PayPeriod,BaseSalary,HourlyRate,EarnedHours_Decimal,EarnedPay,OvertimeHours_Decimal,OvertimePay,PenaltyHours_Decimal,PenaltyDeduction,AdvanceDeduction,Bonus,NetSalary");

            // Rows
            foreach (var entry in history)
            {
                var empName = employees.FirstOrDefault(e => e.EmployeeID == entry.EmployeeID)?.Name ?? "Unknown";
                var payPeriod = new DateTime(entry.PayYear, entry.PayMonth, 1).ToString("MMM-yyyy");

                sb.AppendLine(string.Join(",",
                    entry.EmployeeID,
                    FormatCsvField(empName),
                    payPeriod,
                    entry.BaseSalary?.ToString("F2", inrCulture) ?? "0.00",
                    entry.HourlyRate.ToString("F2", inrCulture),
                    (entry.TotalHoursWorked ?? 0).ToString("F2", inrCulture),
                    ((entry.TotalHoursWorked ?? 0) * entry.HourlyRate).ToString("F2", inrCulture),
                    entry.TotalOvertimeDuration.TotalHours.ToString("F2", inrCulture),
                    entry.OvertimePay?.ToString("F2", inrCulture) ?? "0.00",
                    entry.TotalPenaltyDuration.TotalHours.ToString("F2", inrCulture),
                    entry.Deductions_Hours?.ToString("F2", inrCulture) ?? "0.00",
                    entry.Deductions_Advance?.ToString("F2", inrCulture) ?? "0.00",
                    entry.Bonus?.ToString("F2", inrCulture) ?? "0.00",
                    entry.NetSalary.ToString("F2", inrCulture)
                ));
            }
            return sb.ToString();
        }

        // --- ADD THIS NEW METHOD for AttendanceLogViewer ---
        // Note: We must make the class public to access it here
        public string GenerateAttendanceLogCsv(List<DailyAttendanceRecord> records)
        {
            var sb = new StringBuilder();
            var inrCulture = new CultureInfo("en-IN");

            // Header
            sb.AppendLine("EmployeeID,EmployeeName,BiometricID,Date,Status,ShiftStart,ShiftEnd,FirstIn,FinalOut,WorkedHours_Decimal,ScheduledHours_HHMM,Overtime_HHMM,TotalPenalty_HHMM,Lateness_HHMM,BreakPenalty_HHMM,EarlyLeave_HHMM,Punches");

            // Rows
            foreach (var record in records)
            {
                sb.AppendLine(string.Join(",",
                    record.EmployeeID,
                    FormatCsvField(record.EmployeeName),
                    record.BiometricID,
                    record.Date.ToString("yyyy-MM-dd"),
                    FormatCsvField(record.Status),
                    record.ShiftStartTime?.ToString("HH:mm") ?? "",
                    record.ShiftEndTime?.ToString("HH:mm") ?? "",
                    record.FirstIn?.ToString("HH:mm") ?? "",
                    record.FinalOut?.ToString("HH:mm") ?? "",
                    record.FinalWorkedHours.ToString("F2", inrCulture),
                    FormatTimeSpan(record.ScheduledShiftDuration),
                    FormatTimeSpan(record.OvertimeDuration),
                    FormatTimeSpan(record.TotalPenalty),
                    FormatTimeSpan(record.LatenessDuration),
                    FormatTimeSpan(record.BreakPenalty),
                    FormatTimeSpan(record.EarlyLeavePenalty),
                    FormatCsvField(string.Join(" / ", record.PunchTimes.Select(pt => pt.ToString("HH:mm"))))
                ));
            }
            return sb.ToString();
        }

        // --- ADD THIS NEW METHOD for CompanyAttendanceReport ---
        // Note: We must make the class public to access it here
        public string GenerateCompanyReportCsv(CompanyCumulativeSummary summary, DateTime startDate, DateTime endDate)
        {
            var sb = new StringBuilder();
            var inrCulture = new CultureInfo("en-IN");

            sb.AppendLine("Report Period," + $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
            sb.AppendLine("Total Employees Processed," + summary.TotalEmployeesProcessed);
            sb.AppendLine();
            sb.AppendLine("Metric,Total (HH:MM),Total (Decimal Hours)");
            sb.AppendLine($"Total Scheduled,{FormatTimeSpan(summary.TotalScheduledHours)},{summary.TotalScheduledHours.TotalHours.ToString("F2", inrCulture)}");
            sb.AppendLine($"Total Worked,{FormatTimeSpan(TimeSpan.FromHours((double)summary.TotalWorkedHours))},{summary.TotalWorkedHours.ToString("F2", inrCulture)}");
            sb.AppendLine($"Total Overtime,{FormatTimeSpan(summary.TotalOvertime)},{summary.TotalOvertime.TotalHours.ToString("F2", inrCulture)}");
            sb.AppendLine($"Total Penalty,{FormatTimeSpan(summary.TotalOverallPenalty)},{summary.TotalOverallPenalty.TotalHours.ToString("F2", inrCulture)}");
            sb.AppendLine($"Total Lateness,{FormatTimeSpan(summary.TotalLateness)},{summary.TotalLateness.TotalHours.ToString("F2", inrCulture)}");
            sb.AppendLine($"Total Break Penalty,{FormatTimeSpan(summary.TotalBreakPenalty)},{summary.TotalBreakPenalty.TotalHours.ToString("F2", inrCulture)}");
            sb.AppendLine($"Total Early Leave,{FormatTimeSpan(summary.TotalEarlyLeave)},{summary.TotalEarlyLeave.TotalHours.ToString("F2", inrCulture)}");

            return sb.ToString();
        }

        private string FormatCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return "";

            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }

        // Helper to format TimeSpan to HH:MM
        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            int h = (int)Math.Floor(ts.TotalHours);
            int m = ts.Minutes;
            return $"{h:D2}:{m:D2}";
        }
    }
}