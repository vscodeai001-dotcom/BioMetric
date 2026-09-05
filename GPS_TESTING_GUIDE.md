# GPS Persistent Tracking - Testing Guide

## Quick Start Test

### Test 1: Verify GPS Still Works (5 minutes)
1. **Login as Employee**
   - Open the employee portal
   - Ensure GPS permission is granted

2. **Check Admin Dashboard**
   - Open admin panel in another browser/tab
   - Navigate to "Live Staff Locations"
   - Should see employee as LIVE (green)

3. **Expected Result:**
   - ? Employee location visible on map
   - ? Status shows "LIVE" 
   - ? Distance from office displayed
   - ? Accuracy shows GPS precision

---

## Comprehensive Test Scenarios

### Scenario A: Browser Tab Inactive (10 minutes)

**Objective:** Verify GPS continues when employee's tab is not active

1. **Setup**
   - Employee logged in and visible as LIVE on admin dashboard
   - Admin panel open in separate window/tab

2. **Test Steps**
   - Employee: Switch to another browser tab or minimize browser
   - Wait 30-60 seconds
   - Admin: Check if employee still shows as LIVE (or STALE)
   - Employee: Return to browser
   - Admin: Verify location updates resume (should update within 10 seconds)

3. **Expected Results**
   - ? Employee remains visible (LIVE or STALE, NOT OFFLINE)
   - ? Admin map still shows location
   - ? When tab active again, GPS continues smoothly
   - ? No false "OFFLINE" status from inactivity

4. **Browser Console Logs** (F12 > Console)
   ```
   "GPS watcher started"
   "Background force GPS update"
   "Tab is now HIDDEN - GPS will continue in background"
   "Tab is now VISIBLE - Forcing GPS update"
   "GPS location updated via HTTP API" (if API fallback)
   "GPS Update: Lat=X.XXXXXX, Lon=Y.YYYYYY, Accuracy=Zm"
   ```

---

### Scenario B: Network Interruption & Recovery (15 minutes)

**Objective:** Verify GPS queuing and recovery when network is down

1. **Setup**
   - Employee logged in and visible as LIVE
   - Admin panel open
   - Browser DevTools open (F12)

2. **Test Steps**
   - DevTools: Go to Network tab
   - DevTools: Click "Offline" checkbox (simulate network down)
   - Employee: Keep browser open and wait 30-60 seconds
   - Admin: Watch employee status (should transition LIVE ? STALE ? OFFLINE)
   - DevTools: Uncheck "Offline" checkbox (network restored)
   - Wait 5-10 seconds
   - Admin: Employee should return to LIVE or STALE status

3. **Expected Results**
   - ? During offline: GPS continues working (still getting locations)
   - ? Locations queued in browser localStorage
   - ? When network returns: Queued locations auto-sent
   - ? Employee returns to LIVE status on admin dashboard
   - ? Admin sees continuous location history (no gaps)

4. **Browser Console Logs** (F12 > Console)
   ```
   "Network connection lost. GPS locations will be queued."
   "Location queued for retry. Queue size: 1"
   "Network connection restored. Processing queued GPS locations..."
   "GPS location sent via HTTP API"
   "Processing N queued GPS locations"
   ```

5. **Check localStorage** (F12 > Storage > Local Storage > Current Site)
   ```
   gps_location_queue: Should contain JSON array of queued locations
   (After network recovery, should be empty)
   ```

---

### Scenario C: Browser Close & Reopen (10 minutes)

**Objective:** Verify GPS session persists across browser close/reopen

1. **Setup**
   - Employee logged in and visible as LIVE
   - Admin panel open in admin's browser
   - Note the GPS session ID or employee location

2. **Test Steps**
   - Employee: Wait for location to update 1-2 times (watch admin dashboard)
   - Employee: Close the employee browser tab/window completely
   - Wait 10-15 seconds
   - Admin: Employee should transition to STALE then OFFLINE
   - Employee: Reopen employee portal login
   - Employee: Login with same credentials
   - Wait 5-10 seconds
   - Admin: Employee should return to LIVE status

