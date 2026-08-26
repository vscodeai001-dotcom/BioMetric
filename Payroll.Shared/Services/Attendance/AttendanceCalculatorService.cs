using Payroll.Shared.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared.Models;
using Payroll.Shared.Services;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// V2 - REWRITTEN CALCULATION ENGINE
    /// This service is the single source of truth for all daily attendance calculations.
    /// It correctly implements all locked-in rules (OT, Breaks, Penalties, Status)
    /// and is used by the LogViewer, CompanyReport, and RunPayroll.
    /// </summary>
    public class AttendanceCalculatorService
    {
        private readonly ILogger<AttendanceCalculatorService> _logger;
        private readonly AttendancePunchProcessor _punchProcessor;
        private readonly AttendanceScheduleService _scheduleService;
        private readonly AttendanceBreakPenaltyService _breakService;
        private readonly AttendanceDayTypeService _dayTypeService;
        private readonly DailySummaryBuilder _summaryBuilder;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        // ---== We will add these dependencies in Program.cs ==---
        // (Note: These services are already in your project, we are just using them)

        public AttendanceCalculatorService(
            ILogger<AttendanceCalculatorService> logger,
            AttendancePunchProcessor punchProcessor,
            AttendanceScheduleService scheduleService,
            AttendanceBreakPenaltyService breakService,
            AttendanceDayTypeService dayTypeService,
            DailySummaryBuilder summaryBuilder,
            IDbContextFactory<AppDbContext> dbFactory)
        {
            _logger = logger;
            _punchProcessor = punchProcessor;
            _scheduleService = scheduleService;
            _breakService = breakService;
            _dayTypeService = dayTypeService;
            _summaryBuilder = summaryBuilder;
            _dbFactory = dbFactory;
        }

        /// <summary>
        /// Calculates the Hourly Rate for an employee for a specific payroll month.
        /// This logic is now centralized here.
        /// </summary>
        public Task<PayrollRateResult> CalculateMonthlyRate(
            Employee emp,
            int year,
            int month,
            CompanySetting settings,            
            List<ShiftSchedule> schedules,
            List<CompanyHoliday> holidays)
        {
            // Rule 1: Direct Hourly Wage (Overrides everything)
            if (emp.DirectHourlyWage.HasValue && emp.DirectHourlyWage > 0)
            {
                return Task.FromResult(new PayrollRateResult
                {
                    HourlyRate = emp.DirectHourlyWage.Value,
                    CalculationMethod = "Pro-Rata Hourly (Direct)"
                });
            }

            // Rule 2: Monthly Salary (Calculate rate based on method)
            if (emp.MonthlySalary <= 0)
            {
                return Task.FromResult(new PayrollRateResult { HourlyRate = 0, CalculationMethod = "N/A (No Salary)" });
            }

            string calcMethod = emp.SalaryCalculationMethod ?? settings.SalaryCalculationMethod;
            decimal monthlySalary = emp.MonthlySalary;
            decimal dailyRate = 0;
            decimal hourlyRate = 0;

            int daysInMonth = DateTime.DaysInMonth(year, month);
            int workDaysInMonth = 0; // Includes holidays and comp-offs

            // Count actual work days (Mon-Fri, excluding CompOff, includes Holidays)
            for (int d = 1; d <= daysInMonth; d++)
            {
                DateTime day = new DateTime(year, month, d);
                bool isCompOff = emp.CompOffDayOfWeek.HasValue && day.DayOfWeek == emp.CompOffDayOfWeek.Value;
                if (!isCompOff)
                {
                    workDaysInMonth++;
                }
            }

            // Get total scheduled hours for the month (for Pro-Rata)
            TimeSpan totalMonthScheduledNet = TimeSpan.Zero;
            for (int d = 1; d <= daysInMonth; d++)
            {
                DateTime day = new DateTime(year, month, d);
                var schedule = schedules.FirstOrDefault(s => s.ShiftDate == DateOnly.FromDateTime(day) && s.EmployeeID == emp.EmployeeID);
                var sr = _scheduleService.CalculateSchedule(emp, day, schedule, settings, emp.StandardBreakMinutes, 0, 0);
                totalMonthScheduledNet += sr.NetScheduled;
            }


            switch (calcMethod)
            {
                case "Fixed 30-Day":
                    dailyRate = monthlySalary / 30;
                    hourlyRate = dailyRate / (emp.StandardHours > 0 ? emp.StandardHours : 8);
                    break;

                case "Fixed 26-Day":
                    dailyRate = monthlySalary / 26;
                    hourlyRate = dailyRate / (emp.StandardHours > 0 ? emp.StandardHours : 8);
                    break;

                case "Days in Month": // ActualDaysInMonth
                    dailyRate = monthlySalary / daysInMonth;
                    hourlyRate = dailyRate / (emp.StandardHours > 0 ? emp.StandardHours : 8);
                    break;

                case "Pro-Rata Hourly":
                default:
                    if (totalMonthScheduledNet.TotalHours > 0)
                    {
                        hourlyRate = monthlySalary / (decimal)totalMonthScheduledNet.TotalHours;
                    }
                    else
                    {
                        hourlyRate = 0; // No hours scheduled, can't calc rate
                    }
                    dailyRate = hourlyRate * (emp.StandardHours > 0 ? emp.StandardHours : 8);
                    break;
            }

            return Task.FromResult(new PayrollRateResult
            {
                HourlyRate = hourlyRate,
                DailyRate = dailyRate,
                CalculationMethod = calcMethod,
                TotalMonthlyScheduledNetHours = totalMonthScheduledNet
            });
        }

        /// <summary>
        /// This is the new, single "brain" function for all daily calculations.
        /// </summary>
        public DailyAttendanceRecord CalculateDailyResult(
            Employee emp,
            DateTime day,
            List<AttendanceLog> punchesForDay,
            LeaveRequest? leaveRecord,
            ShiftSchedule? schedule,
            CompanySetting settings,
            List<CompanyHoliday> holidays,
            FeatureSettings featureSettings)
        {
            // === 1. NORMALIZE PUNCHES ===

            // CRITICAL FIX: Filter out unapproved manual punch requests before processing.
            punchesForDay = punchesForDay
                .Where(p => p.LogType != "Correction Request" || p.IsApproved)
                .ToList();

            // Get a clean, ordered list of IN/OUT pairs (handles odd punches)
            var pr = _punchProcessor.ProcessPunches(punchesForDay ?? new(), day);

            // === 2. DETERMINE DAY TYPE ===
            // Is this a Holiday, Comp-Off, Leave Day, or simple Working Day?
            var dayTypeResult = _dayTypeService.DetectDayType(emp, day, pr.Ordered, leaveRecord, holidays);

            // === 3. GET SHIFT SCHEDULE ===
            // Get shift times, grace periods, and scheduled durations
            int paidBreakMin = emp.StandardBreakMinutes;
            int startGrace = settings.LateGraceMinutes;
            int endGrace = settings.EndTimeGraceMinutes;


            // --- NEW: Pattern Lookup Logic ---
            if (schedule == null)
            {
                using var dbContext = _dbFactory.CreateDbContext();
                // Find a recurring pattern shift that matches the current day of the week
                schedule = dbContext.ShiftSchedules
                    .AsNoTracking()
                    .FirstOrDefault(s =>
                        s.EmployeeID == emp.EmployeeID &&
                        s.IsRecurringPattern &&
                        s.AppliesToDayOfWeek == day.DayOfWeek);

                // Note: PatternDurationDays is reserved for future complex rotation logic.
            }
            // --- END NEW: Pattern Lookup Logic ---

            var scheduleResult = _scheduleService.CalculateSchedule(emp, day, schedule, settings, paidBreakMin, startGrace, endGrace);

            // === 4. INITIALIZE CALCULATION VARS ===
            string status = "ERROR";
            TimeSpan earnedStandard = TimeSpan.Zero;
            TimeSpan lateness = TimeSpan.Zero;
            TimeSpan earlyLeave = TimeSpan.Zero;
            TimeSpan overtime = TimeSpan.Zero;
            TimeSpan totalBreak = TimeSpan.Zero;
            TimeSpan breakPenalty = TimeSpan.Zero;

            // === 5. CALCULATE BASED ON DAY TYPE ===
            switch (dayTypeResult.Type)
            {
                case DayType.NonEmployment:
                    status = "Not Employed";
                    scheduleResult = new ScheduleResult(); // Blank out schedule
                    break;

                case DayType.Holiday:
                    status = holidays.FirstOrDefault(h => h.HolidayDate == DateOnly.FromDateTime(day))?.HolidayName ?? "Holiday";
                    scheduleResult = new ScheduleResult(); // No scheduled hours on holiday
                    break;

                case DayType.HolidayWithWork:
                    status = "Holiday (Worked)";
                    (totalBreak, breakPenalty) = _breakService.CalculateBreakPenalty(pr.Ordered, 0); // 0 paid break on holiday
                    overtime = pr.GrossWorked() - totalBreak; // All worked time is OT
                    scheduleResult = new ScheduleResult(); // No scheduled hours
                    break;

                case DayType.CompOffNoWork:
                    status = "Weekly Off"; // Use "Weekly Off" for clarity
                    scheduleResult = new ScheduleResult(); // No scheduled hours
                    break;

                case DayType.CompOffWithWork:
                    status = "Weekly Off (Worked)";
                    (totalBreak, breakPenalty) = _breakService.CalculateBreakPenalty(pr.Ordered, 0); // 0 paid break
                    overtime = pr.GrossWorked() - totalBreak; // All worked time is OT
                    scheduleResult = new ScheduleResult(); // No scheduled hours
                    break;

                case DayType.WorkingAbsent:
                    // Only APPROVED leave may override an absence.
                    // Pending leave requests must never change attendance status.
                    if (leaveRecord != null && leaveRecord.IsApproved)
                    {
                        status = leaveRecord.LeaveType;

                        if (leaveRecord.IsHalfDay)
                        {
                            // Half-day leave, but no punches = Absent (Half)
                            status = "Absent (Half Day)";

                            scheduleResult.NetScheduled =
                                TimeSpan.FromTicks(
                                    scheduleResult.NetScheduled.Ticks / 2);
                        }
                    }
                    else
                    {
                        status = "Absent";
                    }
                    break;

                // This is the main calculation block
                case DayType.WorkingPresent:
                case DayType.HalfDayApproved:
                default:
                    status = "Present";
                    if (dayTypeResult.Type == DayType.HalfDayApproved)
                    {
                        status = "Half Day";
                        scheduleResult.NetScheduled = TimeSpan.FromTicks(scheduleResult.NetScheduled.Ticks / 2);
                    }

                    // --- A. Lateness & Early Leave (Based on LOCKED rules) ---
                    if (pr.FirstIn.HasValue && scheduleResult.StartGraceEnd.HasValue &&
                        pr.FirstIn.Value > scheduleResult.StartGraceEnd.Value)
                    {
                        lateness = pr.FirstIn.Value - scheduleResult.ShiftStart; // Penalty from SHIFT START
                    }

                    if (pr.LastOut.HasValue && scheduleResult.EndGraceStart.HasValue &&
                        pr.LastOut.Value < scheduleResult.EndGraceStart.Value)
                    {
                        earlyLeave = scheduleResult.ShiftEnd - pr.LastOut.Value; // Penalty from SHIFT END
                    }

                    // --- B. Breaks & Penalties (Based on LOCKED rules) ---
                    (totalBreak, breakPenalty) = _breakService.CalculateBreakPenalty(pr.Ordered, paidBreakMin);

                    // --- C. Standard Worked Hours ---
                    // Standard time is the sum of IN/OUT segments, clamped to the shift window
                    TimeSpan grossWorkedInSegments = pr.GrossWorked();
                    earnedStandard = grossWorkedInSegments - totalBreak; // Start with total work

                    // Clamp to shift boundaries (remove time worked *before* or *after* shift)
                    TimeSpan preShiftWork = TimeSpan.Zero;
                    TimeSpan postShiftWork = TimeSpan.Zero;

                    if (pr.FirstIn.HasValue && pr.FirstIn < scheduleResult.ShiftStart)
                    {
                        preShiftWork = scheduleResult.ShiftStart - pr.FirstIn.Value;
                    }
                    if (pr.LastOut.HasValue && pr.LastOut > scheduleResult.ShiftEnd)
                    {
                        postShiftWork = pr.LastOut.Value - scheduleResult.ShiftEnd;
                    }

                    // 'earnedStandard' is ONLY the time worked INSIDE the shift, minus breaks
                    earnedStandard = earnedStandard - preShiftWork - postShiftWork;
                    if (earnedStandard < TimeSpan.Zero) earnedStandard = TimeSpan.Zero;

                    // --- D. Overtime (Based on LOCKED rule) ---
                    // OT = (Early-IN) - (Early-OUT) + (Post-Shift)
                    TimeSpan earlyInOT = TimeSpan.Zero;
                    TimeSpan earlyOutDeduction = earlyLeave; // Use the already-calculated penalty
                    TimeSpan postShiftOT = postShiftWork;

                    if (pr.FirstIn.HasValue && pr.FirstIn < scheduleResult.ShiftStart)
                    {
                        earlyInOT = scheduleResult.ShiftStart - pr.FirstIn.Value;
                    }

                    // Apply the formula
                    overtime = earlyInOT - earlyOutDeduction + postShiftOT;
                    if (overtime < TimeSpan.Zero) overtime = TimeSpan.Zero;

                    // --- E. Final Status Check (Missing Punch) ---
                    if (pr.Ordered.Count % 2 != 0 || pr.Ordered.Count == 0)
                    {
                        // This case should be WorkingAbsent, but as a fallback:
                        status = "Missing Punch";
                        earnedStandard = TimeSpan.Zero;
                        overtime = TimeSpan.Zero;
                    }
                    else if (pr.Ordered.Count == 1) // Single punch (e.g. IN only)
                    {
                        status = "Missing Punch";
                    }
                    break;


            }

            // === 6. BUILD THE FINAL SUMMARY OBJECT ===
            var summary = _summaryBuilder.BuildSummary(
                day,
                status,
                scheduleResult,
                pr.Ordered.Select(p => TimeOnly.FromDateTime(p.PunchTime)).ToList(),
                totalBreak,
                lateness,
                earlyLeave,
                breakPenalty,
                earnedStandard, // Pass the clamped standard hours
                overtime       // Pass the new OT calculation
            );

            // Add First/Last punch times for display
            if (pr.FirstIn.HasValue) summary.FirstIn = TimeOnly.FromDateTime(pr.FirstIn.Value);
            if (pr.LastOut.HasValue) summary.FinalOut = TimeOnly.FromDateTime(pr.LastOut.Value);

            if (featureSettings.EnableShiftAllowance && settings.EnableShiftAllowance && emp.NightShiftAllowance > 0)
            {
                if ((status == "Present" || status == "Half Day") &&
                    scheduleResult.ShiftEnd.Date > scheduleResult.ShiftStart.Date)
                {
                    // It's a night shift!
                    summary.ShiftAllowanceEarned = emp.NightShiftAllowance;
                }
            }

            return summary;
        }
    }

    // Helper model for the Rate Calculator
    public class PayrollRateResult
    {
        public decimal HourlyRate { get; set; }
        public decimal DailyRate { get; set; }
        public string CalculationMethod { get; set; } = string.Empty;
        public TimeSpan TotalMonthlyScheduledNetHours { get; set; }
    }
}