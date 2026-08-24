using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;
using System;
using System.Threading.Tasks;

namespace Payroll.Web.Services
{
    public class NotificationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
       
        public NotificationService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
            
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