using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.Shared.Data;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

[ApiController]
[Route("api/mobile/admin/shifts")]
[Authorize(Roles = "Admin,SuperAdmin")]
public sealed class MobileAdminShiftController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _db;
    private readonly RosteringService _rostering;
    public MobileAdminShiftController(IDbContextFactory<AppDbContext> db, RosteringService rostering) { _db = db; _rostering = rostering; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminShiftDto>>> Get([FromQuery] int employeeId = 0, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        await using var db = await _db.CreateDbContextAsync();
        var q = db.ShiftSchedules.AsNoTracking().AsQueryable();
        if (employeeId > 0) q = q.Where(s => s.EmployeeID == employeeId);
        if (DateOnly.TryParse(from, out var f)) q = q.Where(s => s.IsRecurringPattern || s.ShiftDate >= f);
        if (DateOnly.TryParse(to, out var t)) q = q.Where(s => s.IsRecurringPattern || s.ShiftDate <= t);
        var employees = await db.Employees.AsNoTracking().Where(e => !e.IsDeleted).ToDictionaryAsync(e => e.EmployeeID, e => e.Name);
        var rows = await q.OrderBy(s => s.ShiftDate).ThenBy(s => s.EmployeeID).ThenBy(s => s.StartTime).ToListAsync();
        return Ok(rows.Select(s => new AdminShiftDto(s.ScheduleID, s.EmployeeID, employees.GetValueOrDefault(s.EmployeeID, "Employee"), s.ShiftDate.ToString("yyyy-MM-dd"), s.StartTime.ToString("HH:mm"), s.EndTime.ToString("HH:mm"), s.IsRecurringPattern, s.PatternDurationDays, (int)s.AppliesToDayOfWeek)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShiftRequest r)
    {
        if (!DateOnly.TryParse(r.ShiftDate, out var date) || !TimeOnly.TryParse(r.StartTime, out var start) || !TimeOnly.TryParse(r.EndTime, out var end)) return BadRequest(new { message = "Invalid date or time." });
        await using var db = await _db.CreateDbContextAsync();
        if (await db.ShiftSchedules.AnyAsync(s => s.EmployeeID == r.EmployeeId && s.ShiftDate == date)) return Conflict(new { message = "A shift already exists for this employee and date." });
        db.ShiftSchedules.Add(new ShiftSchedule { EmployeeID = r.EmployeeId, ShiftDate = date, StartTime = start, EndTime = end, IsRecurringPattern = r.IsRecurringPattern, PatternDurationDays = r.PatternDurationDays, AppliesToDayOfWeek = date.DayOfWeek });
        await db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    { await using var db = await _db.CreateDbContextAsync(); var s = await db.ShiftSchedules.FindAsync(id); if (s == null) return NotFound(); db.ShiftSchedules.Remove(s); await db.SaveChangesAsync(); return Ok(new { success = true }); }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate()
    { var start = DateOnly.FromDateTime(DateTime.Now.Date); var count = await _rostering.GenerateScheduleFromPatternsAsync(start, start.AddDays(30)); return Ok(new { success = true, count }); }

    public sealed record CreateShiftRequest(int EmployeeId, string ShiftDate, string StartTime, string EndTime, bool IsRecurringPattern, int PatternDurationDays = 7);
    public sealed record AdminShiftDto(int Id, int EmployeeId, string EmployeeName, string ShiftDate, string StartTime, string EndTime, bool IsRecurringPattern, int PatternDurationDays, int DayOfWeek);
}
