using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Payroll.Web.Services
{
    /// <summary>
    /// Service to check employee deletion dependencies and prevent orphaned data.
    /// Ensures referential integrity by identifying all related records before deletion.
    /// </summary>
    public class EmployeeDeletionService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<EmployeeDeletionService> _logger;

        public EmployeeDeletionService(IDbContextFactory<AppDbContext> dbFactory, ILogger<EmployeeDeletionService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        /// <summary>
        /// Data structure representing all related records for an employee
        /// </summary>
        public class EmployeeDeletionDependencies
        {
            public int EmployeeID { get; set; }
            public string EmployeeName { get; set; } = "";
            public bool CanDelete { get; set; }
            public string BlockReason { get; set; } = "";

            // Related Records Count
            public int AttendanceLogsCount { get; set; } = 0;
            public int PayrollHistoryCount { get; set; } = 0;
            public int SalaryAdvancesCount { get; set; } = 0;
            public int LeaveRequestsCount { get; set; } = 0;
            public int ShiftSchedulesCount { get; set; } = 0;
            public int BonusRecordsCount { get; set; } = 0;
            public int DailySummariesCount { get; set; } = 0;

            /// <summary>
            /// Get total count of all related records
            /// </summary>
            public int GetTotalDependencies()
            {
                return AttendanceLogsCount 
                    + PayrollHistoryCount 
                    + SalaryAdvancesCount 
                    + LeaveRequestsCount 
                    + ShiftSchedulesCount 
                    + BonusRecordsCount 
                    + DailySummariesCount;
            }

            /// <summary>
            /// Get a formatted list of all dependencies for display
            /// </summary>
            public List<(string Category, int Count)> GetDependenciesList()
            {
                var list = new List<(string, int)>();
                
                if (AttendanceLogsCount > 0) list.Add(("Attendance Logs", AttendanceLogsCount));
                if (PayrollHistoryCount > 0) list.Add(("Payroll History", PayrollHistoryCount));
                if (SalaryAdvancesCount > 0) list.Add(("Salary Advances", SalaryAdvancesCount));
                if (LeaveRequestsCount > 0) list.Add(("Leave Requests", LeaveRequestsCount));
                if (ShiftSchedulesCount > 0) list.Add(("Shift Schedules", ShiftSchedulesCount));
                if (BonusRecordsCount > 0) list.Add(("Bonus Records", BonusRecordsCount));
                if (DailySummariesCount > 0) list.Add(("Daily Summaries", DailySummariesCount));

                return list;
            }
        }

        /// <summary>
        /// Check if an employee can be deleted by examining all related records
        /// </summary>
        public async Task<EmployeeDeletionDependencies> CheckDeletionDependenciesAsync(int employeeID)
        {
            var dependencies = new EmployeeDeletionDependencies { EmployeeID = employeeID };

            try
            {
                await using var dbContext = await _dbFactory.CreateDbContextAsync();

                // Get employee name
                var employee = await dbContext.Employees.FindAsync(employeeID);
                if (employee == null)
                {
                    dependencies.BlockReason = "Employee not found.";
                    dependencies.CanDelete = false;
                    return dependencies;
                }

                dependencies.EmployeeName = employee.Name;

                // Count all related records in parallel for performance
                var attendanceLogsTask = dbContext.AttendanceLogs.Where(a => a.EmployeeID == employeeID).CountAsync();
                var payrollHistoryTask = dbContext.PayrollHistories.Where(p => p.EmployeeID == employeeID).CountAsync();
                var salaryAdvancesTask = dbContext.SalaryAdvances.Where(s => s.EmployeeID == employeeID).CountAsync();
                var leaveRequestsTask = dbContext.LeaveRequests.Where(l => l.EmployeeID == employeeID).CountAsync();
                var shiftSchedulesTask = dbContext.ShiftSchedules.Where(s => s.EmployeeID == employeeID).CountAsync();
                var bonusRecordsTask = dbContext.BonusRecords.Where(b => b.EmployeeID == employeeID).CountAsync();
                var dailySummariesTask = dbContext.DailySummaries.Where(d => d.EmployeeID == employeeID).CountAsync();

                // Wait for all counts
                await Task.WhenAll(
                    attendanceLogsTask, payrollHistoryTask, salaryAdvancesTask, leaveRequestsTask,
                    shiftSchedulesTask, bonusRecordsTask, dailySummariesTask
                );

                // Assign counts
                dependencies.AttendanceLogsCount = await attendanceLogsTask;
                dependencies.PayrollHistoryCount = await payrollHistoryTask;
                dependencies.SalaryAdvancesCount = await salaryAdvancesTask;
                dependencies.LeaveRequestsCount = await leaveRequestsTask;
                dependencies.ShiftSchedulesCount = await shiftSchedulesTask;
                dependencies.BonusRecordsCount = await bonusRecordsTask;
                dependencies.DailySummariesCount = await dailySummariesTask;

                // Determine if employee can be deleted
                int totalDependencies = dependencies.GetTotalDependencies();

                if (totalDependencies > 0)
                {
                    dependencies.CanDelete = false;
                    var depList = dependencies.GetDependenciesList();
                    var depSummary = string.Join(", ", depList.Select(d => $"{d.Count} {d.Category}"));
                    dependencies.BlockReason = $"Cannot delete employee. Related records exist: {depSummary}. Please delete these records first.";
                }
                else
                {
                    dependencies.CanDelete = true;
                    dependencies.BlockReason = "";
                }

                _logger.LogInformation($"Deletion check for employee {employeeID} ({employee.Name}): CanDelete={dependencies.CanDelete}, Dependencies={totalDependencies}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking deletion dependencies for employee {employeeID}");
                dependencies.CanDelete = false;
                dependencies.BlockReason = $"Error checking dependencies: {ex.Message}";
            }

            return dependencies;
        }
    }
}
