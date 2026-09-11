using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.Shared.Data;
using Payroll.Shared.Services;
using Payroll.Web.Services;
using System.Globalization;

namespace Payroll.Web.Controllers;

[ApiController]
[Route("api/mobile/admin/punches")]
[Authorize(AuthenticationSchemes = "MobileBearer", Roles = "Admin,SuperAdmin")]
public sealed class MobileAdminPunchController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AttendanceCalculatorService _calculator;
    private readonly PayrollLockService _lockService;
    private readonly AuditService _audit;
    private readonly AttendanceRefreshService _refresh;

    public MobileAdminPunchController(IDbContextFactory<AppDbContext> dbFactory, AttendanceCalculatorService calculator,
        PayrollLockService lockService, AuditService audit, AttendanceRefreshService refresh)
    { _dbFactory = dbFactory; _calculator = calculator; _lockService = lockService; _audit = audit; _refresh = refresh; }

    [HttpGet("issues")]
    public async Task<IActionResult> Issues([FromQuery] int employeeId = 0, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) start = DateTime.Today.AddDays(-7);
        if (!DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) end = DateTime.Today;
        if (end < start) return BadRequest(new { success = false, message = "Invalid date range." });
        await using var db = await _dbFactory.CreateDbContextAsync();
        var employees = await db.Employees.AsNoTracking().Where(e => !e.IsDeleted && (employeeId <= 0 || e.EmployeeID == employeeId)).ToListAsync();
        var logs = await db.AttendanceLogs.AsNoTracking().Where(x => x.EmployeeID.HasValue && x.PunchTime >= start.Date && x.PunchTime < end.Date.AddDays(1)).OrderBy(x => x.PunchTime).ToListAsync();
        var result = new List<IssueDayDto>();
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek == DayOfWeek.Sunday) continue;
            foreach (var emp in employees)
            {
                var punches = logs.Where(x => x.EmployeeID == emp.EmployeeID && x.PunchTime.Date == day).ToList();
                if (punches.Count == 0 || punches.Count % 2 != 0)
                    result.Add(new IssueDayDto(emp.EmployeeID, emp.Name, day.ToString("yyyy-MM-dd"), punches.Select(ToDto).ToList()));
            }
        }
        return Ok(result.OrderBy(x => x.Date).ThenBy(x => x.EmployeeName));
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.AttendanceLogs.AsNoTracking().Where(x => !x.IsApproved && x.LogType == "Correction Request" && x.EmployeeID.HasValue).OrderBy(x => x.PunchTime).ToListAsync();
        var names = await db.Employees.AsNoTracking().ToDictionaryAsync(x => x.EmployeeID, x => x.Name);
        return Ok(rows.Select(x => new PendingPunchDto(x.LogID, x.EmployeeID!.Value, names.TryGetValue(x.EmployeeID.Value, out var n) ? n : $"Employee #{x.EmployeeID}", x.PunchTime.ToString("yyyy-MM-dd HH:mm:ss"), x.LogType ?? "Correction Request", x.DeviceID ?? "")));
    }

    [HttpPost("manual")]
    public async Task<IActionResult> Add([FromBody] PunchMutationRequest request)
    {
        if (request.EmployeeId <= 0 || !DateTime.TryParseExact(request.PunchTime, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var punchTime)) return BadRequest(new { success=false, message="Invalid employee or punch time." });
        if (await _lockService.IsLockedAsync(request.EmployeeId, punchTime)) return Conflict(new { success=false, message="Payroll is finalized for this month. Cannot modify punches." });
        await using var db = await _dbFactory.CreateDbContextAsync();
        var log = new AttendanceLog { EmployeeID=request.EmployeeId, PunchTime=punchTime, DeviceID="ManualCorrection", LogType="Manual Correction", IsApproved=true };
        db.AttendanceLogs.Add(log); await db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "AttendanceLog", log.LogID.ToString(), $"Manual punch added at {punchTime:HH:mm:ss} for EmpID: {request.EmployeeId}");
        await Recalculate(db, request.EmployeeId, punchTime.Date);
        await _refresh.NotifyAttendanceChangedAsync(request.EmployeeId, DateOnly.FromDateTime(punchTime.Date));
        return Ok(new { success=true, id=log.LogID });
    }

    [HttpPost("manual/full-day")]
    public async Task<IActionResult> AddFullDay([FromBody] FullDayPunchRequest request)
    {
        if (request.EmployeeId <= 0 || !DateTime.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return BadRequest(new { success=false, message="Invalid employee or date." });
        var start = TimeOnly.TryParseExact(request.StartTime ?? "09:00", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ? s : new TimeOnly(9,0);
        var end = TimeOnly.TryParseExact(request.EndTime ?? "18:00", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e : new TimeOnly(18,0);
        if (await _lockService.IsLockedAsync(request.EmployeeId, day)) return Conflict(new { success=false, message="Payroll is finalized for this month. Cannot modify punches." });
        await using var db = await _dbFactory.CreateDbContextAsync();
        var a = new AttendanceLog { EmployeeID=request.EmployeeId, PunchTime=day.Date.Add(start.ToTimeSpan()), DeviceID="ManualCorrection", LogType="Manual Correction", IsApproved=true };
        var b = new AttendanceLog { EmployeeID=request.EmployeeId, PunchTime=day.Date.Add(end.ToTimeSpan()), DeviceID="ManualCorrection", LogType="Manual Correction", IsApproved=true };
        db.AttendanceLogs.AddRange(a,b); await db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "AttendanceLog", $"{a.LogID},{b.LogID}", $"Quick Added {start:hh\\:mm tt}-{end:hh\\:mm tt} punches for EmpID: {request.EmployeeId}");
        await Recalculate(db, request.EmployeeId, day.Date); await _refresh.NotifyAttendanceChangedAsync(request.EmployeeId, DateOnly.FromDateTime(day.Date));
        return Ok(new { success=true });
    }

    [HttpPut("manual/{logId:int}")]
    public async Task<IActionResult> Edit(int logId, [FromBody] EditPunchRequest request)
    {
        if (!TimeOnly.TryParseExact(request.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return BadRequest(new { success=false, message="Invalid time." });
        await using var db = await _dbFactory.CreateDbContextAsync(); var log = await db.AttendanceLogs.FindAsync(logId);
        if (log == null || !log.EmployeeID.HasValue) return NotFound(new { success=false, message="Punch record not found." });
        if (await _lockService.IsLockedAsync(log.EmployeeID.Value, log.PunchTime)) return Conflict(new { success=false, message="Payroll is finalized for this month. Cannot modify punches." });
        var day = log.PunchTime.Date; log.PunchTime = day.Add(t.ToTimeSpan()); await db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE", "AttendanceLog", log.LogID.ToString(), $"Punch time updated to {t:HH:mm:ss} for EmpID: {log.EmployeeID}");
        await Recalculate(db, log.EmployeeID.Value, day); await _refresh.NotifyAttendanceChangedAsync(log.EmployeeID.Value, DateOnly.FromDateTime(day)); return Ok(new { success=true });
    }

    [HttpDelete("manual/{logId:int}")]
    public async Task<IActionResult> Delete(int logId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(); var log = await db.AttendanceLogs.FindAsync(logId);
        if (log == null || !log.EmployeeID.HasValue) return NotFound(new { success=false, message="Punch record not found." });
        if (await _lockService.IsLockedAsync(log.EmployeeID.Value, log.PunchTime)) return Conflict(new { success=false, message="Payroll is finalized for this month. Cannot delete punches." });
        var emp = log.EmployeeID.Value; var day = log.PunchTime.Date; var id=log.LogID; var time=log.PunchTime; db.AttendanceLogs.Remove(log); await db.SaveChangesAsync();
        await _audit.LogAsync("DELETE", "AttendanceLog", id.ToString(), $"Punch deleted at {time:HH:mm:ss} for EmpID: {emp}"); await Recalculate(db, emp, day); await _refresh.NotifyAttendanceChangedAsync(emp, DateOnly.FromDateTime(day)); return Ok(new { success=true });
    }

    [HttpPost("pending/{logId:int}/approve")]
    public Task<IActionResult> Approve(int logId) => SetPending(logId, true);
    [HttpPost("pending/{logId:int}/reject")]
    public Task<IActionResult> Reject(int logId) => SetPending(logId, false);

    private async Task<IActionResult> SetPending(int logId, bool approve)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(); var log=await db.AttendanceLogs.FindAsync(logId);
        if (log == null || !log.EmployeeID.HasValue) return NotFound(new { success=false, message="Request not found." });
        var emp=log.EmployeeID.Value; var day=log.PunchTime.Date;
        if (approve) { log.IsApproved=true; await db.SaveChangesAsync(); await _audit.LogAsync("APPROVE", "AttendanceLog", log.LogID.ToString(), $"Punch correction approved for EmpID: {emp} at {log.PunchTime:hh:mm tt}"); await Recalculate(db,emp,day); await _refresh.NotifyAttendanceChangedAsync(emp,DateOnly.FromDateTime(day)); }
        else { db.AttendanceLogs.Remove(log); await db.SaveChangesAsync(); await _audit.LogAsync("REJECT/DELETE", "AttendanceLog", log.LogID.ToString(), $"Punch correction rejected and deleted for EmpID: {emp} at {log.PunchTime:hh:mm tt}"); await _refresh.NotifyAttendanceChangedAsync(emp,DateOnly.FromDateTime(day)); }
        return Ok(new { success=true });
    }

    private async Task Recalculate(AppDbContext db, int employeeId, DateTime day)
    {
        var emp=await db.Employees.AsNoTracking().FirstOrDefaultAsync(e=>e.EmployeeID==employeeId); if(emp==null)return;
        var settings=await db.CompanySettings.AsNoTracking().FirstOrDefaultAsync(x=>x.SettingID==1)??new CompanySetting();
        var features=await db.FeatureSettings.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==1)??new FeatureSettings();
        var holidays=await db.CompanyHolidays.Where(x=>x.HolidayDate==DateOnly.FromDateTime(day)).ToListAsync();
        var punches=await db.AttendanceLogs.Where(x=>x.EmployeeID==employeeId && x.PunchTime.Date==day.Date).ToListAsync();
        var schedule=await db.ShiftSchedules.FirstOrDefaultAsync(x=>x.EmployeeID==employeeId&&x.ShiftDate==DateOnly.FromDateTime(day));
        var leave=await db.LeaveRequests.FirstOrDefaultAsync(x=>x.EmployeeID==employeeId&&x.LeaveDate.HasValue&&x.LeaveDate.Value.Date==day.Date);
        var r=_calculator.CalculateDailyResult(emp,day,punches,leave,schedule,settings,holidays,features);
        var s=await db.DailySummaries.FirstOrDefaultAsync(x=>x.EmployeeID==employeeId&&x.ShiftDate==DateOnly.FromDateTime(day));
        if(s==null){s=new DailySummary{EmployeeID=employeeId,ShiftDate=DateOnly.FromDateTime(day)};db.DailySummaries.Add(s);}
        s.Status=r.Status;s.EarnedStandardHours=(decimal)r.EarnedStandardDuration.TotalHours;s.TotalOvertimeDuration=r.TotalOvertimeDuration;s.TotalPenaltyDuration=r.TotalPenalty;s.TotalLateness=r.TotalLateness;s.TotalBreakPenalty=r.TotalBreakPenalty;s.ScheduledShiftDuration=r.ScheduledShiftDuration;s.ShiftAllowanceEarned=r.ShiftAllowanceEarned;s.IsManualOverride=false;await db.SaveChangesAsync();
    }
    private static PunchDto ToDto(AttendanceLog x)=>new(x.LogID,x.EmployeeID??0,x.PunchTime.ToString("yyyy-MM-dd HH:mm:ss"),x.LogType??"",x.DeviceID??"",x.IsApproved);
    public sealed record IssueDayDto(int EmployeeId,string EmployeeName,string Date,List<PunchDto> Punches);
    public sealed record PunchDto(int Id,int EmployeeId,string PunchTime,string LogType,string DeviceId,bool IsApproved);
    public sealed record PendingPunchDto(int Id,int EmployeeId,string EmployeeName,string PunchTime,string LogType,string DeviceId);
    public sealed record PunchMutationRequest(int EmployeeId,string PunchTime);
    public sealed record FullDayPunchRequest(int EmployeeId,string Date,string? StartTime,string? EndTime);
    public sealed record EditPunchRequest(string Time);
}
