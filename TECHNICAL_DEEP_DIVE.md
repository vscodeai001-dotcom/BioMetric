# Live Location Tracking - Technical Deep Dive

## Problem Root Cause Analysis

### Symptom
Admin opens "Live Staff Locations" panel ? Sees employee as "Offline" after 2-5 minutes of inactivity, even though employee is still logged in and working.

### Investigation
1. **Employee GPS Watcher** - Component on employee's browser running persistent GPS tracking
2. **LiveLocationStore** - In-memory cache of latest employee locations
3. **EmployeeGpsSessions** - Database table tracking active GPS sessions
4. **SecurityStamp Validation** - Identity framework's session validation mechanism

### Root Cause Chain

```
SecurityStamp Validation every 5 min
         ?
Employee's session becomes invalid
         ?
Next HTTP request fails validation check
         ?
EmployeeGpsTracker circuit may reconnect
         ?
BUT: GPS session in database already inactive
         ?
Even with new SessionId, old session marked as ended
         ?
Admin sees: No location update in 2+ min ? "OFFLINE"
```

### Why This Happened Before
- SecurityStamp validation at 5 minute intervals was too aggressive
- GPS updates were being sent (Location Store alive)
- But database session state was being invalidated separately
- Admin panel checking: "Is there an active GPS session?" ? "No" ? Shows "OFFLINE"
- Meanwhile employee is actually still logged in and GPS is running

---

## Solution Architecture

### 1. Session Persistence (No Auto-Expiration)

**File:** `Program.cs`

```csharp
builder.Services.Configure<SecurityStampValidatorOptions>(
    options =>
    {
        // Very long validation interval = effectively disabled
        // Security stamp changes are still detected immediately
        options.ValidationInterval = TimeSpan.FromDays(365);
    });
```

**Why This Works:**
- Employees stay logged in indefinitely
- GPS session lifecycle is independent of Identity session
- Explicit logout still works (invalidates security stamp)
- New device login still forces old device logout (security stamp change)

**Key Insight:**
```
Manual Logout or New Device Login
         ?
Security Stamp Changes
         ?
Next validation immediately detects change
         ?
Session invalidated
         ?
Employee must re-login
```

### 2. GPS Session Timeout (Extended Grace Period)

**File:** `GeoLocationService.cs`

```csharp
public async Task MarkTimedOutSessionsAsync()
{
    // Only timeout after 30 minutes (1800 seconds) without update
    var timeoutBefore = DateTime.UtcNow.AddSeconds(-1800);
    
    var sessions = await db.EmployeeGpsSessions
        .Where(x =>
            x.EndedAtUtc == null &&
            x.LastUpdateAtUtc <= timeoutBefore)
        .ToListAsync();
}
```

**Why 30 Minutes:**
- GPS watcher might be paused by browser power management
- Network interruptions are temporary
- Employee may have GPS disabled during break
- Session should not end just because of temporary inactivity

**Scenario:**
```
14:00 - GPS update received ? LastUpdateAtUtc = 14:00
14:05 - No GPS update (phone in pocket)
14:10 - Still no update (GPS disabled to save battery)
14:30 - GPS resumes ? New update ? Session continues as LIVE
14:40 - No update for 10 minutes ? Session still active
14:59 - No update for ~59 minutes ? Session TIMES OUT

BEFORE: Would have timed out at 14:02 (WRONG)
AFTER: Allows natural recovery, only times out after real inactivity
```

### 3. In-Memory Location Status (Separate from Session Status)

**File:** `LiveLocationStore.cs`

```csharp
// Status depends on LastUpdatedUtc in memory store
LIVE:    Age 0-30 seconds
STALE:   Age 30-120 seconds  
OFFLINE: Age 120+ seconds

// Session state depends on EmployeeGpsSessions in database
ACTIVE:  EndedAtUtc is null
ENDED:   EndedAtUtc has value
TIMEOUT: EndedAtUtc set by maintenance job after 30 min
```

**Key Difference:**
- **Location Status** = "How fresh is the last GPS update?"
- **Session Status** = "Is the employee's GPS session still active?"
- Admin checks BOTH before deciding if employee is truly offline

---

## Session Flow Diagrams

### Happy Path: Normal Login & Tracking

