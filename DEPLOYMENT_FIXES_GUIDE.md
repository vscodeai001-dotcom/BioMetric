# Biometric Payroll - Deployment & Configuration Guide

## Overview

This guide covers the implementation of five critical fixes for the Biometric Payroll application:

1. **Real-time GPS Location Updates** (SignalR)
2. **GPS Session Deduplication** (Prevent multiple sessions)
3. **Employee Single-Device Login** (Already implemented)
4. **Persistent Data Protection Keys** (Production containers)
5. **Health Checks & Graceful Shutdown** (Container orchestration)

---

## 🔴 ISSUE 1: GPS Location Not Live

### Problem
Admin map requires manual refresh to see employee location updates. No automatic real-time updates.

### Solution Implemented
- Added SignalR integration to `GeoLocationService`
- Location updates are now broadcast immediately via SignalR
- Admin clients receive real-time updates without polling

### Files Modified
- `Payroll.Web/Services/GeoLocationService.cs` - Added SignalR `IHubContext<AttendanceRefreshHub>` injection
- Location updates now broadcast to all connected clients via `LocationChanged` signal

### How It Works
```
Employee GPS Point
    ↓
LiveLocationStore (in-memory update)
    ↓
SignalR Broadcast
    ↓
Admin Map (auto-updates)
```

### Testing
1. Open admin map in browser A
2. Employee updates GPS location in browser B
3. Admin map in browser A updates automatically within 1-2 seconds
4. No manual refresh required

---

## 🔴 ISSUE 2: Multiple GPS Sessions While Logged In

### Problem
Multiple GPS sessions created for single login (sessions: 1731a02a, 955ffc4d, 188e6ebf)

### Root Causes
1. Blazor circuit reconnects triggered component re-initialization
2. Component lifecycle creating duplicate sessions
3. No deduplication logic for circuit reconnects

### Solution Implemented
- Enhanced `EmployeeGpsTracker` component with circuit reconnect detection
- Added `reconnectAttempts` counter to track restart attempts
- Improved logging to diagnose session creation issues
- Component now properly reuses existing session on reconnect

### Files Modified
- `Payroll.Web/Components/UI/Attendance/EmployeeGpsTracker.razor`
- Added reconnect attempt tracking
- Enhanced logging for session creation/reuse

### Technical Details

**Session Lifecycle:**
```
Login
  ↓
StartTracking() - Checks browser localStorage for existing sessionId
  ↓
If sessionId exists:
  → Try to resume existing database session
  → If timed out: Create new sessionId
  → If active: Reuse session
  ↓
GPS watcher runs
  ↓
Logout: Call StopTracking() → Explicitly end session
  ↓
Circuit Dispose: Do NOT end session (allow timeout)
```

**Circuit Reconnect Handling:**
- Browser localStorage stores sessionId (survives page close)
- On reconnect, component retrieves stored sessionId from browser
- Checks if database session still active (within 2 min timeout)
- Reuses session if active, creates new only if timed out
- Logs all reconnect attempts for diagnostics

### Monitoring

Check logs for:
```
"GPS session started" - New session created
"Stored GPS session has ended. Creating a new session" - Session reused then replaced
"Marked X GPS sessions as timed out" - Cleanup of abandoned sessions
"ReconnectAttempt=N" - Circuit reconnection detected
```

---

## 🟡 ISSUE 3: Employee Multi-Device Login

### Status
**ALREADY IMPLEMENTED** - No changes required

Uses `EmployeeSingleSessionSignInManager` to enforce:
- One employee = one active device
- New device login invalidates previous session
- Database lock mechanism via `EmployeeDeviceLocks` table

### Verification
1. Login on Device A ✓
2. Login same account on Device B
3. Device B should show message: "This account is already logged in on another device"
4. Employee can force logout or wait 30 seconds for automatic invalidation
5. Then Device B login succeeds

---

## 🟠 ISSUE 4: Data Protection Keys Not Persistent

### Problem
ASP.NET Core Data Protection keys stored in ephemeral `/root/.aspnet/DataProtection-Keys`
- Container restart = key loss
- Authentication cookies become invalid
- All sessions are invalidated

### Solution Implemented

#### Application Configuration (`Program.cs`)
```csharp
var dataProtectionPath = Environment.GetEnvironmentVariable("DATA_PROTECTION_PATH") ?? 
                        "/data/dataprotection";

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
```

