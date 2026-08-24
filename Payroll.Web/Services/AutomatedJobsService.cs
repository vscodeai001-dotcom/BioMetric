using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.Shared.Data;
using Payroll.Shared.Services;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Payroll.Web.Services
{
    public class AutomatedJobsService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutomatedJobsService> _logger;
        private readonly IEmailSender _emailSender;
        private readonly AttendanceLeavePostingService _leaveService; // Required for Sandwich Rule

        public AutomatedJobsService(
            IServiceProvider serviceProvider,
            ILogger<AutomatedJobsService> logger,
            IEmailSender emailSender,
            AttendanceLeavePostingService leaveService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _emailSender = emailSender;
            _leaveService = leaveService;
        }

        public async Task MarkYesterdayAbsencesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var calculatorService = scope.ServiceProvider.GetRequiredService<AttendanceCalculatorService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            DateTime dayToProcess = DateTime.Now.Date.AddDays(-1);
            DateOnly dateOnlyToProcess = DateOnly.FromDateTime(dayToProcess);

            _logger.LogInformation("Starting AutomatedJobsService: Calculating and saving summaries for {Date}", dayToProcess);

            var absenteeNames = new List<string>();

            try
            {
                var employeesToProcess = await dbContext.Employees
                    .Where(e => !e.TerminationDate.HasValue || e.TerminationDate.Value.ToDateTime(TimeOnly.MinValue).Date >= dayToProcess)
                    .ToListAsync();

                if (!employeesToProcess.Any())
                {
                    _logger.LogWarning("No active employees found to process.");
                    return;
                }

                var employeeIds = employeesToProcess.Select(e => e.EmployeeID).ToList();

                DateTime logsQueryStart = dayToProcess.AddDays(-1);
                DateTime logsQueryEndExclusive = dayToProcess.AddDays(1);

                var existingLogs = await dbContext.AttendanceLogs
                    .Where(log => log.EmployeeID.HasValue && employeeIds.Contains(log.EmployeeID.Value) &&
                                  log.PunchTime.Date >= logsQueryStart.Date && log.PunchTime.Date < logsQueryEndExclusive.Date)
                    .ToListAsync();

                var existingLeave = await dbContext.LeaveRequests
                    .Where(lr => employeeIds.Contains(lr.EmployeeID) && lr.LeaveDate.HasValue &&
                                  lr.LeaveDate.Value.Date == dayToProcess)
                    .ToListAsync();

                var existingSchedules = await dbContext.ShiftSchedules
                    .Where(s => employeeIds.Contains(s.EmployeeID) && s.ShiftDate == dateOnlyToProcess)
                    .ToListAsync();

                var holidays = await dbContext.CompanyHolidays
                    .Where(h => h.HolidayDate == dateOnlyToProcess)
                    .ToListAsync();

                var companySettings = await dbContext.CompanySettings.FirstOrDefaultAsync() ?? new CompanySetting();
                var featureSettings = await dbContext.FeatureSettings.FirstOrDefaultAsync() ?? new FeatureSettings();

                // --- 1. APPLY SANDWICH RULE ---
                if (companySettings.EnableSandwichRule)
                {
                    // FIX: Pass the correct service instance (_leaveService)
                    await ApplySandwichRuleAsync(dbContext, employeesToProcess, dayToProcess, existingLeave, holidays, _leaveService);
                }
                // ------------------------------

                var newSummaries = new List<DailySummary>();
                int summariesSaved = 0;

                foreach (var emp in employeesToProcess)
                {
                    if (emp.HireDate.HasValue && emp.HireDate.Value.ToDateTime(TimeOnly.MinValue).Date > dayToProcess) continue;

                    bool alreadySummarized = await dbContext.DailySummaries
                        .AnyAsync(s => s.EmployeeID == emp.EmployeeID && s.ShiftDate == dateOnlyToProcess);

                    if (alreadySummarized) continue;

                    var punchesForDay = existingLogs.Where(log => log.EmployeeID == emp.EmployeeID && log.PunchTime.Date == dayToProcess).ToList();
                    var leaveRecord = existingLeave.FirstOrDefault(lr => lr.EmployeeID == emp.EmployeeID);
                    var schedule = existingSchedules.FirstOrDefault(s => s.EmployeeID == emp.EmployeeID);

                    var dailyResult = calculatorService.CalculateDailyResult(
                    emp, dayToProcess, punchesForDay, leaveRecord, schedule, companySettings, holidays, featureSettings); // <-- FIX: ADD featureSettings

                    bool isCompOff = emp.CompOffDayOfWeek.HasValue && dayToProcess.DayOfWeek == emp.CompOffDayOfWeek.Value;
                    bool isWorkingDay = !isCompOff;

                    if (isWorkingDay && (dailyResult.Status == "Absent" || dailyResult.Status == "Incomplete"))
                    {
                        absenteeNames.Add(emp.Name);

                        dbContext.LeaveRequests.Add(new LeaveRequest
                        {
                            EmployeeID = emp.EmployeeID,
                            LeaveDate = dayToProcess,
                            LeaveType = "Loss of Pay (Auto)",
                            Notes = "Automatically marked absence (no punches/leave)",
                            IsApproved = true
                        });
                    }

                    newSummaries.Add(new DailySummary
                    {
                        EmployeeID = emp.EmployeeID,
                        ShiftDate = dateOnlyToProcess,
                        Status = dailyResult.Status,
                        EarnedStandardHours = dailyResult.EarnedStandardDuration.TotalHours < 0 ? 0 : (decimal)dailyResult.EarnedStandardDuration.TotalHours,
                        TotalOvertimeDuration = dailyResult.TotalOvertimeDuration,
                        TotalPenaltyDuration = dailyResult.TotalPenalty,
                        TotalLateness = dailyResult.TotalLateness,
                        TotalBreakPenalty = dailyResult.TotalBreakPenalty,
                        ScheduledShiftDuration = dailyResult.ScheduledShiftDuration
                    });
                    summariesSaved++;
                }

                if (newSummaries.Any())
                {
                    dbContext.DailySummaries.AddRange(newSummaries);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Successfully saved {Count} new DailySummary records for {Date}.", summariesSaved, dayToProcess);
                }

                // --- 2. SEND EMAIL REPORT (Uses injected _emailSender via constructor field) ---
                await SendAbsenteeReport(userManager, _emailSender, absenteeNames, dayToProcess);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during daily summary calculation for {Date}.", dayToProcess);
            }
        }

        private async Task ApplySandwichRuleAsync(
            AppDbContext dbContext,
            List<Employee> employees,
            DateTime processDate,
            List<LeaveRequest> existingLeave,
            List<CompanyHoliday> holidays,
            AttendanceLeavePostingService leaveService)
        {
            var holidaysByDate = holidays.ToDictionary(h => h.HolidayDate);

            foreach (var emp in employees)
            {
                var yesterday = processDate.AddDays(-1);
                var tomorrow = processDate.AddDays(1);

                bool isCompOffDay = emp.CompOffDayOfWeek.HasValue && processDate.DayOfWeek == emp.CompOffDayOfWeek.Value;
                bool isHoliday = holidaysByDate.ContainsKey(DateOnly.FromDateTime(processDate));
                bool isNonWorkingDay = isCompOffDay || isHoliday;

                if (!isNonWorkingDay) continue;

                var yesterdayLeave = existingLeave.FirstOrDefault(l =>
                    l.EmployeeID == emp.EmployeeID &&
                    l.LeaveDate.HasValue &&
                    l.LeaveDate.Value.Date == yesterday &&
                    l.IsApproved);

                var tomorrowLeave = existingLeave.FirstOrDefault(l =>
                    l.EmployeeID == emp.EmployeeID &&
                    l.LeaveDate.HasValue &&
                    l.LeaveDate.Value.Date == tomorrow &&
                    l.IsApproved);

                if (yesterdayLeave != null && tomorrowLeave != null)
                {
                    await leaveService.PostLeaveIfNeededAsync(
                        emp.EmployeeID,
                        processDate,
                        "Sandwich Leave (Auto)",
                        $"Sandwich rule applied: weekend/holiday between {yesterday:dd-MMM} and {tomorrow:dd-MMM}",
                        approved: true
                    );
                }
            }
        }

        private async Task SendAbsenteeReport(UserManager<IdentityUser> userManager, IEmailSender emailSender, List<string> absenteeNames, DateTime reportDate)
        {
            try
            {
                _logger.LogInformation("Building absentee report...");

                var admins = await userManager.GetUsersInRoleAsync("Admin");
                var superAdmins = await userManager.GetUsersInRoleAsync("SuperAdmin");

                var recipients = admins.Concat(superAdmins)
                                       .Where(u => !string.IsNullOrEmpty(u.Email))
                                       .Distinct()
                                       .ToList();

                if (!recipients.Any()) return;

                var subject = $"Daily Absentee Report - {reportDate:dd-MMM-yyyy}";
                var sb = new StringBuilder();
                sb.AppendLine($"<p>Here is the automated attendance report for {reportDate:dddd, dd MMMM yyyy}.</p>");

                if (absenteeNames.Any())
                {
                    sb.AppendLine("<h4>The following employees were marked Absent (Loss of Pay):</h4><ul>");
                    foreach (var name in absenteeNames.OrderBy(n => n))
                    {
                        sb.AppendLine($"<li>{name}</li>");
                    }
                    sb.AppendLine("</ul>");
                }
                else
                {
                    sb.AppendLine("<h4>All employees were present or on approved leave.</h4><p>There are no absentees to report.</p>");
                }

                foreach (var user in recipients)
                {
                    await emailSender.SendEmailAsync(user.Email!, subject, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send absentee email report.");
            }
        }
    }
}