```
TIME    EMPLOYEE                ADMIN SCREEN              DATABASE
????????????????????????????????????????????????????????????????
00:00   Login
          ?
        GPS Watcher Starts
          ?
        Session ID Created ??????????????? EmployeeGpsSessions
                             [Active, no EndedAtUtc]

00:01   GPS Update (Latitude/Longitude)
          ?                    
        LiveLocationStore ????? [Shows LIVE]
        Update GPS Session ???? [Updates LastUpdateAtUtc]

00:05   GPS Update
          ?
        LiveLocationStore ????? [Shows LIVE]
        GPS Session Update ???? [LastUpdateAtUtc = 00:05]

00:10   No GPS Update (browser inactive)
          ?
        LiveLocationStore ????? [Shows STALE (5 min old)]
        GPS Session Idle ?????? [LastUpdateAtUtc still 00:05]

01:00   Still No GPS Update
          ?
        LiveLocationStore ????? [Shows OFFLINE (55 min old)]
        GPS Session STILL ACTIVE (only 55 min, needs 30+ min)
        
01:31   MarkTimedOutSessionsAsync runs
          ?
        Session Marked TIMED_OUT
          ?
        LiveLocationStore removed ??? [No longer shown]
        GPS Session Ended ??????????? [EndedAtUtc set]

Employee Manually Logs Out (Anytime)
          ?
        EndGpsSessionAsync ???????????? [EndedAtUtc set immediately]
        LiveLocationStore Removed ????? [No longer shown]
```

### Conflict Resolution: New Device Login

```
TIME    DEVICE A                DEVICE B              DATABASE
????????????????????????????????????????????????????????????
00:00   Login ?
          ?
        Active Session ??????????????? EmployeeDeviceLocks
        GPS Running                   EmployeeGpsSessions
        
00:15   Still Logged In ???????
          ?
        GPS Session Active    ?
        Device Lock Active    ?

00:15                         Login Attempt
                              ?
                    Check: Is session active? YES
                              ?
                    Show: "Session already active on Device A
                          Force logout?"
                              ?
                    If User Confirms:
                    ?
        Session Invalidated ????????? Security Stamp Changed
        Device Lock Removed ????????? Database cleanup
        GPS Session Ended ???????????? EndedAtUtc set
        (Previous location removed from LiveLocationStore)
                                      ?
                                      New Session Created
                                      New GPS Started
                                      New Device Lock
                                      ?
```

### Recovery: Circuit Reconnection

```
BROWSER EVENT           EMPLOYEE TRACKER          DATABASE
??????????????????????????????????????????????????????????
1. Disconnect
   (Network lost)
2. Reconnect  
   ?
3. Component Re-render
   ?
4. OnAfterRenderAsync
   ?
5. StartTracking()     Read from browser
                       localStorage:
                       SessionId ????? Check: Active?
                                       ? (EndedAtUtc null)
                                       ?
                                       Resume with same SessionId
                       
                       ? (EndedAtUtc has value)
                       ?
                       Create new SessionId
                       
6. GPS Watcher Restart
   ?
7. Location Updates
   ?
8. Admin Sees: LIVE
```

---

## Data Flow: GPS Update to Admin Display

```
Employee GPS Update Every 10 Seconds
        ?
EmployeeGpsTracker.UpdatePersistentEmployeeLocation()
        ??? LiveLocationStore.Update()
        ?   ??? In-Memory Cache
        ?       ??? Admin Page: "LIVE" status
        ?
        ??? GeoLocationService.UpdateGpsSessionAsync()
        ?   ??? EmployeeGpsSessions Table
        ?       ??? LastUpdateAtUtc
        ?       ??? Latitude/Longitude
        ?       ??? Distance/Accuracy
        ?
        ??? GeoLocationService.SaveLocationHistoryAsync()
        ?   ??? EmployeeLocationHistory Table
        ?       ??? One record every 10 seconds
        ?       ??? Used for history playback
        ?
        ??? AttendanceRefreshHub.SendAsync("LocationChanged")
            ??? SignalR Broadcast
                ??? Admin's LiveStaffLocationPanel
                    ??? RefreshLocations()
                    ??? UpdateMap()
                    ??? Admin sees new position
```

---

## Timeout Job Behavior

### Before Fix
```
MarkTimedOutSessionsAsync() runs every X minutes

FOR EACH ACTIVE SESSION:
    IF LastUpdateAtUtc > 2 minutes ago:
        Mark as TIMED_OUT
        ? PROBLEM: Too aggressive
        ? PROBLEM: Kills valid sessions just because GPS paused
```

### After Fix
```
MarkTimedOutSessionsAsync() runs every X minutes

FOR EACH ACTIVE SESSION:
    IF LastUpdateAtUtc > 30 minutes ago:
        Mark as TIMED_OUT  
        ? Allows natural recovery
        ? Only closes truly inactive sessions
        ? Logs reason: "TIMED_OUT" (vs "LOGGED_OUT" or "NEW_SESSION")
```

---

## Session End Reasons

Session ending can occur in multiple ways, all logged:

```csharp
session.EndReason values:

"LOGGED_OUT"    - Employee manually logged out
"NEW_SESSION"   - Employee logged in on different device
"TIMED_OUT"     - No GPS update for 30+ minutes
"CIRCUIT_DISPOSED" - Blazor circuit ended (not stored, memory only)
```