3. **Expected Results**
   - ? After close: Employee shows OFFLINE after 2 min
   - ? After reopen: GPS starts within 5 seconds
   - ? Session ID recognized (either same or new)
   - ? Admin sees continuous or resumed tracking
   - ? No duplicate locations in history

4. **Browser Console Logs**
   ```
   "Retrieved existing GPS session: [UUID]"
   OR
   "Stored GPS session has ended or timed out. Creating a new session."
   "GPS watcher initialized. EmployeeId=X, SessionId=[UUID]"
   "Employee GPS started"
   ```

---

### Scenario D: Blazor Circuit Disconnection (12 minutes)

**Objective:** Verify HTTP API fallback when Blazor circuit is disconnected

1. **Setup**
   - Employee logged in
   - Admin panel open
   - Browser DevTools open (F12)
   - Network tab showing API calls

2. **Test Steps**
   - Employee: Navigate between pages (Home ? Attendance ? Payslips)
   - Watch Network tab for requests
   - Admin: Monitor employee status (should remain LIVE)
   - DevTools: Filter to API calls in Network tab
   - Look for: `POST /api/employee-location/update` requests
   - Test Circuit Reconnection:
     - Simulate losing connection: DevTools > Network > Throttling > Offline
     - Wait 5-10 seconds (GPS tries Blazor JSInterop, fails, retries)
     - Turn back Online
     - Wait 10 seconds

3. **Expected Results**
   - ? Blazor JSInterop works when circuit active
   - ? Falls back to HTTP API automatically
   - ? No manual intervention needed
   - ? Admin sees continuous tracking
   - ? Seamless transition between modes

4. **Network Tab Analysis**
   ```
   Look for:
   - Successful API calls: POST /api/employee-location/update ? 200 OK
   - Each call shows JSON payload with GPS coordinates
   - Response time: Should be <100ms normally
   ```

5. **Server Console Logs** (Check application logs)
   ```
   "GPS location updated via HTTP API. EmployeeId=X, Lat=X.XXXXXX, Lon=Y.YYYYYY"
   "GPS location updated via HTTP API" messages indicate fallback is working
   (Compare to Blazor console messages in Razor component)
   ```

---

### Scenario E: Rapid Battery Drain / Phone Pocket Scenario (15 minutes)

**Objective:** Simulate real-world usage where GPS might be paused

1. **Setup**
   - Employee at office (within geofence)
   - Admin panel open
   - Employee working with browser tab open but not focused

2. **Test Steps**
   - Employee: Work normally for 3-5 minutes
   - Employee: Put phone in pocket / move away from desk
   - Browser might auto-throttle GPS due to power saving
   - Wait 2-3 minutes
   - Admin: Watch status (might show STALE)
   - Employee: Return to desk
   - Admin: Verify status returns to LIVE within 10 seconds

3. **Expected Results**
   - ? No false "OFFLINE" status from power management
   - ? GPS resumes automatically when device active again
   - ? Force update every 10 seconds prevents stale status
   - ? Location history shows continuous tracking

---

## Debugging Tools

### Browser Console (F12)
Monitor real-time GPS activity:
```javascript
// Manually force GPS update (for testing)
window.EmployeeGpsTracker.sendLocationViaHttpApi({
    latitude: 12.9716,
    longitude: 77.5946,
    accuracy: 10
});

// Check current GPS session ID
localStorage.getItem("current_employee_id");
localStorage.getItem("gps_session_id");

// Check queued locations
JSON.parse(localStorage.getItem("gps_location_queue") || "[]");
```

### Admin Dashboard Live Map
- **Green (LIVE):** Location updated within 30 seconds
- **Yellow (STALE):** Location updated 30-120 seconds ago
- **Red (OFFLINE):** Location updated 120+ seconds ago
- **Grayed Out:** No location data available

### Database Queries (for admins with DB access)
```sql
-- Check active GPS sessions
SELECT e.EmployeeId, e.FirstName, eg.SessionId, eg.StartedAtUtc, eg.LastUpdateAtUtc
FROM EmployeeGpsSessions eg
JOIN employees e ON eg.EmployeeId = e.Id
WHERE eg.EndedAtUtc IS NULL
ORDER BY eg.LastUpdateAtUtc DESC;

-- Check recent location history
SELECT * FROM employee_location_history
WHERE EmployeeId = ?
ORDER BY RecordedAtUtc DESC
LIMIT 20;

-- Check GPS session ends
SELECT SessionId, EndReason, EndedAtUtc
FROM EmployeeGpsSessions
WHERE EmployeeId = ?
ORDER BY EndedAtUtc DESC;
```

