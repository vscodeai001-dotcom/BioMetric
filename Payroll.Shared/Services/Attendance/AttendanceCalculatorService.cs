using Payroll.Shared.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared.Models;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// V2 - CENTRAL ATTENDANCE CALCULATION ENGINE
    ///
    /// BUSINESS REGION:
    /// India
    ///
    /// BUSINESS TIMEZONE:
    /// Asia/Kolkata
    ///
    /// IMPORTANT:
    /// Existing database PunchTime values are treated as business-local
    /// attendance timestamps. They are NOT automatically converted to UTC.
    ///
    /// This service remains the single source of truth for:
    /// - Worked hours
    /// - OT
    /// - Breaks
    /// - Penalties
    /// - Lateness
    /// - Early leave
    /// - Attendance status
    /// </summary>
    public class AttendanceCalculatorService
    {
        private const string IndiaTimeZoneId =
            "Asia/Kolkata";

        private readonly TimeZoneInfo _indiaTimeZone;

        private readonly ILogger<AttendanceCalculatorService>
            _logger;

        private readonly AttendancePunchProcessor
            _punchProcessor;

        private readonly AttendanceScheduleService
            _scheduleService;

        private readonly AttendanceBreakPenaltyService
            _breakService;

        private readonly AttendanceDayTypeService
            _dayTypeService;

        private readonly DailySummaryBuilder
            _summaryBuilder;

        private readonly IDbContextFactory<AppDbContext>
            _dbFactory;

        public AttendanceCalculatorService(
            ILogger<AttendanceCalculatorService> logger,
            AttendancePunchProcessor punchProcessor,
            AttendanceScheduleService scheduleService,
            AttendanceBreakPenaltyService breakService,
            AttendanceDayTypeService dayTypeService,
            DailySummaryBuilder summaryBuilder,
            IDbContextFactory<AppDbContext> dbFactory)
        {
            _logger = logger;

            _punchProcessor =
                punchProcessor;

            _scheduleService =
                scheduleService;

            _breakService =
                breakService;

            _dayTypeService =
                dayTypeService;

            _summaryBuilder =
                summaryBuilder;

            _dbFactory =
                dbFactory;

            _indiaTimeZone =
                TimeZoneInfo.FindSystemTimeZoneById(
                    IndiaTimeZoneId);
        }

        // ============================================================
        // BUSINESS DATE NORMALIZATION
        // ============================================================

        private DateTime NormalizeBusinessDate(
            DateTime value)
        {
            /*
             * DateTime values used by payroll attendance are business
             * wall-clock values.
             *
             * We deliberately do NOT call ToLocalTime().
             *
             * Render runs in UTC, while payroll runs in India.
             */

            return DateTime.SpecifyKind(
                value,
                DateTimeKind.Unspecified);
        }

        private DateTime NormalizePunchTime(
            DateTime value)
        {
            return DateTime.SpecifyKind(
                value,
                DateTimeKind.Unspecified);
        }

        // ============================================================
        // CURRENT INDIA DATE
        // ============================================================

        public DateTime GetIndiaNow()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,
                _indiaTimeZone);
        }

        public DateOnly GetIndiaToday()
        {
            return DateOnly.FromDateTime(
                GetIndiaNow().Date);
        }

        // ================================================================
        // CALCULATE ACTUAL WORK OUTSIDE SHIFT
        // ================================================================

        private static TimeSpan CalculateOutsideShiftOvertime(
            List<AttendanceLog> punches,
            DateTime shiftStart,
            DateTime shiftEnd)
        {
            if (punches == null ||
                punches.Count < 2)
            {
                return TimeSpan.Zero;
            }

            shiftStart =
                DateTime.SpecifyKind(
                    shiftStart,
                    DateTimeKind.Unspecified);

            shiftEnd =
                DateTime.SpecifyKind(
                    shiftEnd,
                    DateTimeKind.Unspecified);

            TimeSpan totalOt =
                TimeSpan.Zero;

            // Punches are already ordered by the processor.
            for (int i = 0;
                 i + 1 < punches.Count;
                 i += 2)
            {
                DateTime inTime =
                    DateTime.SpecifyKind(
                        punches[i].PunchTime,
                        DateTimeKind.Unspecified);

                DateTime outTime =
                    DateTime.SpecifyKind(
                        punches[i + 1].PunchTime,
                        DateTimeKind.Unspecified);

                if (outTime <= inTime)
                {
                    continue;
                }

                // --------------------------------------------------------
                // WORK BEFORE SHIFT
                // --------------------------------------------------------

                if (inTime < shiftStart)
                {
                    DateTime preShiftEnd =
                        outTime < shiftStart
                            ? outTime
                            : shiftStart;

                    if (preShiftEnd > inTime)
                    {
                        totalOt +=
                            preShiftEnd - inTime;
                    }
                }

                // --------------------------------------------------------
                // WORK AFTER SHIFT
                // --------------------------------------------------------

                if (outTime > shiftEnd)
                {
                    DateTime postShiftStart =
                        inTime > shiftEnd
                            ? inTime
                            : shiftEnd;

                    if (outTime > postShiftStart)
                    {
                        totalOt +=
                            outTime - postShiftStart;
                    }
                }
            }

            return totalOt;
        }

        // ============================================================
        // MONTHLY RATE
        // ============================================================

        public Task<PayrollRateResult> CalculateMonthlyRate(
            Employee emp,
            int year,
            int month,
            CompanySetting settings,
            List<ShiftSchedule> schedules,
            List<CompanyHoliday> holidays)
        {
            // --------------------------------------------------------
            // DIRECT HOURLY WAGE
            // --------------------------------------------------------

            if (emp.DirectHourlyWage.HasValue &&
                emp.DirectHourlyWage > 0)
            {
                return Task.FromResult(
                    new PayrollRateResult
                    {
                        HourlyRate =
                            emp.DirectHourlyWage.Value,

                        CalculationMethod =
                            "Pro-Rata Hourly (Direct)"
                    });
            }

            // --------------------------------------------------------
            // NO MONTHLY SALARY
            // --------------------------------------------------------

            if (emp.MonthlySalary <= 0)
            {
                return Task.FromResult(
                    new PayrollRateResult
                    {
                        HourlyRate = 0,

                        CalculationMethod =
                            "N/A (No Salary)"
                    });
            }

            string calcMethod =
                emp.SalaryCalculationMethod ??
                settings.SalaryCalculationMethod;

            decimal monthlySalary =
                emp.MonthlySalary;

            decimal dailyRate = 0;
            decimal hourlyRate = 0;

            int daysInMonth =
                DateTime.DaysInMonth(
                    year,
                    month);

            int workDaysInMonth = 0;

            // --------------------------------------------------------
            // COUNT WORK DAYS
            // --------------------------------------------------------

            for (int d = 1;
                 d <= daysInMonth;
                 d++)
            {
                DateTime day =
                    new DateTime(
                        year,
                        month,
                        d);

                bool isCompOff =
                    emp.CompOffDayOfWeek.HasValue &&
                    day.DayOfWeek ==
                    emp.CompOffDayOfWeek.Value;

                if (!isCompOff)
                {
                    workDaysInMonth++;
                }
            }

            // --------------------------------------------------------
            // MONTH SCHEDULED HOURS
            // --------------------------------------------------------

            TimeSpan totalMonthScheduledNet =
                TimeSpan.Zero;

            for (int d = 1;
                 d <= daysInMonth;
                 d++)
            {
                DateTime day =
                    new DateTime(
                        year,
                        month,
                        d);

                var schedule =
                    schedules.FirstOrDefault(
                        s =>
                            s.ShiftDate ==
                            DateOnly.FromDateTime(day) &&
                            s.EmployeeID ==
                            emp.EmployeeID);

                var sr =
                    _scheduleService.CalculateSchedule(
                        emp,
                        day,
                        schedule,
                        settings,
                        emp.StandardBreakMinutes,
                        0,
                        0);

                totalMonthScheduledNet +=
                    sr.NetScheduled;
            }

            // --------------------------------------------------------
            // RATE METHOD
            // --------------------------------------------------------

            switch (calcMethod)
            {
                case "Fixed 30-Day":

                    dailyRate =
                        monthlySalary / 30;

                    hourlyRate =
                        dailyRate /
                        (
                            emp.StandardHours > 0
                                ? emp.StandardHours
                                : 8
                        );

                    break;

                case "Fixed 26-Day":

                    dailyRate =
                        monthlySalary / 26;

                    hourlyRate =
                        dailyRate /
                        (
                            emp.StandardHours > 0
                                ? emp.StandardHours
                                : 8
                        );

                    break;

                case "Days in Month":

                    dailyRate =
                        monthlySalary /
                        daysInMonth;

                    hourlyRate =
                        dailyRate /
                        (
                            emp.StandardHours > 0
                                ? emp.StandardHours
                                : 8
                        );

                    break;

                case "Pro-Rata Hourly":

                default:

                    if (totalMonthScheduledNet.TotalHours > 0)
                    {
                        hourlyRate =
                            monthlySalary /
                            (decimal)
                            totalMonthScheduledNet.TotalHours;
                    }
                    else
                    {
                        hourlyRate = 0;
                    }

                    dailyRate =
                        hourlyRate *
                        (
                            emp.StandardHours > 0
                                ? emp.StandardHours
                                : 8
                        );

                    break;
            }

            return Task.FromResult(
                new PayrollRateResult
                {
                    HourlyRate =
                        hourlyRate,

                    DailyRate =
                        dailyRate,

                    CalculationMethod =
                        calcMethod,

                    TotalMonthlyScheduledNetHours =
                        totalMonthScheduledNet
                });
        }

        // ============================================================
        // DAILY CALCULATION
        // ============================================================

        public DailyAttendanceRecord CalculateDailyResult(
            Employee emp,
            DateTime day,
            List<AttendanceLog> punchesForDay,
            LeaveRequest? leaveRecord,
            ShiftSchedule? schedule,
            CompanySetting settings,
            List<CompanyHoliday> holidays,
            FeatureSettings featureSettings)
        {
            // --------------------------------------------------------
            // NORMALIZE BUSINESS DATE
            // --------------------------------------------------------

            day =
                NormalizeBusinessDate(day);

            // --------------------------------------------------------
            // NORMALIZE EXISTING PUNCHES
            //
            // IMPORTANT:
            // No timezone conversion.
            // Existing database punch clock values are preserved.
            // --------------------------------------------------------

            punchesForDay =
                (punchesForDay ??
                 new List<AttendanceLog>())
                .Select(p =>
                {
                    p.PunchTime =
                        NormalizePunchTime(
                            p.PunchTime);

                    return p;
                })
                .OrderBy(
                    p => p.PunchTime)
                .ToList();

            // --------------------------------------------------------
            // REMOVE UNAPPROVED CORRECTIONS
            // --------------------------------------------------------

            punchesForDay =
                punchesForDay
                    .Where(
                        p =>
                            p.LogType !=
                                "Correction Request" ||
                            p.IsApproved)
                    .ToList();

            // --------------------------------------------------------
            // PROCESS PUNCHES
            // --------------------------------------------------------

            var pr =
                _punchProcessor.ProcessPunches(
                    punchesForDay,
                    day);

            // --------------------------------------------------------
            // DETERMINE DAY TYPE
            // --------------------------------------------------------

            var dayTypeResult =
                _dayTypeService.DetectDayType(
                    emp,
                    day,
                    pr.Ordered,
                    leaveRecord,
                    holidays);

            // --------------------------------------------------------
            // SCHEDULE
            // --------------------------------------------------------

            int paidBreakMin =
                emp.StandardBreakMinutes;

            int startGrace =
                settings.LateGraceMinutes;

            int endGrace =
                settings.EndTimeGraceMinutes;

            // --------------------------------------------------------
            // RECURRING PATTERN
            // --------------------------------------------------------

            if (schedule == null)
            {
                using var dbContext =
                    _dbFactory.CreateDbContext();

                schedule =
                    dbContext.ShiftSchedules
                        .AsNoTracking()
                        .FirstOrDefault(
                            s =>
                                s.EmployeeID ==
                                    emp.EmployeeID &&
                                s.IsRecurringPattern &&
                                s.AppliesToDayOfWeek ==
                                    day.DayOfWeek);
            }

            // --------------------------------------------------------
            // CALCULATE SCHEDULE
            // --------------------------------------------------------

            var scheduleResult =
                _scheduleService.CalculateSchedule(
                    emp,
                    day,
                    schedule,
                    settings,
                    paidBreakMin,
                    startGrace,
                    endGrace);

            // --------------------------------------------------------
            // INITIAL VALUES
            // --------------------------------------------------------

            string status = "ERROR";

            TimeSpan earnedStandard =
                TimeSpan.Zero;

            TimeSpan lateness =
                TimeSpan.Zero;

            TimeSpan earlyLeave =
                TimeSpan.Zero;

            TimeSpan overtime =
                TimeSpan.Zero;

            TimeSpan totalBreak =
                TimeSpan.Zero;

            TimeSpan breakPenalty =
                TimeSpan.Zero;

            // ========================================================
            // DAY TYPE
            // ========================================================

            switch (dayTypeResult.Type)
            {
                // ----------------------------------------------------
                // NOT EMPLOYED
                // ----------------------------------------------------

                case DayType.NonEmployment:

                    status =
                        "Not Employed";

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ----------------------------------------------------
                // HOLIDAY
                // ----------------------------------------------------

                case DayType.Holiday:

                    status =
                        holidays
                            .FirstOrDefault(
                                h =>
                                    h.HolidayDate ==
                                    DateOnly.FromDateTime(day))
                            ?.HolidayName ??
                        "Holiday";

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ----------------------------------------------------
                // HOLIDAY WORKED
                // ----------------------------------------------------

                case DayType.HolidayWithWork:

                    status =
                        "Holiday (Worked)";

                    (totalBreak,
                     breakPenalty) =
                        _breakService
                            .CalculateBreakPenalty(
                                pr.Ordered,
                                0);

                    overtime =
                        pr.GrossWorked() -
                        totalBreak;

                    if (overtime < TimeSpan.Zero)
                    {
                        overtime =
                            TimeSpan.Zero;
                    }

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ----------------------------------------------------
                // WEEKLY OFF NO WORK
                // ----------------------------------------------------

                case DayType.CompOffNoWork:

                    status =
                        "Weekly Off";

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ----------------------------------------------------
                // WEEKLY OFF WORKED
                // ----------------------------------------------------

                case DayType.CompOffWithWork:

                    status =
                        "Weekly Off (Worked)";

                    (totalBreak,
                     breakPenalty) =
                        _breakService
                            .CalculateBreakPenalty(
                                pr.Ordered,
                                0);

                    overtime =
                        pr.GrossWorked() -
                        totalBreak;

                    if (overtime < TimeSpan.Zero)
                    {
                        overtime =
                            TimeSpan.Zero;
                    }

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ----------------------------------------------------
                // ABSENT
                // ----------------------------------------------------

                case DayType.WorkingAbsent:

                    if (leaveRecord != null &&
                        leaveRecord.IsApproved)
                    {
                        status =
                            leaveRecord.LeaveType;

                        if (leaveRecord.IsHalfDay)
                        {
                            status =
                                "Absent (Half Day)";

                            scheduleResult.NetScheduled =
                                TimeSpan.FromTicks(
                                    scheduleResult
                                        .NetScheduled
                                        .Ticks / 2);
                        }
                    }
                    else
                    {
                        status =
                            "Absent";
                    }

                    break;

                // ----------------------------------------------------
                // PRESENT / HALF DAY
                // ----------------------------------------------------

                case DayType.WorkingPresent:
                case DayType.HalfDayApproved:
                default:

                    status =
                        "Present";

                    if (dayTypeResult.Type ==
                        DayType.HalfDayApproved)
                    {
                        status =
                            "Half Day";

                        scheduleResult.NetScheduled =
                            TimeSpan.FromTicks(
                                scheduleResult
                                    .NetScheduled
                                    .Ticks / 2);
                    }

                    // ------------------------------------------------
                    // LATENESS
                    // ------------------------------------------------

                    if (
                        pr.FirstIn.HasValue &&
                        scheduleResult
                            .StartGraceEnd
                            .HasValue &&
                        pr.FirstIn.Value >
                            scheduleResult
                                .StartGraceEnd.Value)
                    {
                        lateness =
                            pr.FirstIn.Value -
                            scheduleResult.ShiftStart;
                    }

                    // ------------------------------------------------
                    // EARLY LEAVE
                    // ------------------------------------------------

                    if (
                        pr.LastOut.HasValue &&
                        scheduleResult
                            .EndGraceStart
                            .HasValue &&
                        pr.LastOut.Value <
                            scheduleResult
                                .EndGraceStart.Value)
                    {
                        earlyLeave =
                            scheduleResult.ShiftEnd -
                            pr.LastOut.Value;
                    }

                    // ------------------------------------------------
                    // BREAKS
                    // ------------------------------------------------

                    (totalBreak,
                     breakPenalty) =
                        _breakService
                            .CalculateBreakPenalty(
                                pr.Ordered,
                                paidBreakMin);

                    // ------------------------------------------------
                    // GROSS WORK
                    // ------------------------------------------------

                    TimeSpan grossWorkedInSegments =
                        pr.GrossWorked();

                    earnedStandard =
                        grossWorkedInSegments -
                        totalBreak;

                    // ------------------------------------------------
                    // PRE-SHIFT
                    // ------------------------------------------------

                    TimeSpan preShiftWork =
                        TimeSpan.Zero;

                    TimeSpan postShiftWork =
                        TimeSpan.Zero;

                    if (
                        pr.FirstIn.HasValue &&
                        pr.FirstIn.Value <
                            scheduleResult.ShiftStart)
                    {
                        preShiftWork =
                            scheduleResult.ShiftStart -
                            pr.FirstIn.Value;
                    }

                    // ------------------------------------------------
                    // POST-SHIFT
                    // ------------------------------------------------

                    if (
                        pr.LastOut.HasValue &&
                        pr.LastOut.Value >
                            scheduleResult.ShiftEnd)
                    {
                        postShiftWork =
                            pr.LastOut.Value -
                            scheduleResult.ShiftEnd;
                    }

                    // ------------------------------------------------
                    // STANDARD WORKED HOURS
                    // ------------------------------------------------

                    earnedStandard =
                        earnedStandard -
                        preShiftWork -
                        postShiftWork;

                    if (earnedStandard <
                        TimeSpan.Zero)
                    {
                        earnedStandard =
                            TimeSpan.Zero;
                    }

                    // ------------------------------------------------
                    // OT OUTSIDE ACTUAL SHIFT
                    // ------------------------------------------------
                    //
                    // Rule:
                    //
                    // Only ACTUAL WORK outside the scheduled shift
                    // counts as OT.
                    //
                    // Example:
                    //
                    // Shift: 18:00 - 22:00
                    //
                    // 17:00 IN
                    // 17:03 OUT
                    //
                    // OT = 00:03
                    //
                    // NOT 01:00.
                    //
                    // Likewise:
                    //
                    // 18:00 IN
                    // 22:03 OUT
                    //
                    // OT = 00:03.
                    //
                    // We calculate OT from actual punch segments,
                    // not simply FirstIn -> ShiftStart.
                    //
                    // ------------------------------------------------

                    overtime =
                        CalculateOutsideShiftOvertime(
                            pr.Ordered,
                            scheduleResult.ShiftStart,
                            scheduleResult.ShiftEnd);

                    if (overtime < TimeSpan.Zero)
                    {
                        overtime =
                            TimeSpan.Zero;
                    }

                    // ------------------------------------------------
                    // MISSING PUNCH
                    // ------------------------------------------------

                    if (pr.Ordered.Count == 0)
                    {
                        status =
                            "Missing Punch";

                        earnedStandard =
                            TimeSpan.Zero;

                        overtime =
                            TimeSpan.Zero;
                    }
                    else if (
                        pr.Ordered.Count % 2 != 0)
                    {
                        status =
                            "Missing Punch";

                        earnedStandard =
                            TimeSpan.Zero;

                        overtime =
                            TimeSpan.Zero;
                    }

                    break;
            }

            // ========================================================
            // BUILD SUMMARY
            // ========================================================

            var summary =
                _summaryBuilder.BuildSummary(
                    day,
                    status,
                    scheduleResult,
                    pr.Ordered
                        .Select(
                            p =>
                                TimeOnly.FromDateTime(
                                    p.PunchTime))
                        .ToList(),
                    totalBreak,
                    lateness,
                    earlyLeave,
                    breakPenalty,
                    earnedStandard,
                    overtime);

            // ========================================================
            // FIRST / FINAL PUNCH
            // ========================================================

            if (pr.FirstIn.HasValue)
            {
                summary.FirstIn =
                    TimeOnly.FromDateTime(
                        pr.FirstIn.Value);
            }

            if (pr.LastOut.HasValue)
            {
                summary.FinalOut =
                    TimeOnly.FromDateTime(
                        pr.LastOut.Value);
            }

            // ========================================================
            // NIGHT SHIFT ALLOWANCE
            // ========================================================

            if (
                featureSettings.EnableShiftAllowance &&
                settings.EnableShiftAllowance &&
                emp.NightShiftAllowance > 0 &&
                (
                    status == "Present" ||
                    status == "Half Day"
                ) &&
                scheduleResult
                    .ShiftEnd
                    .Date >
                scheduleResult
                    .ShiftStart
                    .Date)
            {
                summary.ShiftAllowanceEarned =
                    emp.NightShiftAllowance;
            }

            return summary;
        }
    }

    // ================================================================
    // PAYROLL RATE RESULT
    // ================================================================

    public class PayrollRateResult
    {
        public decimal HourlyRate { get; set; }

        public decimal DailyRate { get; set; }

        public string CalculationMethod { get; set; }
            = string.Empty;

        public TimeSpan TotalMonthlyScheduledNetHours
        {
            get;
            set;
        }
    }
}