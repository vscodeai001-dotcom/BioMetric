using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Payroll.Web.Services
{
    public class LeaveAccrualService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<LeaveAccrualService> _logger;

        public LeaveAccrualService(IDbContextFactory<AppDbContext> dbFactory, ILogger<LeaveAccrualService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        /// <summary>
        /// Runs on the 1st of every month to credit leave balances.
        /// </summary>
        public async Task RunMonthlyAccrualAsync()
        {
            var today = DateTime.Now.Date;
            var currentMonth = today.Month;
            var currentYear = today.Year;

            // Create a new context for this background task to avoid scope issues
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            // 1. Check if Accrual is enabled globally
            var settings = await dbContext.CompanySettings.FirstOrDefaultAsync();
            if (settings == null || !settings.EnableLeaveAccrual)
            {
                _logger.LogInformation("Leave Accrual skipped: Module is disabled in Company Settings.");
                return;
            }

            decimal monthlyRate = settings.LeaveAccrualRate; // e.g., 1.5 days/month
            _logger.LogInformation("Starting monthly leave credit run. Rate: {Rate} days.", monthlyRate);

            try
            {
                var employees = await dbContext.Employees.ToListAsync();
                int creditedCount = 0;

                foreach (var emp in employees)
                {
                    // 1. Skip if terminated before today
                    if (emp.TerminationDate.HasValue && emp.TerminationDate.Value.ToDateTime(TimeOnly.MinValue).Date < today) continue;

                    // 2. Skip if hired *this month* (accrual runs on the 1st for the previous month's work)
                    if (emp.HireDate.HasValue && emp.HireDate.Value.Month == currentMonth && emp.HireDate.Value.Year == currentYear) continue;

                    decimal daysToCredit = monthlyRate;

                    // 3. Pro-Rata Calculation: If hired LAST month, calculate partial credit
                    if (emp.HireDate.HasValue && emp.HireDate.Value.Month == currentMonth - 1 && emp.HireDate.Value.Year == currentYear)
                    {
                        int previousMonthDaysInMonth = DateTime.DaysInMonth(currentYear, currentMonth - 1);
                        int daysWorkedInFirstMonth = previousMonthDaysInMonth - emp.HireDate.Value.Day + 1;

                        if (daysWorkedInFirstMonth > 0)
                        {
                            // Formula: (Rate / DaysInMonth) * DaysWorked
                            daysToCredit = monthlyRate * ((decimal)daysWorkedInFirstMonth / previousMonthDaysInMonth);
                        }
                        else
                        {
                            daysToCredit = 0;
                        }
                    }

                    // 4. Apply Credit
                    emp.PaidLeaveBalance += daysToCredit;

                    // Optional: Round to 2 decimal places
                    emp.PaidLeaveBalance = Math.Round(emp.PaidLeaveBalance, 2);

                    _logger.LogInformation("Credited {Credit:F2} days to {Name}. New Balance: {Bal:F2}", daysToCredit, emp.Name, emp.PaidLeaveBalance);

                    dbContext.Employees.Update(emp);
                    creditedCount++;
                }

                if (creditedCount > 0)
                {
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Accrual complete. {Count} employees credited.", creditedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FATAL: Leave Accrual Job failed.");
            }
        }
    }
}