---

## Common Issues & Solutions

### Issue: "Employee shows OFFLINE immediately"
**Possible Causes:**
- GPS permission denied
- Geolocation API not supported
- JavaScript error in GPS tracker
- Database session creation failed

**Solutions:**
1. Check browser console for errors (F12 > Console)
2. Verify GPS permission granted
3. Check server logs for GPS session errors
4. Restart application

### Issue: "API endpoint returns 409 Conflict"
**Possible Causes:**
- Session ID mismatch
- Another login device invalidated session
- Session timed out in database

**Solutions:**
1. Employee logs out and back in
2. Check if another device is logged in (single session per employee)
3. Wait for session timeout (30 minutes)

### Issue: "Queued locations not sending"
**Possible Causes:**
- API endpoint not accessible
- Authorization failed
- Network still not actually connected
- API endpoint URL incorrect

**Solutions:**
1. Check browser Network tab (F12 > Network)
2. Verify `/api/employee-location/update` endpoint exists
3. Check if authorization cookie is still valid
4. Look for 401/403 responses in Network tab

### Issue: "Location history has gaps"
**Possible Causes:**
- Normal behavior during offline periods
- GPS disabled on employee device
- Browser power management paused GPS

**Solutions:**
1. Check GPS watcher status in console
2. Verify location updates in Network tab
3. Review job logs for GPS session timeout
4. This is expected behavior during network loss

---

## Performance Metrics to Monitor

### During 1-Hour Continuous Tracking:
- **API Calls:** ~400 (10 sec × 360 min)
- **Database Updates:** ~400 (GPS session + history combined)
- **localStorage Size:** <10KB (GPS data + queue)
- **Memory Usage:** <5MB JavaScript overhead
- **Battery Impact:** Minimal (background tracking)

### Expected Behavior:
- Updates every 5-10 seconds (configurable)
- Force update every 10 seconds (guarantees responsiveness)
- Graceful degradation when offline
- Automatic recovery when online
- No memory leaks (intervals cleaned up properly)

---

## Test Report Template

```
Test Date: [DATE]
Tester: [NAME]
Employee: [EMPLOYEE ID]
Browser: [CHROME/FIREFOX/EDGE]
Device: [DESKTOP/MOBILE]

Scenario A (Tab Inactive):  [PASS/FAIL]
Scenario B (Network Loss):   [PASS/FAIL]
Scenario C (Browser Close):  [PASS/FAIL]
Scenario D (Circuit DC):     [PASS/FAIL]
Scenario E (Battery Drain):  [PASS/FAIL]

Issues Found:
[DESCRIBE ANY FAILURES]

Console Errors:
[COPY RELEVANT ERRORS]

Network Issues:
[API FAILURES, TIMEOUTS, ETC]

Database Issues:
[GPS SESSION ISSUES, HISTORY GAPS, ETC]

Recommendations:
[IMPROVEMENTS OR FIXES NEEDED]
```

---

## Final Verification Checklist

Before considering GPS tracking "fixed":

- [ ] GPS works with Blazor circuit active
- [ ] GPS works with Blazor circuit inactive
- [ ] GPS works after network loss/recovery
- [ ] GPS works after browser close/reopen
- [ ] Admin sees LIVE status continuously
- [ ] No false "OFFLINE" from inactivity
- [ ] Location history has no gaps
- [ ] API endpoint responding correctly
- [ ] Queue clears after network recovery
- [ ] Session persists across page navigation
- [ ] Session recovers across browser close
- [ ] Multiple employees tracked correctly
- [ ] Admin dashboard updates in real-time
- [ ] No console errors or exceptions
- [ ] Battery usage reasonable
- [ ] No memory leaks after 1+ hour
- [ ] Graceful degradation when offline
- [ ] Automatic recovery when online

? All tests passing = **GPS tracking is working correctly**
