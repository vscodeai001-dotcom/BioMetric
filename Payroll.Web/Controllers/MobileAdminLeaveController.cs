using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.Shared.Data;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

[ApiController]
[Route("api/mobile/admin/leaves")]
[Authorize(Roles = "Admin,SuperAdmin")]
public sealed class MobileAdminLeaveController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _db;
    private readonly LeaveManagementService _leave;
    public MobileAdminLeaveController(IDbContextFactory<AppDbContext> db, LeaveManagementService leave)
    { _db = db; _leave = leave; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminLeaveDto>>> Get([FromQuery] int employeeId = 0, [FromQuery] string status = "Pending", [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        await using var db = await _db.CreateDbContextAsync();
        var employees = await db.Employees.AsNoTracking().Where(e => !e.IsDeleted).ToDictionaryAsync(e => e.EmployeeID, e => e.Name);
        var rows = await _leave.LoadLeaveRequestsAsync(employeeId, status, from, to);
        return Ok(rows.Select(x => new AdminLeaveDto(x.LeaveRequestID, x.EmployeeID, employees.GetValueOrDefault(x.EmployeeID, "Employee"), x.LeaveDate, x.LeaveType, x.IsHalfDay, x.IsApproved, x.Notes)));
    }

    [HttpPost]
    public async Task<ActionResult<AdminLeaveDto>> Create([FromBody] CreateAdminLeaveRequest request)
    {
        if (request.EmployeeId <= 0 || !DateTime.TryParse(request.LeaveDate, out var date)) return BadRequest(new { message = "Employee and valid leave date are required." });
        var entity = new LeaveRequest { EmployeeID = request.EmployeeId, LeaveDate = date.Date, LeaveType = string.IsNullOrWhiteSpace(request.LeaveType) ? "Paid Leave" : request.LeaveType, IsHalfDay = request.IsHalfDay, Notes = request.Notes };
        await _leave.SaveNewLeaveRequestAsync(entity);
        await using var db = await _db.CreateDbContextAsync();
        var name = await db.Employees.AsNoTracking().Where(e => e.EmployeeID == request.EmployeeId).Select(e => e.Name).FirstOrDefaultAsync() ?? "Employee";
        return Ok(new AdminLeaveDto(entity.LeaveRequestID, entity.EmployeeID, name, entity.LeaveDate, entity.LeaveType, entity.IsHalfDay, entity.IsApproved, entity.Notes));
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> Status(int id, [FromBody] LeaveStatusRequest request)
    { await _leave.UpdateLeaveStatusAsync(id, request.Approved); return Ok(new { success = true }); }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    { await _leave.DeleteLeaveRequestAsync(id); return Ok(new { success = true }); }

    public sealed record CreateAdminLeaveRequest(int EmployeeId, string LeaveDate, string LeaveType, bool IsHalfDay, string? Notes);
    public sealed record LeaveStatusRequest(bool Approved, string? Remarks);
    public sealed record AdminLeaveDto(int Id, int EmployeeId, string EmployeeName, DateTime? LeaveDate, string LeaveType, bool IsHalfDay, bool Approved, string? Notes);
}
