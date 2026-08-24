using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.Shared.Data;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// Inserts auto leave rows when required, avoiding duplicates.
    /// </summary>
    public class AttendanceLeavePostingService
    {
        private readonly AppDbContext _db;

        public AttendanceLeavePostingService(AppDbContext db)
        {
            _db = db;
        }

        public async Task PostLeaveIfNeededAsync(int employeeId, DateTime day, string leaveType, string notes, bool approved = true)
        {
            var exists = await _db.LeaveRequests.AnyAsync(l =>
                l.EmployeeID == employeeId &&
                l.LeaveDate.HasValue &&
                l.LeaveDate.Value.Date == day.Date &&
                l.LeaveType == leaveType);

            if (exists) return;

            _db.LeaveRequests.Add(new LeaveRequest
            {
                EmployeeID = employeeId,
                LeaveDate = day,
                LeaveType = leaveType,
                IsApproved = approved,
                Notes = notes
            });

            await _db.SaveChangesAsync();
        }
    }
}
