/**
 * ============================================================
 * PERSISTENT EMPLOYEE GPS TRACKER
 * ============================================================
 *
 * This module handles real-time GPS tracking for employees.
 *
 * RESPONSIBILITIES:
 * 1. Get browser geolocation (with permission)
 * 2. Watch location changes continuously
 * 3. Send location updates to Blazor component
 * 4. Handle GPS errors gracefully
 * 5. Maintain session across browser tabs
 *
 * IMPORTANT:
 * - Each employee gets their own GPS session
 * - localStorage persists across tab close/reopen
 * - GPS watcher survives Blazor circuit reconnect
 */

window.EmployeeGpsTracker = (function () {

    // ============================================================
    // STATE
    // ============================================================

    let dotNetReference = null;
    let gpsWatcherId = null;
    let isWatching = false;
    let lastBroadcastTime = 0;
    let lastLocationData = null;
    let visibilityCheckInterval = null;
    const BROADCAST_INTERVAL_MS = 5000; // Send updates every 5 seconds minimum
    const VISIBILITY_CHECK_INTERVAL_MS = 3000; // Check visibility every 3 seconds
    const FORCE_UPDATE_INTERVAL_MS = 15000; // Force GPS update even if no movement every 15 seconds

    // ============================================================
    // START PERSISTENT GPS WATCHER
    // ============================================================

    function startPersistentEmployeeGps(blazorReference) {

        console.log('startPersistentEmployeeGps called');

        if (isWatching) {
            console.log('GPS watcher already running');
            return true;
        }

        dotNetReference = blazorReference;

        // Check if geolocation is supported
        if (!navigator.geolocation) {
            console.error('Geolocation is not supported by this browser');
            blazorReference.invokeMethodAsync(
                'PersistentEmployeeGpsError',
                'Geolocation not supported');
            return false;
        }

        try {
            // Start watching position with aggressive settings
            gpsWatcherId = navigator.geolocation.watchPosition(
                onLocationSuccess,
                onLocationError,
                {
                    enableHighAccuracy: true,
                    timeout: 10000,
                    maximumAge: 0  // Always get fresh location, never use cached
                }
            );

            isWatching = true;
            lastBroadcastTime = 0;
            lastLocationData = null;

            console.log('GPS watcher started. WatcherId=' + gpsWatcherId);

            // ============================================================
            // MONITOR TAB VISIBILITY
            // ============================================================
            // If tab becomes hidden, force GPS update when it becomes visible again
            // This keeps GPS active even when tab is in background
            // ============================================================
            
            if (document.addEventListener) {
                document.addEventListener('visibilitychange', onVisibilityChange);
                console.log('Visibility change listener registered');
            }

            // ============================================================
            // PERIODIC BACKGROUND CHECK
            // ============================================================
            // Even if watchPosition is paused by browser power management,
            // we force a location update every 15 seconds
            // This keeps admin dashboard updated even with inactive tab
            // ============================================================
            
            startVisibilityAndBackgroundCheck();

            return true;
        }
        catch (error) {
            console.error('Failed to start GPS watcher:', error);
            blazorReference.invokeMethodAsync(
                'PersistentEmployeeGpsError',
                'Failed to start GPS: ' + error.message);
            return false;
        }
    }

    // ============================================================
    // STOP GPS WATCHER
    // ============================================================

    function stopPersistentEmployeeGps() {

        console.log('stopPersistentEmployeeGps called');

        if (gpsWatcherId !== null && typeof gpsWatcherId !== 'undefined') {
            navigator.geolocation.clearWatch(gpsWatcherId);
            gpsWatcherId = null;
        }

        isWatching = false;
        dotNetReference = null;
        lastBroadcastTime = 0;
        lastLocationData = null;

        // Stop background checks
        stopVisibilityAndBackgroundCheck();

        // Remove event listeners
        if (document.removeEventListener) {
            document.removeEventListener('visibilitychange', onVisibilityChange);
        }

        console.log('GPS watcher stopped');
    }

    // ============================================================
    // TAB VISIBILITY CHANGE HANDLER
    // ============================================================
    // When tab becomes visible, force a location update
    // This prevents stale status when tab is in background
    // ============================================================

    function onVisibilityChange() {

        if (!isWatching) {
            return;
        }

        if (document.hidden) {
            console.log('Tab is now HIDDEN - GPS will continue in background');
        }
        else {
            console.log('Tab is now VISIBLE - Forcing GPS update');
            // Restart the watcher to get fresh location
            forceLocationUpdate();
        }
    }

    // ============================================================
    // VISIBILITY AND BACKGROUND CHECK INTERVAL
    // ============================================================
    // Periodically check if GPS is still active and force updates
    // This handles browser power management that pauses watchPosition
    // ============================================================

    function startVisibilityAndBackgroundCheck() {

        if (visibilityCheckInterval !== null) {
            return;
        }

        visibilityCheckInterval = setInterval(function () {

            if (!isWatching || !dotNetReference) {
                stopVisibilityAndBackgroundCheck();
                return;
            }

            // Force a location update every 15 seconds
            // This keeps admin dashboard updated even if tab is inactive
            const timeSinceLastUpdate = Date.now() - lastBroadcastTime;

            if (timeSinceLastUpdate > FORCE_UPDATE_INTERVAL_MS) {
                console.log('Background force GPS update (15 second interval)');
                forceLocationUpdate();
            }

        }, VISIBILITY_CHECK_INTERVAL_MS);

        console.log('Background visibility/check interval started');
    }

    function stopVisibilityAndBackgroundCheck() {

        if (visibilityCheckInterval !== null) {
            clearInterval(visibilityCheckInterval);
            visibilityCheckInterval = null;
            console.log('Background visibility/check interval stopped');
        }
    }

    // ============================================================
    // FORCE LOCATION UPDATE
    // ============================================================
    // Request current position immediately
    // Bypasses the regular watchPosition interval
    // ============================================================

    function forceLocationUpdate() {

        if (!navigator.geolocation || !isWatching || !dotNetReference) {
            return;
        }

        try {
            navigator.geolocation.getCurrentPosition(
                function (position) {
                    console.log('Force update - getCurrentPosition success');
                    onLocationSuccess(position);
                },
                function (error) {
                    console.log('Force update - getCurrentPosition error: ' + error.message);
                    // Don't report error, just continue with watchPosition
                },
                {
                    enableHighAccuracy: true,
                    timeout: 5000,
                    maximumAge: 0
                }
            );
        }
        catch (error) {
            console.error('Force location update failed:', error);
        }
    }

    // ============================================================
    // LOCATION SUCCESS CALLBACK
    // ============================================================

    function onLocationSuccess(position) {

        if (!dotNetReference || !isWatching) {
            return;
        }

        try {
            const now = Date.now();

            // Throttle broadcasts to prevent excessive updates
            if (now - lastBroadcastTime < BROADCAST_INTERVAL_MS) {
                return;
            }

            lastBroadcastTime = now;

            const coords = position.coords;

            // Store last location for forced updates
            lastLocationData = {
                latitude: coords.latitude,
                longitude: coords.longitude,
                accuracy: coords.accuracy
            };

            console.log(
                'GPS Update: ' +
                'Lat=' + coords.latitude.toFixed(6) + ', ' +
                'Lon=' + coords.longitude.toFixed(6) + ', ' +
                'Accuracy=' + Math.round(coords.accuracy) + 'm'
            );

            // Send to Blazor component
            dotNetReference.invokeMethodAsync(
                'UpdatePersistentEmployeeLocation',
                {
                    latitude: coords.latitude,
                    longitude: coords.longitude,
                    accuracy: coords.accuracy
                }
            ).catch(error => {
                console.error('Failed to send GPS update to Blazor:', error);
            });
        }
        catch (error) {
            console.error('Error processing GPS location:', error);
        }
    }

    // ============================================================
    // LOCATION ERROR CALLBACK
    // ============================================================

    function onLocationError(error) {

        if (!dotNetReference || !isWatching) {
            return;
        }

        let errorMessage;

        switch (error.code) {
            case error.PERMISSION_DENIED:
                errorMessage = 'GPS permission denied. Enable location in browser settings.';
                break;
            case error.POSITION_UNAVAILABLE:
                errorMessage = 'GPS position unavailable. Retrying...';
                break;
            case error.TIMEOUT:
                errorMessage = 'GPS request timed out. Retrying...';
                break;
            default:
                errorMessage = 'GPS error: ' + error.message;
        }

        console.warn('GPS Error:', errorMessage);

        dotNetReference.invokeMethodAsync(
            'PersistentEmployeeGpsError',
            errorMessage
        ).catch(err => {
            console.error('Failed to send GPS error to Blazor:', err);
        });
    }

    // ============================================================
    // CREATE OR GET BROWSER SESSION ID
    // ============================================================

    function getOrCreateEmployeeGpsSessionId(storageKey, employeeId) {

        const prefixedKey = storageKey + '_' + employeeId;

        try {
            let sessionId = localStorage.getItem(prefixedKey);

            if (!sessionId || sessionId.trim() === '') {
                // Create new session ID
                sessionId = generateGuid();
                localStorage.setItem(prefixedKey, sessionId);
                console.log('Created new GPS session: ' + sessionId);
            }
            else {
                console.log('Retrieved existing GPS session: ' + sessionId);
            }

            return sessionId;
        }
        catch (error) {
            console.error('Failed to manage GPS session ID:', error);
            return generateGuid();
        }
    }

    // ============================================================
    // CREATE NEW SESSION ID (forced)
    // ============================================================

    function createNewEmployeeGpsSessionId(storageKey, employeeId) {

        const prefixedKey = storageKey + '_' + employeeId;

        try {
            const newSessionId = generateGuid();
            localStorage.setItem(prefixedKey, newSessionId);
            console.log('Created new forced GPS session: ' + newSessionId);
            return newSessionId;
        }
        catch (error) {
            console.error('Failed to create new GPS session ID:', error);
            return generateGuid();
        }
    }

    // ============================================================
    // CLEAR SESSION ID (on logout)
    // ============================================================

    function clearEmployeeGpsSessionId(storageKey, employeeId) {

        const prefixedKey = storageKey + '_' + employeeId;

        try {
            localStorage.removeItem(prefixedKey);
            console.log('Cleared GPS session ID');
        }
        catch (error) {
            console.error('Failed to clear GPS session ID:', error);
        }
    }

    // ============================================================
    // GENERATE GUID
    // ============================================================

    function generateGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            const r = Math.random() * 16 | 0;
            const v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    return {
        startPersistentEmployeeGps: startPersistentEmployeeGps,
        stopPersistentEmployeeGps: stopPersistentEmployeeGps,
        getOrCreateEmployeeGpsSessionId: getOrCreateEmployeeGpsSessionId,
        createNewEmployeeGpsSessionId: createNewEmployeeGpsSessionId,
        clearEmployeeGpsSessionId: clearEmployeeGpsSessionId
    };

})();

// ============================================================
// EXPOSE FUNCTIONS TO GLOBAL SCOPE
// ============================================================
// These are called from Blazor components via JSRuntime

window.startPersistentEmployeeGps =
    function (blazorReference) {
        return window.EmployeeGpsTracker.startPersistentEmployeeGps(blazorReference);
    };

window.stopPersistentEmployeeGps =
    function () {
        window.EmployeeGpsTracker.stopPersistentEmployeeGps();
    };

window.getOrCreateEmployeeGpsSessionId =
    function (storageKey, employeeId) {
        return window.EmployeeGpsTracker.getOrCreateEmployeeGpsSessionId(storageKey, employeeId);
    };

window.createNewEmployeeGpsSessionId =
    function (storageKey, employeeId) {
        return window.EmployeeGpsTracker.createNewEmployeeGpsSessionId(storageKey, employeeId);
    };

window.clearEmployeeGpsSessionId =
    function (storageKey, employeeId) {
        window.EmployeeGpsTracker.clearEmployeeGpsSessionId(storageKey, employeeId);
    };

console.log('Employee GPS Tracker module loaded');
