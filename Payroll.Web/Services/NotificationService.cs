using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;
using Payroll.Web.Hubs;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Payroll.Web.Services
{
    public class NotificationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IHubContext<AttendanceRefreshHub> _hub;
        private readonly UserManager<IdentityUser> _userManager;
       
        public NotificationService(
            IDbContextFactory<AppDbContext> dbFactory,
            IHubContext<AttendanceRefreshHub> hub,
            UserManager<IdentityUser> userManager)
        {
            _dbFactory = dbFactory;
            _hub = hub;
            _userManager = userManager;
        }

        public async Task SendNotificationAsync(string userId, string title, string message, string? url = null)
        {
            // 1. Save to Database (Persistence)
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Url = url,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();

            await _hub.Clients.User(userId).SendAsync("NotificationChanged");
        }

        public async Task NotifyAdminsEmployeeLoginAsync(
            string employeeName,
            string email,
            string ipAddress,
            string userAgent,
            DateTime loginTimeUtc,
            bool replacedExistingSession,
            string? gpsDetails = null)
        {
            var title = replacedExistingSession
                ? "Employee session replaced"
                : "Employee login detected";

            var message =
                $"Employee: {employeeName} ({email}); " +
                $"Time UTC: {loginTimeUtc:yyyy-MM-dd HH:mm:ss}; " +
                $"IP: {ipAddress}; " +
                $"GPS: {gpsDetails ?? "Pending from device"}; " +
                $"Device: {userAgent}";

            if (message.Length > 500)
                message = message[..500];

            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            var superAdminUsers = await _userManager.GetUsersInRoleAsync("SuperAdmin");
            var adminIds = adminUsers
                .Concat(superAdminUsers)
                .Select(u => u.Id)
                .Distinct()
                .ToList();

            foreach (var adminId in adminIds)
            {
                await SendNotificationAsync(
                    adminId,
                    title,
                    message,
                    "/");
            }
        }

        public async Task MarkAsReadAsync(int notificationId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var note = await db.Notifications.FindAsync(notificationId);
            if (note != null)
            {
                note.IsRead = true;
                await db.SaveChangesAsync();
            }
        }

        public async Task<List<Notification>> GetRecentNotificationsAsync(string userId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            // Load last 10 notifications (unread first)
            return await db.Notifications
                .Where(n => n.UserId == userId)
                .OrderBy(n => n.IsRead)
                .ThenByDescending(n => n.CreatedAt)
                .Take(10)
                .ToListAsync();
        }
    }
}