#### Docker Configuration (`Dockerfile`)
```dockerfile
ENV DATA_PROTECTION_PATH=/data/dataprotection

# Create persistent directory
RUN mkdir -p /data/dataprotection && chmod 755 /data/dataprotection
```

### Deployment Configuration

#### For Render Platform
1. In Render dashboard:
   - Go to Web Service → Disks
   - Add persistent disk
   - Mount path: `/data`
   - Size: 1 GB minimum

2. Set environment variable:
   ```
   DATA_PROTECTION_PATH=/data/dataprotection
   ```

3. Container automatically creates `/data/dataprotection` on startup

#### For Docker Compose
```yaml
version: '3.8'
services:
  payroll-web:
    image: payroll:latest
    volumes:
      - payroll-data:/data
    environment:
      DATA_PROTECTION_PATH: /data/dataprotection
      DATABASE_URL: "Host=postgres;..."

volumes:
  payroll-data:
    driver: local
```

#### For Local Docker
```bash
docker run \
  -v /local/persistent/path:/data \
  -e DATA_PROTECTION_PATH=/data/dataprotection \
  payroll:latest
```

### Impact
- ✅ Keys survive container restart
- ✅ User sessions remain valid across deployments
- ✅ Authentication cookies not invalidated
- ✅ Supports zero-downtime deployments

### Security Notes
- Keys in persistent storage are protected by filesystem permissions
- For production: Consider additional encryption
- Keys directory owned by container user (automatic)
- Read/Write permissions: 755 (user only)

---

## 🟡 ISSUE 5: Container Stopping/Restarting

### Problem
Application shutting down without graceful handling
- Render: "Application is shutting down..."
- Hangfire: Caught stopping signal
- GPS sessions not properly closed
- Background jobs interrupted

### Solution Implemented

#### 1. Health Check Endpoint
```csharp
app.MapHealthChecks("/health", new HealthCheckOptions { ... });
```

**Endpoint:** `GET /health`
**Response:** JSON with status and database health

```json
{
  "status": "Healthy",
  "checks": {
    "database": {
      "status": "Healthy",
      "description": null
    }
  }
}
```

**Render Configuration:**
- Go to Web Service → Health Check
- Enable health check
- Endpoint: `/health`
- Period: 30s
- Timeout: 10s
- Failure threshold: 3

#### 2. Graceful Shutdown Handling
```csharp
hostApplicationLifetime.ApplicationStopping.Register(() => {
    // Notify SignalR clients
    // Allow jobs to complete
    // Close database connections
});
```

### Deployment Configuration

#### Render Platform
1. Settings → Health Check
   - Enable: ✓
   - Endpoint: `/health`
   - Interval: 30s
   - Timeout: 10s
   - Start period: 60s

2. Settings → Restart Policy
   - On failure: Automatic
   - Max retries: 3

3. Dockerfile HEALTHCHECK
```dockerfile
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:${PORT:-10000}/health || exit 1
```

#### Docker Compose
```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:10000/health"]
  interval: 30s
  timeout: 10s
  retries: 3
  start_period: 60s
```

### Graceful Shutdown Flow
1. Container receives SIGTERM
2. Application detects shutdown signal
3. Notifies all SignalR clients: "ServerShuttingDown"
4. Allows 5 seconds for client disconnect
5. Completes active requests
6. Closes database connections
7. Exits cleanly

### Monitoring
Check logs for:
```
"Application shutdown initiated"
"SignalR clients notified of shutdown"
"Application has shut down successfully"
```

---

## 🔧 Deployment Checklist

### Pre-Deployment
- [ ] Rebuild solution: `dotnet build -c Release`
- [ ] All tests passing
- [ ] Database migrations reviewed
- [ ] Backup existing database

### Docker Image Build
```bash
docker build -t payroll:v1.0 .
```

### Render Deployment

#### Environment Variables
```
DATA_PROTECTION_PATH=/data/dataprotection
DATABASE_URL=<your_database_url>
ASPNETCORE_ENVIRONMENT=Production
```

#### Persistent Disk
- Mount path: `/data`
- Size: 1 GB (for data protection keys)

#### Health Check
- Endpoint: `/health`
- Status: Healthy (green indicator)

#### Verify Post-Deployment
1. Check health endpoint: `curl https://your-app.onrender.com/health`
2. Login and verify sessions work
3. Logout and verify GPS stops
4. Check logs for warnings about Data Protection

### Post-Deployment Verification

1. **Data Protection Keys**
   - Check container: `docker exec <container> ls -la /data/dataprotection/`
   - Should see XML key files after first request