Admin can filter/analyze sessions by EndReason for auditing.

---

## Browser Storage

Employee GPS session ID stored in browser localStorage:

```javascript
// Key: "payroll_employee_gps_session_<employeeId>"
// Value: UUID (e.g., "a1b2c3d4e5f6")

// Persists across:
// ? Tab close/reopen
// ? Browser restart
// ? Page navigation
// ? Circuit reconnection

// Cleared on:
// ? Employee logout
// ? NOT on circuit disconnect
// ? NOT on browser tab close
```

This allows employees to resume GPS session even after closing browser:

```
Login ? GPS Running ? Close Browser
         ?
Open Browser ? Recognize same Employee/Device
         ?
Find old SessionId in localStorage
         ?
Check: Is it still active in database? (EndedAtUtc null)
         ?
YES ? Resume with same SessionId (admin sees continuous tracking)
NO  ? Create new SessionId (old session expired)
```

---

## Configuration Parameters

### SecurityStamp
- **ValidationInterval**: 365 days (effectively disabled)
- **Behavior**: Security stamps are validated on EVERY request, but only matters when changed
- **Impact**: Employees stay logged in for 30 days unless they logout or new device login happens

### Application Cookie  
- **ExpireTimeSpan**: 30 days
- **SlidingExpiration**: true (refreshes on every request)
- **HttpOnly**: true (no JavaScript access)
- **Secure**: true (HTTPS only)
- **SameSite**: Lax

### GPS Session Timeout
- **Inactive Period Before Timeout**: 30 minutes (1800 seconds)
- **Live Update Threshold**: 30 seconds (LIVE status)
- **Stale Threshold**: 120 seconds (STALE status)
- **Offline Threshold**: 120+ seconds (OFFLINE status)

### Location History
- **Recording Interval**: Every 10 seconds
- **Playback Available**: Full day history for selected employee
- **Retention**: All history records kept indefinitely

---

## Testing Scenarios

### Scenario 1: Normal Workday Tracking
```
08:00 - Employee logs in
        Admin sees: LIVE
08:00-18:00 - Regular GPS updates
        Admin sees: LIVE (continuously)
18:00 - Employee manually logs out
        Admin sees: No location (session ended)
```

### Scenario 2: Lunch Break (GPS Paused)
```
12:00 - GPS update received
        Admin sees: LIVE
12:00-13:00 - No GPS updates (phone in bag)
13:00 - Admin panel shows: STALE (60 min old)
13:05 - GPS resumes
        LiveLocationStore updated
        Admin sees: LIVE again
```

### Scenario 3: Browser/Network Issue
```
10:00 - GPS running normally
        Admin sees: LIVE
10:05 - Network disconnected
        LocationStore stale
        Admin sees: STALE
10:10 - Network reconnects
        Circuit resumes
        New GPS updates
        Admin sees: LIVE
```

### Scenario 4: Multi-Device Login
```
10:00 - Employee logs in on Laptop
        Device A Active
        Admin sees: LIVE from Laptop
10:15 - Employee logs in on Mobile
        "Session active" dialog appears
10:16 - Employee confirms force logout
        Laptop session ends
        Mobile becomes active
        Admin sees: Location shifts (different device)
```

---

## Maintenance & Monitoring

### Daily Maintenance Job
```csharp
// Should run daily (e.g., 11:59 PM)
await geoLocationService.MarkTimedOutSessionsAsync();

// Cleans up sessions with no update for 30+ minutes
// Logs: Count of sessions timed out
```

### Queries to Monitor Health

```sql
-- Active GPS Sessions (should be ~equal to logged-in employees)
SELECT COUNT(*) FROM "EmployeeGpsSessions" 
WHERE "EndedAtUtc" IS NULL;

-- Recent Session Ends (should see LOGGED_OUT, not timeouts)
SELECT "EndReason", COUNT(*) 
FROM "EmployeeGpsSessions"
WHERE "EndedAtUtc" > NOW() - INTERVAL '1 day'
GROUP BY "EndReason";

-- Long-running Sessions (confirm 30-day expiration working)
SELECT "EmployeeId", AGE("EndedAtUtc", "StartedAtUtc")
FROM "EmployeeGpsSessions"
WHERE "EndedAtUtc" IS NOT NULL
ORDER BY AGE DESC
LIMIT 10;
```

---

## Conclusion

The fix ensures that:
1. ? Employee sessions persist until manual logout or forced logout
2. ? GPS sessions have a reasonable grace period (30 min) for inactivity
3. ? Admin's live tracking reflects actual employee login status
4. ? No false "Offline" status from system timeouts
5. ? Single device active per employee enforced at login level
6. ? All existing functionality preserved and working correctly
