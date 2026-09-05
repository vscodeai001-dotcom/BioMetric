using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

/// <summary>
/// HTTP API endpoint for employee GPS location updates.
/// 
/// This endpoint handles GPS location data sent directly from the browser's
/// JavaScript GPS tracker when the Blazor circuit is disconnected or unavailable.
/// 
/// Used for:
/// - Background GPS tracking (tab inactive)
/// - Network recovery (offline -> online)
/// - Circuit disconnection recovery
/// 
/// The JavaScript GPS tracker will use this endpoint as a fallback when
/// JSInterop callbacks to Blazor are not available.
/// </summary>
[ApiController]
[Route("api/employee-location")]
[Authorize]
public sealed class EmployeeLocationController : ControllerBase
{
    private readonly GeoLocationService _geoLocationService;
    private readonly ILogger<EmployeeLocationController> _logger;

    public EmployeeLocationController(
        GeoLocationService geoLocationService,
        ILogger<EmployeeLocationController> logger)
    {
        _geoLocationService = geoLocationService;
        _logger = logger;
    }

    /// <summary>
    /// Receive GPS location update from employee's browser.
    /// 
    /// This is called by the JavaScript GPS tracker when:
    /// 1. Blazor circuit is disconnected
    /// 2. JSInterop callback fails
    /// 3. Browser has lost connection to server
    /// 4. Tab is inactive (background tracking)
    /// 
    /// The endpoint updates the in-memory location store and database session.
    /// </summary>
    [HttpPost("update")]
    public async Task<IActionResult> UpdateLocation(
        [FromBody] LocationUpdateRequest request)
    {
        if (request == null)
        {
            return BadRequest("Location data is required.");
        }

        try
        {
            // ============================================================
            // VALIDATE REQUEST
            // ============================================================

            if (request.EmployeeId <= 0)
            {
                return BadRequest("EmployeeId must be positive.");
            }

            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                return BadRequest("SessionId is required.");
            }

            if (!Guid.TryParse(request.SessionId, out var sessionId) ||
                sessionId == Guid.Empty)
            {
                return BadRequest("SessionId must be a valid GUID.");
            }

            if (!double.IsFinite(request.Latitude) ||
                !double.IsFinite(request.Longitude))
            {
                return BadRequest("Latitude and Longitude must be valid numbers.");
            }

            if (request.Latitude < -90 || request.Latitude > 90 ||
                request.Longitude < -180 || request.Longitude > 180)
            {
                return BadRequest("Coordinates out of valid range.");
            }

            var accuracy = request.Accuracy >= 0 ? request.Accuracy : 0;

            // ============================================================
            // GET DISTANCE FROM OFFICE
            // ============================================================

            var distanceResult =
                await _geoLocationService.GetDistanceFromOfficeAsync(
                    request.Latitude,
                    request.Longitude);

            if (!distanceResult.Success)
            {
                _logger.LogWarning(
                    "GPS location validation failed. " +
                    "EmployeeId={EmployeeId}, " +
                    "Message={Message}",
                    request.EmployeeId,
                    distanceResult.Message);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "Unable to validate location.");
            }

            // ============================================================
            // UPDATE IN-MEMORY LOCATION STORE
            // ============================================================

            var liveUpdated = LiveLocationStore.Update(
                request.EmployeeId,
                request.Latitude,
                request.Longitude,
                accuracy,
                distanceResult.DistanceMeters,
                distanceResult.AllowedRadiusMeters,
                distanceResult.IsWithinAllowedRadius,
                sessionId);

            if (!liveUpdated)
            {
                _logger.LogWarning(
                    "GPS update rejected. Session mismatch. " +
                    "EmployeeId={EmployeeId}, " +
                    "SessionId={SessionId}",
                    request.EmployeeId,
                    sessionId);

                return StatusCode(
                    StatusCodes.Status409Conflict,
                    "Session ID mismatch or expired.");
            }

            // ============================================================
            // UPDATE DATABASE GPS SESSION
            // ============================================================

            try
            {
                await _geoLocationService.UpdateGpsSessionAsync(
                    request.EmployeeId,
                    sessionId,
                    request.Latitude,
                    request.Longitude,
                    accuracy,
                    distanceResult.DistanceMeters,
                    distanceResult.AllowedRadiusMeters,
                    distanceResult.IsWithinAllowedRadius);
            }
            catch (Exception sessionEx)
            {
                _logger.LogError(
                    sessionEx,
                    "GPS session statistics update failed. " +
                    "EmployeeId={EmployeeId}",
                    request.EmployeeId);

                // Continue anyway - location store is already updated
            }

            // ============================================================
            // SAVE LOCATION HISTORY (Optional - less frequent than updates)
            // ============================================================

            // Note: You may want to throttle this to every 10-30 seconds
            // to avoid excessive database writes. The JavaScript GPS tracker
            // already sends updates every 5 seconds minimum, but the
            // Blazor component only saves history every 10 seconds.
            
            // For now, we'll save on every API call. Adjust as needed.

            try
            {
                await _geoLocationService.SaveLocationHistoryAsync(
                    request.EmployeeId,
                    sessionId,
                    request.Latitude,
                    request.Longitude,
                    distanceResult.DistanceMeters,
                    distanceResult.AllowedRadiusMeters,
                    distanceResult.IsWithinAllowedRadius,
                    accuracy);
            }
            catch (Exception historyEx)
            {
                _logger.LogError(
                    historyEx,
                    "GPS history save failed. " +
                    "EmployeeId={EmployeeId}",
                    request.EmployeeId);

                // Continue anyway - session update succeeded
            }

            _logger.LogInformation(
                "GPS location updated via HTTP API. " +
                "EmployeeId={EmployeeId}, " +
                "Lat={Latitude}, " +
                "Lon={Longitude}, " +
                "Distance={Distance}m",
                request.EmployeeId,
                Math.Round(request.Latitude, 6),
                Math.Round(request.Longitude, 6),
                Math.Round(distanceResult.DistanceMeters, 1));

            return Ok(new LocationUpdateResponse
            {
                Success = true,
                Message = "Location updated successfully.",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GPS location update failed. " +
                "EmployeeId={EmployeeId}",
                request.EmployeeId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "An error occurred while processing the location update.");
        }
    }

    // ============================================================
    // REQUEST / RESPONSE MODELS
    // ============================================================

    public class LocationUpdateRequest
    {
        /// <summary>
        /// Employee ID from database (from JWT claim or session)
        /// </summary>
        public int EmployeeId { get; set; }

        /// <summary>
        /// Unique GPS session identifier (UUID)
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Latitude coordinate (WGS84)
        /// </summary>
        public double Latitude { get; set; }

        /// <summary>
        /// Longitude coordinate (WGS84)
        /// </summary>
        public double Longitude { get; set; }

        /// <summary>
        /// GPS accuracy in meters (reported by browser)
        /// </summary>
        public double Accuracy { get; set; }

        /// <summary>
        /// Timestamp when location was captured
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class LocationUpdateResponse
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
