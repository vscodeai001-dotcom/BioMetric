using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.Shared.Data;
using Payroll.Shared.Services;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Payroll.Web.Services
{
    public class RegularizationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IEmailSender _emailSender;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AttendanceRefreshService _refreshService;
        private readonly NotificationService _notificationService;
        private readonly AttendanceCalculatorService _calculator;



        public RegularizationService(
            IDbContextFactory<AppDbContext> dbFactory,
            IHttpContextAccessor httpContextAccessor,
            IEmailSender emailSender,
            UserManager<IdentityUser> userManager,
            AttendanceRefreshService refreshService,
            NotificationService notificationService,
            AttendanceCalculatorService calculator)
        {
            _dbFactory = dbFactory;
            _httpContextAccessor = httpContextAccessor;
            _emailSender = emailSender;
            _userManager = userManager;
            _refreshService = refreshService;
            _notificationService = notificationService;
            _calculator = calculator;
        }

        private const string ResubmissionAllowedMarker = "[[RESUBMIT_ALLOWED]]";

        public static bool CanResubmit(AttendanceRegularization request)
        {
            return string.Equals(request.Status, "Rejected", StringComparison.OrdinalIgnoreCase)
                && (request.AdminRemarks?.Contains(ResubmissionAllowedMarker, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        public static string GetDisplayAdminRemarks(string? remarks)
        {
            if (string.IsNullOrWhiteSpace(remarks)) return string.Empty;
            return remarks.Replace(ResubmissionAllowedMarker, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        }

        private static string BuildAdminRemarks(string? remarks, bool allowResubmission)
        {
            var clean = GetDisplayAdminRemarks(remarks);
            return allowResubmission
                ? (string.IsNullOrWhiteSpace(clean) ? ResubmissionAllowedMarker : $"{clean}\n{ResubmissionAllowedMarker}")
                : clean;
        }

        // --- 1. EMPLOYEE SUBMITS REQUEST ---
        public async Task<int> SubmitRequestAsync(int employeeId, DateOnly date, TimeOnly time, string reason, bool isInPunch)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var features = await db.FeatureSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
            if (features != null && (!features.EnablePunchCorrection || !features.EnableRegularizationRequest))
                throw new InvalidOperationException("Attendance correction requests are currently disabled by the administrator.");

            // One active request per employee/date/punch type. A rejected request can
            // be submitted again only when the administrator explicitly allows it.
            var latest = await db.AttendanceRegularizations
                .Where(r => r.EmployeeId == employeeId && r.DateOfPunch == date && r.IsInPunch == isInPunch)
                .OrderByDescending(r => r.SubmissionDate)
                .ThenByDescending(r => r.RegularizationId)
                .FirstOrDefaultAsync();

            if (latest != null)
            {
                if (string.Equals(latest.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A pending request for this specific punch already exists.");

                if (string.Equals(latest.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("This punch correction has already been approved.");

                if (string.Equals(latest.Status, "Rejected", StringComparison.OrdinalIgnoreCase) && !CanResubmit(latest))
                    throw new InvalidOperationException("The administrator has disabled another request for this date and punch.");
            }

            var request = new AttendanceRegularization
            {
                EmployeeId = employeeId,
                DateOfPunch = date,
                PunchTimeNew = time,
                Reason = reason,
                IsInPunch = isInPunch,
                Status = "Pending",
                SubmissionDate = DateTime.Now
            };

            db.AttendanceRegularizations.Add(request);
            await db.SaveChangesAsync();
            var createdId = request.RegularizationId;

            await _refreshService
                .NotifyRegularizationChangedAsync(employeeId);

            var employee = await db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeID == employeeId);
            if (employee != null)
            {
                await _notificationService.NotifyAdminsAsync(
                    "New Regularization Request",
                    $"{employee.Name} submitted a {(isInPunch ? "IN" : "OUT")} regularization for {date:dd-MMM-yyyy} at {time:hh:mm tt}.",
                    "/attendance/regularization-approval");
            }

            return createdId;
        }

        // --- 2. ADMIN/MANAGER APPROVES/REJECTS ---
        public async Task UpdateStatusAndInjectPunchAsync(int regularizationId, string newStatus, string adminRemarks, bool allowResubmission = false)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var request = await db.AttendanceRegularizations.FindAsync(regularizationId);
            if (request == null) return;

            if (!string.Equals(request.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This request has already been processed.");

            if (!string.Equals(newStatus, "Approved", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(newStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid regularization status.");

            if (string.Equals(newStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                allowResubmission = false;

            var features = await db.FeatureSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
            if (allowResubmission && (features == null || !features.EnableRegularizationRequest || !features.EnablePunchCorrection))
                allowResubmission = false;

            // Get approving user details
            string approvingUserId = _httpContextAccessor.HttpContext?.User.FindFirst(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "SYSTEM";
            string approvingUserEmail = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? "System";

            request.Status = newStatus;
            request.AdminRemarks = string.Equals(newStatus, "Rejected", StringComparison.OrdinalIgnoreCase)
                ? BuildAdminRemarks(adminRemarks, allowResubmission)
                : GetDisplayAdminRemarks(adminRemarks);
            request.ApprovedById = approvingUserId; // Log the approver

            // Fetch the employee to get their email for notification
            var emp = await db.Employees.FindAsync(request.EmployeeId);


            if (newStatus == "Approved")
            {
                // CRITICAL STEP: Inject the new Punch into the main AttendanceLog
                var newPunch = new AttendanceLog
                {
                    EmployeeID = request.EmployeeId,
                    BiometricID = "REGULARIZATION",
                    PunchTime = request.DateOfPunch.ToDateTime(request.PunchTimeNew),
                    DeviceID = $"ApprovedBy:{approvingUserEmail}",
                    // The approved correction becomes a real IN/OUT punch so the
                    // existing AttendancePunchProcessor and calculator treat it
                    // exactly like a normal attendance event.
                    LogType = request.IsInPunch ? "IN" : "OUT",
                    IsApproved = true
                };

                db.AttendanceLogs.Add(newPunch);
            }

            await db.SaveChangesAsync();

            if (newStatus == "Approved")
            {
                await RecalculateSingleDayAsync(
                    db,
                    request.EmployeeId,
                    request.DateOfPunch);
            }

            if (newStatus == "Approved")
            {
                await _refreshService
                    .NotifyAttendanceChangedAsync(
                        request.EmployeeId,
                        request.DateOfPunch);
            }

            await _refreshService
                .NotifyRegularizationChangedAsync(
                    request.EmployeeId);

            await _notificationService.NotifyEmployeeAsync(
                request.EmployeeId,
                $"Regularization {newStatus}",
                $"Your {(request.IsInPunch ? "IN" : "OUT")} regularization request for {request.DateOfPunch:dd-MMM-yyyy} at {request.PunchTimeNew:hh:mm tt} was {newStatus.ToLowerInvariant()}.",
                "/my-regularization");

            // --- NOTIFICATION TO EMPLOYEE (Email) ---
            if (emp != null && !string.IsNullOrEmpty(emp.Email))
            {
                string subject = $"Punch Regularization: {newStatus}";
                string body = $"<p>Your punch correction request for <strong>{request.DateOfPunch:dd-MMM}</strong> ({request.PunchTimeNew:HH:mm}) has been <strong>{newStatus}</strong>.</p>" +
                              $"<p>Admin Remarks: {adminRemarks}</p>";

                await _emailSender.SendEmailAsync(emp.Email, subject, body);
            }
        }

    private async Task RecalculateSingleDayAsync(
        AppDbContext db,
        int employeeId,
        DateOnly dayOnly)
    {
        var day = dayOnly.ToDateTime(TimeOnly.MinValue);

        var emp = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeID == employeeId);
        if (emp == null) return;

        var settings = await db.CompanySettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingID == 1)
            ?? new CompanySetting();
        var features = await db.FeatureSettings.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == 1)
            ?? new FeatureSettings();
        var holidays = await db.CompanyHolidays
            .Where(h => h.HolidayDate == dayOnly)
            .ToListAsync();
        var punches = await db.AttendanceLogs
            .Where(l => l.EmployeeID == employeeId && l.PunchTime.Date == day.Date)
            .ToListAsync();
        var schedule = await db.ShiftSchedules
            .FirstOrDefaultAsync(s => s.EmployeeID == employeeId && s.ShiftDate == dayOnly);
        var leave = await db.LeaveRequests
            .FirstOrDefaultAsync(l => l.EmployeeID == employeeId &&
                l.LeaveDate.HasValue && l.LeaveDate.Value.Date == day.Date);

        var result = _calculator.CalculateDailyResult(
            emp,
            day,
            punches,
            leave,
            schedule,
            settings,
            holidays,
            features);

        var summary = await db.DailySummaries
            .FirstOrDefaultAsync(s => s.EmployeeID == employeeId && s.ShiftDate == dayOnly);

        if (summary == null)
        {
            summary = new DailySummary
            {
                EmployeeID = employeeId,
                ShiftDate = dayOnly
            };
            db.DailySummaries.Add(summary);
        }

        summary.Status = result.Status;
        summary.EarnedStandardHours = (decimal)result.EarnedStandardDuration.TotalHours;
        summary.TotalOvertimeDuration = result.TotalOvertimeDuration;
        summary.TotalPenaltyDuration = result.TotalPenalty;
        summary.TotalLateness = result.TotalLateness;
        summary.TotalBreakPenalty = result.TotalBreakPenalty;
        summary.ScheduledShiftDuration = result.ScheduledShiftDuration;
        summary.ShiftAllowanceEarned = result.ShiftAllowanceEarned;
        summary.IsManualOverride = false;

        await db.SaveChangesAsync();
    }

    public class RegularizationFormModel
    {
        [Required] public DateTime DateOfPunch { get; set; }
        [Required] public string PunchTimeNew { get; set; } = "09:00";
        // CRITICAL FIX: Change bool? to string
        [Required(ErrorMessage = "Select IN or OUT.")] public string? PunchTypeString { get; set; }
        [Required, StringLength(250)] public string Reason { get; set; } = "";
    }
}
}
