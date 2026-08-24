using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.Shared.Data;
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



        public RegularizationService(
            IDbContextFactory<AppDbContext> dbFactory,
            IHttpContextAccessor httpContextAccessor,
            IEmailSender emailSender,
            UserManager<IdentityUser> userManager,
            AttendanceRefreshService refreshService)
        {
            _dbFactory = dbFactory;
            _httpContextAccessor = httpContextAccessor;
            _emailSender = emailSender;
            _userManager = userManager;
            _refreshService = refreshService;
        }

        // --- 1. EMPLOYEE SUBMITS REQUEST ---
        public async Task SubmitRequestAsync(int employeeId, DateOnly date, TimeOnly time, string reason, bool isInPunch)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            // Check for existing pending request for the same employee/date/punch type
            bool exists = await db.AttendanceRegularizations
                .AnyAsync(r => r.EmployeeId == employeeId && r.DateOfPunch == date && r.IsInPunch == isInPunch && r.Status == "Pending");

            if (exists) throw new InvalidOperationException("A pending request for this specific punch already exists.");

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

            await _refreshService
                .NotifyRegularizationChangedAsync(employeeId);

            // Existing notification flow remains unchanged.
        }

        // --- 2. ADMIN/MANAGER APPROVES/REJECTS ---
        public async Task UpdateStatusAndInjectPunchAsync(int regularizationId, string newStatus, string adminRemarks)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var request = await db.AttendanceRegularizations.FindAsync(regularizationId);
            if (request == null) return;

            // Get approving user details
            string approvingUserId = _httpContextAccessor.HttpContext?.User.FindFirst(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "SYSTEM";
            string approvingUserEmail = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? "System";

            request.Status = newStatus;
            request.AdminRemarks = adminRemarks;
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
                    DeviceID = $"ApprovedBy:{approvingUserEmail}", // Audit trail of approver
                    LogType = "Correction",
                    IsApproved = true
                };

                db.AttendanceLogs.Add(newPunch);

                // Note: DailySummary needs re-calculation. We rely on Admin running reprocess or nightly job.
            }

            await db.SaveChangesAsync();

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

            // --- NOTIFICATION TO EMPLOYEE (Email) ---
            if (emp != null && !string.IsNullOrEmpty(emp.Email))
            {
                string subject = $"Punch Regularization: {newStatus}";
                string body = $"<p>Your punch correction request for <strong>{request.DateOfPunch:dd-MMM}</strong> ({request.PunchTimeNew:HH:mm}) has been <strong>{newStatus}</strong>.</p>" +
                              $"<p>Admin Remarks: {adminRemarks}</p>";

                await _emailSender.SendEmailAsync(emp.Email, subject, body);
            }
        }
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