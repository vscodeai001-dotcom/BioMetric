using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Payroll.Web.Security;

public sealed class MobileEmployeeTokenService
{
    private const string Purpose = "BioMetric.MobileEmployeeToken.v1";
    private readonly IDataProtector _protector;

    public MobileEmployeeTokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Create(string userId, int employeeId, string deviceId, string role)
    {
        var payload = new MobileEmployeeTokenPayload
        {
            UserId = userId,
            EmployeeId = employeeId,
            DeviceId = deviceId,
            Role = role,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddYears(10).ToUnixTimeSeconds()
        };

        return _protector.Protect(JsonSerializer.Serialize(payload));
    }

    public bool TryRead(string token, out MobileEmployeeTokenPayload payload)
    {
        payload = new MobileEmployeeTokenPayload();
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var json = _protector.Unprotect(token);
            var parsed = JsonSerializer.Deserialize<MobileEmployeeTokenPayload>(json);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.UserId) ||
                parsed.EmployeeId < 0 || string.IsNullOrWhiteSpace(parsed.DeviceId))
                return false;

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= parsed.ExpiresAtUtc)
                return false;

            payload = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class MobileEmployeeTokenPayload
{
    public string UserId { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public long ExpiresAtUtc { get; set; }
}
