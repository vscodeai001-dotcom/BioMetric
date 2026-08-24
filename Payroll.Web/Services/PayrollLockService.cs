using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;
using System;
using System.Threading.Tasks;

namespace Payroll.Web.Services
{
    public class PayrollLockService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public PayrollLockService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        /// <summary>
        /// Checks if payroll has been finalized for the month encompassing the given date.
        /// </summary>
        public async Task<bool> IsLockedAsync(int employeeId, DateTime date)
        {
            int year = date.Year;
            int month = date.Month;

            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            // Check if any PayrollHistory entry exists for the employee/month/year
            return await dbContext.PayrollHistories
                .AsNoTracking()
                .AnyAsync(ph => ph.EmployeeID == employeeId &&
                                ph.PayYear == year &&
                                ph.PayMonth == month);
        }
    }
}