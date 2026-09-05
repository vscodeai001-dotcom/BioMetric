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
 * 3. Send location updates via HTTP API (independent of Blazor)
 * 4. Handle GPS errors gracefully
 * 5. Maintain session across browser tabs
 * 6. Support background tracking via Service Worker
 *
 * IMPORTANT:
 * - Each employee gets their own GPS session
 * - localStorage persists across tab close/reopen
 * - GPS watcher survives Blazor circuit reconnect
 * - GPS data sent via HTTP API, not dependent on Blazor JSInterop
 * - Continues tracking even when tab is inactive or closed
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
    let employeeId = null;
    let gpsSessionId = null;
    let apiEndpoint = null;
    const BROADCAST_INTERVAL_MS = 5000; // Send updates every 5 seconds minimum
    const VISIBILITY_CHECK_INTERVAL_MS = 3000; // Check visibility every 3 seconds
    const FORCE_UPDATE_INTERVAL_MS = 10000; // Force GPS update every 10 seconds
    const LOCATION_QUEUE_STORAGE_KEY = 'gps_location_queue';
    const EMPLOYEE_ID_STORAGE_KEY = 'current_employee_id';
    const GPS_SESSION_STORAGE_KEY = 'gps_session_id';
    const API_ENDPOINT_STORAGE_KEY = 'gps_api_endpoint';

    // ============================================================
    // START PERSISTENT GPS WATCHER
    // ============================================================

    function startPersistentEmployeeGps(blazorReference, empId, sessionId, endpoint) {

        console.log('startPersistentEmployeeGps called with empId=' + empId);

        if (isWatching) {
            console.log('GPS watcher already running');
            return true;
        }

        dotNetReference = blazorReference;
        employeeId = empId;
        gpsSessionId = sessionId;
        apiEndpoint = endpoint || '/api/employee-location/update';

        // Store employee info in localStorage for background tracking
        try {
            localStorage.setItem(EMPLOYEE_ID_STORAGE_KEY, employeeId);
            localStorage.setItem(GPS_SESSION_STORAGE_KEY, gpsSessionId);
            localStorage.setItem(API_ENDPOINT_STORAGE_KEY, apiEndpoint);
            console.log('Stored GPS session info in localStorage');
        }
        catch (error) {
            console.error('Failed to store GPS session info:', error);
        }

        // Check if geolocation is supported
        if (!navigator.geolocation) {
            console.error('Geolocation is not supported by this browser');
            if (blazorReference && blazorReference.invokeMethodAsync) {
                blazorReference.invokeMethodAsync(
                    'PersistentEmployeeGpsError',
                    'Geolocation not supported');
            }
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
            // Force a location update periodically via HTTP API
            // This keeps admin dashboard updated even with inactive tab
            // ============================================================
            
            startVisibilityAndBackgroundCheck();

            return true;
        }
        catch (error) {
            console.error('Failed to start GPS watcher:', error);
            if (blazorReference && blazorReference.invokeMethodAsync) {
                blazorReference.invokeMethodAsync(
                    'PersistentEmployeeGpsError',
                    'Failed to start GPS: ' + error.message);
            }
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
    // Periodically force GPS update via HTTP API
    // This handles browser power management that pauses watchPosition
    // Works even if tab is inactive, circuit disconnected, or browser closed
    // ============================================================

    function startVisibilityAndBackgroundCheck() {

        if (visibilityCheckInterval !== null) {
            return;
        }

        visibilityCheckInterval = setInterval(function () {

            if (!isWatching) {
                stopVisibilityAndBackgroundCheck();
                return;
            }

            // Force a location update every 10 seconds
            // This keeps admin dashboard updated even if tab is inactive
            const timeSinceLastUpdate = Date.now() - lastBroadcastTime;

            if (timeSinceLastUpdate > FORCE_UPDATE_INTERVAL_MS) {
                console.log('Background force GPS update (' + FORCE_UPDATE_INTERVAL_MS + 'ms interval)');
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

        if (!isWatching) {
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
                accuracy: coords.accuracy,
                timestamp: now
            };

            console.log(
                'GPS Update: ' +
                'Lat=' + coords.latitude.toFixed(6) + ', ' +
                'Lon=' + coords.longitude.toFixed(6) + ', ' +
                'Accuracy=' + Math.round(coords.accuracy) + 'm'
            );

            const locationData = {
                latitude: coords.latitude,
                longitude: coords.longitude,
                accuracy: coords.accuracy
            };

            // Send to Blazor component (if circuit is active)
            if (dotNetReference && dotNetReference.invokeMethodAsync) {
                dotNetReference.invokeMethodAsync(
                    'UpdatePersistentEmployeeLocation',
                    locationData
                ).catch(error => {
                    console.warn('Failed to send GPS update to Blazor (will use HTTP API):', error);
                    // Fall back to HTTP API if Blazor circuit is disconnected
                    sendLocationViaHttpApi(locationData);
                });
            }
            else {
                // Blazor reference unavailable, use HTTP API
                sendLocationViaHttpApi(locationData);
            }
        }
        catch (error) {
            console.error('Error processing GPS location:', error);
        }
    }

    // ============================================================
    // SEND LOCATION VIA HTTP API
    // ============================================================
    // Send GPS data directly to API endpoint
    // Works even if Blazor circuit is disconnected
    // Queues updates if network is offline
    // ============================================================

    function sendLocationViaHttpApi(locationData) {

        if (!employeeId || !apiEndpoint) {
            console.warn('Cannot send GPS via HTTP: employeeId or apiEndpoint not set');
            return;
        }

        const payload = {
            employeeId: employeeId,
            sessionId: gpsSessionId,
            latitude: locationData.latitude,
            longitude: locationData.longitude,
            accuracy: locationData.accuracy,
            timestamp: new Date().toISOString()
        };

        fetch(apiEndpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(payload),
            credentials: 'include'
        })
        .then(response => {
            if (response.ok) {
                console.log('GPS location sent via HTTP API');
            }
            else {
                console.warn('HTTP API returned status ' + response.status);
                queueLocationForRetry(locationData);
            }
        })
        .catch(error => {
            console.warn('Failed to send GPS via HTTP API, queuing for retry:', error);
            queueLocationForRetry(locationData);
        });
    }

    // ============================================================
    // QUEUE LOCATION FOR RETRY
    // ============================================================
    // Store location data in localStorage when network is offline
    // Will be sent when network is restored
    // ============================================================

    function queueLocationForRetry(locationData) {

        try {
            let queue = [];
            const queueJson = localStorage.getItem(LOCATION_QUEUE_STORAGE_KEY);
            
            if (queueJson) {
                queue = JSON.parse(queueJson);
            }

            queue.push({
                ...locationData,
                timestamp: new Date().toISOString(),
                employeeId: employeeId,
                sessionId: gpsSessionId
            });

            // Keep only last 100 items to prevent storage overflow
            if (queue.length > 100) {
                queue = queue.slice(-100);
            }

            localStorage.setItem(LOCATION_QUEUE_STORAGE_KEY, JSON.stringify(queue));
            console.log('Location queued for retry. Queue size: ' + queue.length);
        }
        catch (error) {
            console.error('Failed to queue location:', error);
        }
    }

    // ============================================================
    // PROCESS QUEUED LOCATIONS
    // ============================================================
    // Send any queued location data when network is back online
    // ============================================================

    function processQueuedLocations() {

        try {
            const queueJson = localStorage.getItem(LOCATION_QUEUE_STORAGE_KEY);
            
            if (!queueJson) {
                return;
            }

            const queue = JSON.parse(queueJson);
            
            if (queue.length === 0) {
                return;
            }

            console.log('Processing ' + queue.length + ' queued GPS locations');

            // Send each queued location
            queue.forEach(function (locationData, index) {
                setTimeout(function () {
                    sendLocationViaHttpApi(locationData);
                }, index * 500); // Stagger requests to avoid overwhelming server
            });

            // Clear the queue
            localStorage.removeItem(LOCATION_QUEUE_STORAGE_KEY);
        }
        catch (error) {
            console.error('Failed to process queued locations:', error);
        }
    }

    // ============================================================
    // LOCATION ERROR CALLBACK
    // ============================================================

    function onLocationError(error) {

        if (!isWatching) {
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

        if (dotNetReference && dotNetReference.invokeMethodAsync) {
            dotNetReference.invokeMethodAsync(
                'PersistentEmployeeGpsError',
                errorMessage
            ).catch(err => {
                console.error('Failed to send GPS error to Blazor:', err);
            });
        }
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
        clearEmployeeGpsSessionId: clearEmployeeGpsSessionId,
        sendLocationViaHttpApi: sendLocationViaHttpApi,
        processQueuedLocations: processQueuedLocations
    };

})();

// ============================================================
// NETWORK EVENT LISTENERS
// ============================================================
// Listen for online/offline events to handle queued GPS data
// ============================================================

if (window.addEventListener) {
    window.addEventListener('online', function () {
        console.log('Network connection restored. Processing queued GPS locations...');
        window.EmployeeGpsTracker.processQueuedLocations();
    });

    window.addEventListener('offline', function () {
        console.log('Network connection lost. GPS locations will be queued.');
    });
}

// ============================================================
// EXPOSE FUNCTIONS TO GLOBAL SCOPE
// ============================================================
// These are called from Blazor components via JSRuntime

window.startPersistentEmployeeGps =
    function (blazorReference, employeeId, sessionId, apiEndpoint) {
        return window.EmployeeGpsTracker.startPersistentEmployeeGps(
            blazorReference, 
            employeeId, 
            sessionId, 
            apiEndpoint);
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

window.sendLocationViaHttpApi =
    function (locationData) {
        window.EmployeeGpsTracker.sendLocationViaHttpApi(locationData);
    };

window.processQueuedLocations =
    function () {
        window.EmployeeGpsTracker.processQueuedLocations();
    };

console.log('Employee GPS Tracker module loaded');
