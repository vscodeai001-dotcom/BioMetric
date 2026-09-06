using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payroll.Shared.Data;

namespace Payroll.Web.Security;

public sealed class MobileTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly MobileEmployeeTokenService _tokens;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public MobileTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        MobileEmployeeTokenService tokens,
        IDbContextFactory<AppDbContext> dbFactory)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
        _dbFactory = dbFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values))
            return AuthenticateResult.NoResult();

        var header = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        if (!_tokens.TryRead(token, out var payload))
            return AuthenticateResult.Fail("Invalid or expired mobile session.");

        await using var db = await _dbFactory.CreateDbContextAsync(Context.RequestAborted);
        var lockRecord = await db.EmployeeDeviceLocks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == payload.UserId, Context.RequestAborted);

        if (lockRecord == null ||
            !string.Equals(lockRecord.DeviceId, payload.DeviceId, StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("Mobile session is no longer active on this device.");
        }

        var employee = await db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeID == payload.EmployeeId && !x.IsDeleted, Context.RequestAborted);

        if (employee == null || !string.Equals(employee.AspNetUserId, payload.UserId, StringComparison.Ordinal))
            return AuthenticateResult.Fail("Employee session is invalid.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, payload.UserId),
            new Claim(ClaimTypes.Name, employee.Name),
            new Claim(ClaimTypes.Role, "Employee"),
            new Claim("employee_id", payload.EmployeeId.ToString()),
            new Claim("device_id", payload.DeviceId),
            new Claim("mobile_session", "true")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
