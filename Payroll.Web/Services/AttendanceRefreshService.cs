using Microsoft.AspNetCore.SignalR;
using Payroll.Web.Hubs;

namespace Payroll.Web.Services
{
    public sealed class AttendanceRefreshService
    {
        private readonly IHubContext<AttendanceRefreshHub> _hub;

        public AttendanceRefreshService(
            IHubContext<AttendanceRefreshHub> hub)
        {
            _hub = hub;
        }


        /*
         * ==========================================================
         * GENERIC ATTENDANCE DATA CHANGE
         * ==========================================================
         *
         * Use this after ANY successful attendance-related CRUD.
         *
         * Examples:
         *
         * - Mobile punch
         * - Manual punch
         * - Edit punch
         * - Delete punch
         * - Leave approval
         * - Leave cancellation
         * - Attendance correction
         * - Regularization
         * - Shift changes
         * - Recalculation
         *
         */

        public async Task NotifyDataChangedAsync(
            int? employeeId = null,
            DateOnly? date = null,
            string? source = null)
        {
            await _hub.Clients.All.SendAsync(
                "DataChanged",
                new
                {
                    EmployeeId = employeeId,

                    Date =
                        date?.ToString("yyyy-MM-dd"),

                    Source = source ?? "ATTENDANCE",

                    Timestamp =
                        DateTime.UtcNow
                });
        }


        public async Task NotifyAttendanceChangedAsync(
            int? employeeId = null,
            DateOnly? date = null)
        {
            await _hub.Clients.All.SendAsync(
                "AttendanceChanged",
                new
                {
                    EmployeeId = employeeId,

                    Date =
                        date?.ToString("yyyy-MM-dd"),

                    Timestamp =
                        DateTime.UtcNow
                });
        }


        public async Task NotifyLocationChangedAsync(
            int? employeeId = null)
        {
            await _hub.Clients.All.SendAsync(
                "LocationChanged",
                new
                {
                    EmployeeId = employeeId,

                    Timestamp =
                        DateTime.UtcNow
                });
        }


        public async Task NotifyRegularizationChangedAsync(
            int? employeeId = null)
        {
            await _hub.Clients.All.SendAsync(
                "RegularizationChanged",
                new
                {
                    EmployeeId = employeeId,

                    Timestamp =
                        DateTime.UtcNow
                });
        }


        public async Task NotifyAllDataChangedAsync()
        {
            await NotifyDataChangedAsync(
                null,
                null,
                "GLOBAL");
        }
    }
}