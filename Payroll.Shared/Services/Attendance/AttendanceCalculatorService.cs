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
    /// attendance timestamps.
    ///
    /// They are NOT automatically converted to UTC.
    ///
    /// This service remains the single source of truth for:
    /// - Worked hours
    /// - OT
    /// - Breaks
    /// - Penalties
    /// - Lateness
    /// - Early leave
    /// - Attendance status
    ///
    /// OPEN PUNCH RULE:
    ///
    /// When today's attendance contains an odd number of punches,
    /// the final IN remains open and is calculated up to the current
    /// India business time.
    ///
    /// No fake OUT punch is inserted into the database.
    ///
    /// Historical odd-punch dates remain Missing Punch because the
    /// system cannot know when the employee actually stopped working.
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

            // --------------------------------------------------------
            // Linux / Render uses:
            // Asia/Kolkata
            //
            // Windows uses:
            // India Standard Time
            //
            // This keeps the business timezone stable in both
            // environments.
            // --------------------------------------------------------

            try
            {
                _indiaTimeZone =
                    TimeZoneInfo.FindSystemTimeZoneById(
                        IndiaTimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                _indiaTimeZone =
                    TimeZoneInfo.FindSystemTimeZoneById(
                        "India Standard Time");
            }
        }

        // ============================================================
        // BUSINESS DATE NORMALIZATION
        // ============================================================

        private DateTime NormalizeBusinessDate(
            DateTime value)
        {
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
        // CURRENT INDIA DATE / TIME
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

        // ============================================================
        // OPEN PUNCH END TIME
        // ============================================================
        //
        // Only today's odd punch is considered an open work segment.
        //
        // Example:
        //
        // 18:00 IN
        // 20:51 current India time
        //
        // Open segment:
        //
        // 18:00 -> 20:51
        //
        // We do NOT write 20:51 into AttendanceLogs.
        //
        // Historical odd punches return null.
        // ============================================================

        private DateTime? GetOpenPunchEndTime(
            DateTime businessDay,
            List<AttendanceLog> punches)
        {
            if (punches == null ||
                punches.Count == 0 ||
                punches.Count % 2 == 0)
            {
                return null;
            }

            DateTime today =
                GetIndiaNow().Date;

            DateTime requestedDay =
                businessDay.Date;

            // --------------------------------------------------------
            // Only today's open punch can be calculated live.
            // --------------------------------------------------------

            if (requestedDay != today)
            {
                return null;
            }

            DateTime now =
                DateTime.SpecifyKind(
                    GetIndiaNow(),
                    DateTimeKind.Unspecified);

            DateTime lastPunch =
                DateTime.SpecifyKind(
                    punches[^1].PunchTime,
                    DateTimeKind.Unspecified);

            // --------------------------------------------------------
            // Never create a negative open interval.
            // --------------------------------------------------------

            if (now <= lastPunch)
            {
                return null;
            }

            return now;
        }

        // ============================================================
        // GROSS WORK INCLUDING CURRENT OPEN PUNCH
        // ============================================================

        private TimeSpan CalculateGrossWorkedIncludingOpenPunch(
            List<AttendanceLog> punches,
            DateTime? openPunchEnd)
        {
            if (punches == null ||
                punches.Count == 0)
            {
                return TimeSpan.Zero;
            }

            TimeSpan total =
                TimeSpan.Zero;

            // --------------------------------------------------------
            // Existing completed IN -> OUT pairs.
            //
            // This is the same rule as the existing GrossWorked().
            // --------------------------------------------------------

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

                if (outTime > inTime)
                {
                    total +=
                        outTime - inTime;
                }
            }

            // --------------------------------------------------------
            // CURRENT OPEN IN
            // --------------------------------------------------------
            //
            // Example:
            //
            // 17:36 IN
            // 20:51 NOW
            //
            // Add:
            //
            // 03:15
            // --------------------------------------------------------

            if (openPunchEnd.HasValue &&
                punches.Count % 2 != 0)
            {
                DateTime openStart =
                    DateTime.SpecifyKind(
                        punches[^1].PunchTime,
                        DateTimeKind.Unspecified);

                DateTime openEnd =
                    DateTime.SpecifyKind(
                        openPunchEnd.Value,
                        DateTimeKind.Unspecified);

                if (openEnd > openStart)
                {
                    total +=
                        openEnd - openStart;
                }
            }

            return total;
        }

        // ============================================================
        // OUTSIDE SHIFT OT
        // ============================================================

        private static TimeSpan CalculateOutsideShiftOvertime(
            List<AttendanceLog> punches,
            DateTime shiftStart,
            DateTime shiftEnd,
            DateTime? openPunchEnd)
        {
            if (punches == null ||
                punches.Count == 0)
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

            // --------------------------------------------------------
            // EXISTING COMPLETED PAIRS
            // --------------------------------------------------------

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

                // ----------------------------------------------------
                // WORK BEFORE SHIFT
                // ----------------------------------------------------

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

                // ----------------------------------------------------
                // WORK AFTER SHIFT
                // ----------------------------------------------------

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

            // --------------------------------------------------------
            // CURRENT OPEN SEGMENT
            // --------------------------------------------------------
            //
            // Example:
            //
            // Shift = 18:00 - 22:00
            //
            // 17:27 IN
            // 17:36 OUT
            // 17:36 IN
            // 20:51 NOW
            //
            // The open segment contributes:
            //
            // 17:36 -> 18:00 = pre-shift OT
            // 18:00 -> 20:51 = normal worked time
            //
            // If now passes 22:00:
            //
            // 18:00 -> 22:00 = normal worked time
            // 22:00 -> now    = OT
            // --------------------------------------------------------

            if (openPunchEnd.HasValue &&
                punches.Count % 2 != 0)
            {
                DateTime inTime =
                    DateTime.SpecifyKind(
                        punches[^1].PunchTime,
                        DateTimeKind.Unspecified);

                DateTime outTime =
                    DateTime.SpecifyKind(
                        openPunchEnd.Value,
                        DateTimeKind.Unspecified);

                if (outTime > inTime)
                {
                    // ------------------------------------------------
                    // BEFORE SHIFT
                    // ------------------------------------------------

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

                    // ------------------------------------------------
                    // AFTER SHIFT
                    // ------------------------------------------------

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
            day =
                NormalizeBusinessDate(day);

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

            punchesForDay =
                punchesForDay
                    .Where(
                        p =>
                            p.LogType !=
                                "Correction Request" ||
                            p.IsApproved)
                    .ToList();

            var pr =
                _punchProcessor.ProcessPunches(
                    punchesForDay,
                    day);

            // --------------------------------------------------------
            // CURRENT OPEN PUNCH
            // --------------------------------------------------------

            DateTime? openPunchEnd =
                GetOpenPunchEndTime(
                    day,
                    pr.Ordered);

            var dayTypeResult =
                _dayTypeService.DetectDayType(
                    emp,
                    day,
                    pr.Ordered,
                    leaveRecord,
                    holidays);

            int paidBreakMin =
                emp.StandardBreakMinutes;

            int startGrace =
                settings.LateGraceMinutes;

            int endGrace =
                settings.EndTimeGraceMinutes;

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

            var scheduleResult =
                _scheduleService.CalculateSchedule(
                    emp,
                    day,
                    schedule,
                    settings,
                    paidBreakMin,
                    startGrace,
                    endGrace);

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

            switch (dayTypeResult.Type)
            {
                // ====================================================
                // NOT EMPLOYED
                // ====================================================

                case DayType.NonEmployment:

                    status =
                        "Not Employed";

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ====================================================
                // HOLIDAY
                // ====================================================

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

                // ====================================================
                // HOLIDAY WORKED
                // ====================================================

                case DayType.HolidayWithWork:

                    status =
                        "Holiday (Worked)";

                    (totalBreak,
                     breakPenalty) =
                        _breakService
                            .CalculateBreakPenalty(
                                pr.Ordered,
                                0);

                    earnedStandard =
                        CalculateGrossWorkedIncludingOpenPunch(
                            pr.Ordered,
                            openPunchEnd) -
                        totalBreak;

                    overtime =
                        earnedStandard;

                    if (overtime < TimeSpan.Zero)
                    {
                        overtime =
                            TimeSpan.Zero;
                    }

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ====================================================
                // WEEKLY OFF NO WORK
                // ====================================================

                case DayType.CompOffNoWork:

                    status =
                        "Weekly Off";

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ====================================================
                // WEEKLY OFF WORKED
                // ====================================================

                case DayType.CompOffWithWork:

                    status =
                        "Weekly Off (Worked)";

                    (totalBreak,
                     breakPenalty) =
                        _breakService
                            .CalculateBreakPenalty(
                                pr.Ordered,
                                0);

                    earnedStandard =
                        CalculateGrossWorkedIncludingOpenPunch(
                            pr.Ordered,
                            openPunchEnd) -
                        totalBreak;

                    overtime =
                        earnedStandard;

                    if (overtime < TimeSpan.Zero)
                    {
                        overtime =
                            TimeSpan.Zero;
                    }

                    scheduleResult =
                        new ScheduleResult();

                    break;

                // ====================================================
                // ABSENT
                // ====================================================

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

                // ====================================================
                // PRESENT / HALF DAY
                // ====================================================

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
                    //
                    // Existing rule preserved.
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
                    //
                    // Only confirmed OUT is used.
                    //
                    // An open IN cannot be assumed to be early leave.
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
                    //
                    // Existing break calculation is untouched.
                    // ------------------------------------------------

                    (totalBreak,
                     breakPenalty) =
                        _breakService
                            .CalculateBreakPenalty(
                                pr.Ordered,
                                paidBreakMin);

                    // ------------------------------------------------
                    // GROSS WORK
                    //
                    // Existing completed pairs are calculated exactly
                    // as before.
                    //
                    // Today's open IN is additionally calculated
                    // through current India time.
                    // ------------------------------------------------

                    TimeSpan grossWorkedInSegments =
                        CalculateGrossWorkedIncludingOpenPunch(
                            pr.Ordered,
                            openPunchEnd);

                    earnedStandard =
                        grossWorkedInSegments -
                        totalBreak;

                    // ------------------------------------------------
                    // PRE-SHIFT
                    // ------------------------------------------------

                    TimeSpan preShiftWork =
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
                    //
                    // For a confirmed OUT use LastOut.
                    //
                    // For today's open IN use current India time.
                    // ------------------------------------------------

                    DateTime? effectiveFinalWorkTime =
                        pr.LastOut;

                    if (!effectiveFinalWorkTime.HasValue &&
                        openPunchEnd.HasValue)
                    {
                        effectiveFinalWorkTime =
                            openPunchEnd.Value;
                    }

                    TimeSpan postShiftWork =
                        TimeSpan.Zero;

                    if (
                        effectiveFinalWorkTime.HasValue &&
                        effectiveFinalWorkTime.Value >
                            scheduleResult.ShiftEnd)
                    {
                        postShiftWork =
                            effectiveFinalWorkTime.Value -
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

                    overtime =
                        CalculateOutsideShiftOvertime(
                            pr.Ordered,
                            scheduleResult.ShiftStart,
                            scheduleResult.ShiftEnd,
                            openPunchEnd);

                    if (overtime < TimeSpan.Zero)
                    {
                        overtime =
                            TimeSpan.Zero;
                    }

                    // ------------------------------------------------
                    // NO PUNCH
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

                    // ------------------------------------------------
                    // ODD PUNCH
                    //
                    // IMPORTANT:
                    //
                    // Today's odd punch is NOT Missing Punch anymore.
                    //
                    // It is a live/open attendance segment.
                    //
                    // Historical odd punches remain Missing Punch.
                    // ------------------------------------------------

                    else if (
                        pr.Ordered.Count % 2 != 0)
                    {
                        if (openPunchEnd.HasValue)
                        {
                            status =
                                "Present";
                        }
                        else
                        {
                            status =
                                "Missing Punch";

                            earnedStandard =
                                TimeSpan.Zero;

                            overtime =
                                TimeSpan.Zero;
                        }
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

            // --------------------------------------------------------
            // IMPORTANT:
            //
            // Do NOT put the artificial current time into FinalOut.
            //
            // FinalOut represents a real OUT punch.
            //
            // The current open time is used internally only for
            // calculation.
            // --------------------------------------------------------

            if (pr.LastOut.HasValue)
            {
                summary.FinalOut =
                    TimeOnly.FromDateTime(
                        pr.LastOut.Value);
            }

            // ============================================================
            // NIGHT SHIFT ALLOWANCE
            // ============================================================

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