2. **Health Check**
   - Run: `curl http://localhost:10000/health`
   - Verify response includes database status

3. **GPS Real-time**
   - Open admin map
   - Employee updates GPS
   - Map updates without refresh within 1-2 seconds

4. **GPS Sessions**
   - Check database: `SELECT * FROM employee_gps_sessions ORDER BY started_at_utc DESC`
   - Verify only one active session per employee
   - Previous sessions have end times

5. **Graceful Shutdown**
   - Stop container: `docker stop <container>`
   - Verify logs show shutdown messages
   - Restart: `docker start <container>`
   - Database connections clean
   - No leaked sessions

---

## 📊 Monitoring & Logs

### Key Log Messages

**GPS Real-time Updates**
```
GPS watcher initialized. EmployeeId=123, SessionId=abc-def...
GPS session started. EmployeeId=123, SessionId=abc-def...
Marked 1 GPS sessions as timed out.
```

**Data Protection**
```
Data Protection key storage configuration successful
Data Protection keys persisted at /data/dataprotection
```

**Health Check**
```
Database health check successful
```

**Graceful Shutdown**
```
Application shutdown initiated
SignalR clients notified of shutdown
Application has shut down successfully
```

### Metrics to Monitor
- Active GPS sessions
- SignalR connection count
- Health check success rate
- Database connection pool health
- Container restart count
- Deployment duration

---

## 🆘 Troubleshooting

### GPS Sessions Keep Multiplying
**Symptom:** Multiple sessions created during single login
**Solution:**
1. Check browser localStorage is working
2. Verify circuit reconnect logic in logs
3. Ensure GPS watcher not restarting externally
4. Review Blazor circuit configuration

### Health Check Failing
**Symptom:** `curl /health` returns 503
**Solution:**
1. Verify database connectivity
2. Check database migrations completed
3. Review database credentials
4. Check database is not locked

### Data Protection Keys Lost
**Symptom:** Sessions invalid after container restart
**Solution:**
1. Verify `/data` volume mounted
2. Check directory permissions
3. Ensure `DATA_PROTECTION_PATH` environment variable set
4. Rebuild with new keys (users re-login)

### GPS Real-time Not Working
**Symptom:** Admin map requires manual refresh
**Solution:**
1. Verify SignalR WebSocket connection
2. Check browser console for JavaScript errors
3. Verify `/hubs/attendance-refresh` endpoint accessible
4. Review GeoLocationService logs

---

## 📝 Files Modified

1. **Payroll.Web/Services/GeoLocationService.cs**
   - Added SignalR IHubContext injection
   - Location broadcasts on update/session start/session end

2. **Payroll.Web/Program.cs**
   - Data Protection configuration
   - Health checks registration
   - Graceful shutdown handlers
   - SignalR using statements

3. **Payroll.Web/Payroll.Web.csproj**
   - Added Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore

4. **Payroll.Web/Components/UI/Attendance/EmployeeGpsTracker.razor**
   - Enhanced circuit reconnect handling
   - Improved logging for session lifecycle

5. **Dockerfile**
   - Data Protection path setup
   - Health check configuration
   - Persistent directory creation

6. **Payroll.Web/Services/AttendanceRefreshService.cs**
   - Already contains NotifyLocationChangedAsync (no changes needed)

---

## ✅ Verification Checklist

After deployment, verify:

- [ ] Application starts without errors
- [ ] Health check endpoint responds with status
- [ ] Data Protection keys created in persistent storage
- [ ] GPS real-time updates working (admin map auto-updates)
- [ ] Single GPS session per login (no duplicates)
- [ ] Employee single-device login enforced
- [ ] Container gracefully handles restart
- [ ] Authentication sessions survive container restart
- [ ] Logs show appropriate startup messages
- [ ] SignalR connections established
- [ ] Database migrations successful

---

## 🔗 Related Documentation

- [ASP.NET Core Data Protection](https://docs.microsoft.com/en-us/aspnet/core/security/data-protection/introduction)
- [ASP.NET Core Health Checks](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [SignalR Documentation](https://docs.microsoft.com/en-us/aspnet/core/signalr/introduction)
- [Render Persistent Disks](https://render.com/docs/persistent-disks)
- [Docker Health Checks](https://docs.docker.com/engine/reference/builder/#healthcheck)

---

**Version:** 1.0  
**Date:** 2026-08-29  
**Status:** Ready for Production Deployment
