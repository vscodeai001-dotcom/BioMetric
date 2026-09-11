using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;

namespace Payroll.Web.Controllers;

[ApiController]
[Route("api/mobile/admin/attendance")]
[Authorize(AuthenticationSchemes = "MobileBearer", Roles = "Admin,SuperAdmin")]
public sealed class MobileAdminAttendanceController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    public MobileAdminAttendanceController(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] string from, [FromQuery] string to, [FromQuery] int employeeId = 0)
    {
        if (!DateOnly.TryParse(from, out var start) || !DateOnly.TryParse(to, out var end) || start > end)
            return BadRequest(new { success = false, message = "Invalid date range." });
        await using var db = await _dbFactory.CreateDbContextAsync();
        var feature = await db.FeatureSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
        if (feature != null && User.IsInRole("Admin") && !User.IsInRole("SuperAdmin") && !feature.AdminCanViewAttendance)
            return Forbid();
        var employees = await db.Employees.AsNoTracking().Where(e => !e.IsDeleted && (employeeId <= 0 || e.EmployeeID == employeeId)).OrderBy(e => e.Name).ToListAsync();
        var ids = employees.Select(e => e.EmployeeID).ToList();
        var summaries = await db.DailySummaries.AsNoTracking().Where(x => ids.Contains(x.EmployeeID) && x.ShiftDate >= start && x.ShiftDate <= end).OrderByDescending(x => x.ShiftDate).ThenBy(x => x.EmployeeID).ToListAsync();
        var punches = await db.AttendanceLogs.AsNoTracking().Where(x => ids.Contains(x.EmployeeID ?? 0) && x.PunchTime >= start.ToDateTime(TimeOnly.MinValue) && x.PunchTime < end.AddDays(1).ToDateTime(TimeOnly.MinValue)).OrderBy(x => x.PunchTime).ToListAsync();
        var names = employees.ToDictionary(e => e.EmployeeID, e => e.Name);
        var rows = summaries.Select(s => new DailyRowDto {
            EmployeeID=s.EmployeeID, EmployeeName=names.GetValueOrDefault(s.EmployeeID,"Unknown"), Date=s.ShiftDate.ToString("yyyy-MM-dd"), Status=s.Status,
            WorkedHours=s.EarnedStandardHours, OvertimeMinutes=s.TotalOvertimeDuration.TotalMinutes, PenaltyMinutes=s.TotalPenaltyDuration.TotalMinutes,
            LatenessMinutes=s.TotalLateness.TotalMinutes, BreakPenaltyMinutes=s.TotalBreakPenalty.TotalMinutes, ScheduledMinutes=s.ScheduledShiftDuration.TotalMinutes,
            Punches=string.Join("  •  ", punches.Where(p => (p.EmployeeID ?? 0)==s.EmployeeID && DateOnly.FromDateTime(p.PunchTime)==s.ShiftDate).Select(p => $"{p.PunchTime:HH:mm} {p.LogType}"))
        }).ToList();
        return Ok(new { success=true, rows });
    }

    [HttpGet("company-summary")]
    public async Task<IActionResult> CompanySummary([FromQuery] string from, [FromQuery] string to)
    {
        if (!DateOnly.TryParse(from, out var start) || !DateOnly.TryParse(to, out var end) || start > end)
            return BadRequest(new { success=false, message="Invalid date range." });
        await using var db = await _dbFactory.CreateDbContextAsync();
        var feature = await db.FeatureSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
        if (feature != null && User.IsInRole("Admin") && !User.IsInRole("SuperAdmin") && !feature.AdminCanViewAttendance)
            return Forbid();
        var rows = await db.DailySummaries.AsNoTracking().Where(x => x.ShiftDate >= start && x.ShiftDate <= end).ToListAsync();
        var result = new CompanySummaryDto {
            TotalEmployeesProcessed=rows.Select(x=>x.EmployeeID).Distinct().Count(),
            TotalScheduledMinutes=rows.Sum(x=>x.ScheduledShiftDuration.TotalMinutes),
            TotalWorkedHours=rows.Sum(x=>x.EarnedStandardHours + (decimal)x.TotalOvertimeDuration.TotalHours),
            TotalOvertimeMinutes=rows.Sum(x=>x.TotalOvertimeDuration.TotalMinutes),
            TotalPenaltyMinutes=rows.Sum(x=>x.TotalPenaltyDuration.TotalMinutes),
            TotalLatenessMinutes=rows.Sum(x=>x.TotalLateness.TotalMinutes),
            TotalBreakPenaltyMinutes=rows.Sum(x=>x.TotalBreakPenalty.TotalMinutes)
        };
        return Ok(new { success=true, summary=result });
    }

    public sealed class DailyRowDto { public int EmployeeID{get;set;} public string EmployeeName{get;set;}=""; public string Date{get;set;}=""; public string Status{get;set;}="Absent"; public decimal WorkedHours{get;set;} public double OvertimeMinutes{get;set;} public double PenaltyMinutes{get;set;} public double LatenessMinutes{get;set;} public double BreakPenaltyMinutes{get;set;} public double ScheduledMinutes{get;set;} public string Punches{get;set;}=""; }
    public sealed class CompanySummaryDto { public int TotalEmployeesProcessed{get;set;} public double TotalScheduledMinutes{get;set;} public decimal TotalWorkedHours{get;set;} public double TotalOvertimeMinutes{get;set;} public double TotalPenaltyMinutes{get;set;} public double TotalLatenessMinutes{get;set;} public double TotalBreakPenaltyMinutes{get;set;} }
}
