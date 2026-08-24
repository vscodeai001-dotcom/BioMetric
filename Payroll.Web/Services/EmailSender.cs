using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payroll.Shared.Data;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace Payroll.Web.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly ILogger<EmailSender> _logger;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public EmailSender(ILogger<EmailSender> logger, IDbContextFactory<AppDbContext> dbFactory)
        {
            _logger = logger;
            _dbFactory = dbFactory;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            try
            {
                // 1. Load Settings from DB
                using var dbContext = await _dbFactory.CreateDbContextAsync();
                var settings = await dbContext.CompanySettings.AsNoTracking().FirstOrDefaultAsync(s => s.SettingID == 1);
                var features = await dbContext.FeatureSettings.AsNoTracking().FirstOrDefaultAsync(f => f.Id == 1);
                // 2. Validate Settings
                if (settings == null ||
            features?.EnableEmailNotifications != true || // <-- CHECK MASTER TOGGLE HERE
            string.IsNullOrEmpty(settings.SmtpHost) ||
            string.IsNullOrEmpty(settings.SmtpUser) ||
            string.IsNullOrEmpty(settings.SmtpPass))
                {
                    _logger.LogWarning("⚠️ Email not sent. Notifications are disabled or SMTP settings are missing.");
                    return;
                }

                // 3. Configure Client
                using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
                {
                    Credentials = new NetworkCredential(settings.SmtpUser, settings.SmtpPass),
                    EnableSsl = settings.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false
                };

                // 4. Prepare Message
                var mailMessage = new MailMessage
                {
                    From = new MailAddress(settings.SmtpFromEmail ?? settings.SmtpUser, "Payroll System"),
                    Subject = subject,
                    Body = htmlMessage,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(email);

                // 5. Send
                await client.SendMailAsync(mailMessage);
                _logger.LogInformation("✅ Email sent successfully to {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to send email to {Email}", email);
                // We do NOT throw the exception here to prevent crashing the main app logic (like Payroll processing)
            }
        }
    }
}