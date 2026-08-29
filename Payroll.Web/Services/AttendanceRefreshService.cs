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

        public async Task NotifyPunchCreatedAsync(
            int employeeId,
            DateOnly date)
        {
            await _hub.Clients.All.SendAsync(
                "PunchChanged",
                new
                {
                    EmployeeId = employeeId,
                    Date = date.ToString("yyyy-MM-dd"),
                    Action = "CREATED",
                    Timestamp = DateTime.UtcNow
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


        /*
         * ==========================================================
         * LEAVE REQUEST CHANGES
         * ==========================================================
         *
         * Notify when leave requests are created, updated, or deleted.
         */

        public async Task NotifyLeaveChangedAsync(
            int? employeeId = null,
            DateTime? leaveDate = null,
            string? action = null)
        {
            await _hub.Clients.All.SendAsync(
                "LeaveChanged",
                new
                {
                    EmployeeId = employeeId,
                    LeaveDate = leaveDate?.ToString("yyyy-MM-dd"),
                    Action = action ?? "MODIFIED",
                    Timestamp = DateTime.UtcNow
                });
        }


        /*
         * ==========================================================
         * SALARY ADVANCE CHANGES
         * ==========================================================
         *
         * Notify when salary advances are created or deleted.
         */

        public async Task NotifyAdvanceChangedAsync(
            int? employeeId = null,
            string? action = null)
        {
            await _hub.Clients.All.SendAsync(
                "AdvanceChanged",
                new
                {
                    EmployeeId = employeeId,
                    Action = action ?? "MODIFIED",
                    Timestamp = DateTime.UtcNow
                });
        }


        /*
         * ==========================================================
         * PUNCH CHANGES (Manual/Correction)
         * ==========================================================
         *
         * Notify when punches are added, edited, or deleted.
         */

        public async Task NotifyPunchChangedAsync(
            int? employeeId = null,
            DateOnly? date = null,
            string? action = null)
        {
            await _hub.Clients.All.SendAsync(
                "PunchChanged",
                new
                {
                    EmployeeId = employeeId,
                    Date = date?.ToString("yyyy-MM-dd"),
                    Action = action ?? "MODIFIED",
                    Timestamp = DateTime.UtcNow
                });
        }


        /*
         * ==========================================================
         * EMPLOYEE DATA CHANGES
         * ==========================================================
         *
         * Notify when employee records are updated.
         */

        public async Task NotifyEmployeeChangedAsync(
            int? employeeId = null,
            string? action = null)
        {
            await _hub.Clients.All.SendAsync(
                "EmployeeChanged",
                new
                {
                    EmployeeId = employeeId,
                    Action = action ?? "MODIFIED",
                    Timestamp = DateTime.UtcNow
                });
        }


        /*
         * ==========================================================
         * EXIT/RESIGNATION CHANGES
         * ==========================================================
         *
         * Notify when exit or resignation data is modified.
         */

        public async Task NotifyExitChangedAsync(
            int? employeeId = null,
            string? action = null)
        {
            await _hub.Clients.All.SendAsync(
                "ExitChanged",
                new
                {
                    EmployeeId = employeeId,
                    Action = action ?? "MODIFIED",
                    Timestamp = DateTime.UtcNow
                });
        }


        /*
         * ==========================================================
         * BULK REFRESH FOR GLOBAL CRUD
         * ==========================================================
         *
         * Notify all clients to refresh when major operations occur.
         */

        public async Task NotifyGlobalRefreshAsync(string? reason = null)
        {
            await _hub.Clients.All.SendAsync(
                "GlobalRefresh",
                new
                {
                    Reason = reason ?? "DATA_MODIFIED",
                    Timestamp = DateTime.UtcNow
                });
        }
    }
}