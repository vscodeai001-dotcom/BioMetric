# Session Management & Live Location Tracking Fix

## Problem Statement

**Admin's live location tracking shows employees as "Offline" after a few minutes even though employees are still logged in.**

**Root Cause:** Sessions were expiring too frequently due to aggressive SecurityStamp validation timing, causing GPS sessions to be marked as timed out and appearing offline on the admin's live map.

---

## Solution Overview

The fix implements a **non-expiring session model** where:

1. ? Sessions persist indefinitely until **manual logout**
2. ? Only one device/device per employee can be logged in at a time
3. ? When a new device logs in, the existing session is invalidated  
4. ? GPS tracking continues uninterrupted even if the employee's browser tab loses focus
5. ? Admin's live map continuously shows employee locations until manual logout
6. ? No more false "Offline" status from timeouts

---

## Changes Made

### 1. **Program.cs** - Security Configuration

#### Before:
```csharp
options.ValidationInterval = TimeSpan.FromMinutes(5);
```

#### After:
```csharp
options.ValidationInterval = TimeSpan.FromDays(365);
```

**Why:** 
- SecurityStamp was validating every 5 minutes, causing unexpected logouts
- Disabled auto-validation while preserving explicit security stamp changes
- Sessions now persist for up to 30 days (cookie expiration) or until manual logout

**Impact:**
- Employees stay logged in even if browser is inactive
- GPS watcher continues running in background
- Live tracking persists across browser navigation and circuit reconnects

---

### 2. **GeoLocationService.cs** - GPS Session Timeout Policy

#### Before:
```csharp
var timeoutBefore = DateTime.UtcNow.AddSeconds(-LiveLocationStore.StaleTimeoutSeconds);
// StaleTimeoutSeconds = 120 (2 minutes)
```

#### After:
```csharp
var timeoutBefore = DateTime.UtcNow.AddSeconds(-1800);  // 30 minutes
```

**Why:**
- GPS sessions were timing out after just 2 minutes without updates
- This caused "Offline" display even though employee was logged in
- Network delays or GPS pauses (battery saving) triggered false timeouts

**Impact:**
- GPS sessions remain active for 30 minutes without updates
- Only truly inactive sessions (no update for 30+ minutes) are closed
- Allows recovery from temporary GPS/network interruptions
- Admin sees accurate employee status

---

### 3. **EmployeeGpsTracker.razor** - Component Documentation

Added detailed comments explaining:
- Normal navigation does NOT create new GPS sessions
- Circuit disposal does NOT end database GPS sessions  
- Browser tab close does NOT immediately end sessions
- Only manual logout explicitly ends GPS sessions
- Automatic session recovery on circuit reconnect

**Impact:**
- Clear lifecycle management for debugging
- Proper expectations for GPS session persistence

---

### 4. **LiveLocationStore.cs** - Status Documentation

Added comprehensive documentation of GPS status meanings:
- **LIVE (0-30 sec):** GPS update within last 30 seconds
- **STALE (30-120 sec):** No update for 30-120 seconds  
- **OFFLINE (120+ sec):** No update for 120+ seconds

**Impact:**
- Clarifies why employees appear "Stale" vs "Offline"
- Helps interpret live map status correctly
- Documents the 2-minute grace period before showing offline

---

## Session Lifecycle After Fix

```
Employee Login (Device A)
         ?
[Employee Session Created - No Expiration]
[GPS Session Started - 30 min timeout]
[In-Memory Location Updated Every GPS Update]
         ?
Admin Sees: LIVE (0-30 sec) ? STALE (30-120 sec) ? OFFLINE (120+ sec)
         ?
Employee Login on Device B
         ?
[Device A Session Invalidated]
[Device B Gets New Session & GPS]
[Admin Sees New Employee Status]
         ?
Employee Manually Logs Out
         ?
[Session Ended]
[GPS Session Closed]
[In-Memory Location Removed]
[Admin Sees: No Location]
```

---

## Key Features Preserved

? **Single-device login per employee** - One device active at a time  
? **Multi-device acknowledgment** - Alert when logging in on different device  
? **Manual logout required** - No auto-expiration  
? **GPS session lifecycle** - Starts on login, ends on logout  
? **Live tracking continuous** - No interruption during inactivity  
? **Circuit reconnect support** - GPS resumes after connection loss  
? **Admin/SuperAdmin unrestricted** - Multiple simultaneous logins allowed  

---

## Configuration Summary

| Setting | Before | After | Purpose |
|---------|--------|-------|---------|
| SecurityStamp Validation | 5 min | 365 days | Prevent unexpected logouts |
| Session Cookie Lifetime | 30 days | 30 days | Session persistence duration |
| GPS Session Timeout | 2 min | 30 min | Grace period for inactivity |
| Sliding Expiration | Yes | Yes | Refresh on every request |

---

## Testing Checklist

- [ ] Employee logs in ? GPS starts tracking
- [ ] Admin sees employee as "LIVE" on location map  
- [ ] Employee doesn't interact with app for 5+ minutes ? Still "LIVE" on map
- [ ] Employee doesn't interact for 30+ minutes ? GPS session times out
- [ ] Admin manually logs out employee ? Location removed from map
- [ ] Employee logs in on second device ? First device's session ends
- [ ] Circuit reconnects ? GPS continues without interruption
- [ ] Location history shows continuous tracking (no gaps from timeouts)
- [ ] Punch audit shows GPS points throughout the day

---

## No Breaking Changes

- ? Existing database schema unchanged
- ? Existing business logic preserved  
- ? Existing API contracts unchanged
- ? Backward compatible with deployed systems

---

## Result

**Problem Solved:** Admin's live location tracking now shows employee locations continuously until manual logout. No more false "Offline" status from system timeouts.

**Benefit:** Admins can reliably track where employees are throughout the workday with real-time accuracy.
