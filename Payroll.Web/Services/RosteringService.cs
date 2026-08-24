using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payroll.Shared;
using Payroll.Shared.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Payroll.Web.Services
{
    public class RosteringService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<RosteringService> _logger;

        public RosteringService(IDbContextFactory<AppDbContext> dbFactory, ILogger<RosteringService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        /// <summary>
        /// Generates the necessary daily shift entries for active employees over a period,
        /// based on recurring patterns.
        /// </summary>
        public async Task<int> GenerateScheduleFromPatternsAsync(DateOnly startDate, DateOnly endDate)
        {
            _logger.LogInformation("Starting pattern-based schedule generation from {Start} to {End}.", startDate, endDate);
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var featureSettings = await dbContext.FeatureSettings.AsNoTracking().FirstOrDefaultAsync(f => f.Id == 1);
            if (featureSettings == null || !featureSettings.EnableShiftScheduling)
            {
                _logger.LogWarning("Schedule generation skipped: EnableShiftScheduling feature is disabled.");
                return 0;
            }

            var employees = await dbContext.Employees
                .AsNoTracking()
                .Where(e => !e.IsDeleted)
                .ToListAsync();

            var recurringPatterns = await dbContext.ShiftSchedules
                .AsNoTracking()
                .Where(s => s.IsRecurringPattern)
                .ToListAsync();

            var shiftsToInsert = new List<ShiftSchedule>();
            int schedulesCreated = 0;

            for (DateOnly date = startDate; date <= endDate; date = date.AddDays(1))
            {
                DayOfWeek day = date.DayOfWeek;

                foreach (var emp in employees)
                {
                    // Ignore if a manual shift override already exists for this date/employee
                    bool exists = await dbContext.ShiftSchedules
                        .AnyAsync(s => s.EmployeeID == emp.EmployeeID && s.ShiftDate == date);

                    if (exists) continue;

                    var pattern = recurringPatterns
                        .FirstOrDefault(p =>
                            p.EmployeeID == emp.EmployeeID &&
                            p.AppliesToDayOfWeek == day);

                    if (pattern != null)
                    {
                        shiftsToInsert.Add(new ShiftSchedule
                        {
                            EmployeeID = emp.EmployeeID,
                            ShiftDate = date,
                            StartTime = pattern.StartTime,
                            EndTime = pattern.EndTime,
                            IsRecurringPattern = false,
                            PatternDurationDays = 0,
                            AppliesToDayOfWeek = day
                        });
                        schedulesCreated++;
                    }
                }
            }

            if (shiftsToInsert.Any())
            {
                dbContext.ShiftSchedules.AddRange(shiftsToInsert);
                await dbContext.SaveChangesAsync();
            }

            _logger.LogInformation("Schedule generation complete. {Count} daily shifts created.", schedulesCreated);
            return schedulesCreated;
        }

        /// <summary>
        /// Background job logic to check all rotating employees and advance their schedule pattern.
        /// </summary>
        public async Task RunShiftRotationJobAsync()
        {
            _logger.LogInformation("Starting weekly shift rotation job.");
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            var today = DateOnly.FromDateTime(DateTime.Now.Date);

            // Fetch employees enabled for rotation
            var employeesToRotate = await dbContext.Employees
                .Where(e => e.EnableShiftRotation && e.ShiftRotationPattern != null)
                .ToListAsync();

            foreach (var emp in employeesToRotate)
            {
                var pattern = emp.ShiftRotationPattern!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                      .Select(s => s.Trim())
                                                      .ToList();

                if (!pattern.Any()) continue;

                // 2. Determine the Next Shift (Advance the index)
                int nextIndex = (emp.CurrentShiftIndex + 1) % pattern.Count;
                string nextShiftTime = pattern[nextIndex];

                // Parse StartTime and EndTime from the pattern string (e.g., "09:00-18:00")
                var timeParts = nextShiftTime.Split('-');

                if (timeParts.Length == 2 &&
                    TimeOnly.TryParse(timeParts[0].Trim(), out var startTime) &&
                    TimeOnly.TryParse(timeParts[1].Trim(), out var endTime))
                {
                    // 3. Update Employee Profile for audit
                    emp.CurrentShiftIndex = nextIndex;
                    emp.LastRotatedDate = today;
                    emp.ShiftStartTime = startTime;
                    emp.ShiftEndTime = endTime;

                    dbContext.Employees.Update(emp);
                    _logger.LogInformation("Employee {Id} rotated to index {Index}: {Shift}", emp.EmployeeID, nextIndex, nextShiftTime);
                }
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Shift rotation job finished.");
        }
    }
}