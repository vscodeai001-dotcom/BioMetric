// ============================================================
// Payroll.Web - Shared Theme / Browser Interop
// ============================================================

// ============================================================
// THEME
// ============================================================

window.themeInterop = {
    setThemeOnBody: function (theme) {
        if (theme === 'dark') document.body.classList.add('dark');
        else document.body.classList.remove('dark');
    },
    saveTheme: function (theme) {
        try { localStorage.setItem('payroll_theme', theme); }
        catch (e) { console.warn('Unable to save theme to localStorage', e); }
    },
    loadTheme: function () {
        try { return localStorage.getItem('payroll_theme') || 'light'; }
        catch (e) { console.warn('Unable to read theme from localStorage', e); return 'light'; }
    },
    applySavedTheme: function () {
        var theme = this.loadTheme();
        this.setThemeOnBody(theme);
        return theme;
    }
};

// ============================================================
// GEOLOCATION - ROBUST CURRENT POSITION
// ============================================================

window.getCoords = async function () {

    if (!navigator.geolocation) {
        throw new Error(
            "Geolocation is not supported by this browser."
        );
    }

    function getPosition(options) {
        return new Promise(function (resolve, reject) {
            navigator.geolocation.getCurrentPosition(
                resolve,
                reject,
                options
            );
        });
    }

    function convertError(error) {

        switch (error.code) {

            case error.PERMISSION_DENIED:
                return new Error(
                    "Location permission was denied. Please allow location access."
                );

            case error.POSITION_UNAVAILABLE:
                return new Error(
                    "Location services are currently unavailable. Please turn on device Location."
                );

            case error.TIMEOUT:
                return new Error(
                    "GPS is taking too long to respond."
                );

            default:
                return new Error(
                    "Unable to determine your current location."
                );
        }
    }

    /*
     * First try a recent location. This is fast when the device
     * already has a recent GPS/network location.
     */
    try {

        const position = await getPosition({
            enableHighAccuracy: false,
            timeout: 8000,
            maximumAge: 15000
        });

        return {
            Latitude: position.coords.latitude,
            Longitude: position.coords.longitude,
            Accuracy: Number(position.coords.accuracy || 0)
        };

    } catch (firstError) {

        console.warn(
            "Normal GPS attempt failed:",
            firstError
        );
    }

    /*
     * Then request a fresh high-accuracy position.
     */
    try {

        const position = await getPosition({
            enableHighAccuracy: true,
            timeout: 15000,
            maximumAge: 0
        });

        return {
            Latitude: position.coords.latitude,
            Longitude: position.coords.longitude,
            Accuracy: Number(position.coords.accuracy || 0)
        };

    } catch (secondError) {

        console.warn(
            "High accuracy GPS attempt failed:",
            secondError
        );

        throw convertError(secondError);
    }
};

// ============================================================
// GPS ACCURACY HELPERS
// ============================================================

window.isUsableGpsAccuracy = function (accuracy, maximumMeters) {
    const value = Number(accuracy);
    const max = Number(maximumMeters) || 150;

    return Number.isFinite(value) &&
        value >= 0 &&
        value <= max;
};

window.formatGpsAccuracy = function (accuracy) {
    const value = Number(accuracy);

    if (!Number.isFinite(value) || value <= 0) {
        return '-';
    }

    return Math.round(value) + ' m';
};

// ============================================================
// LIVE MOBILE PUNCH LOCATION TRACKING
// ============================================================

window.mobilePunchLocationWatch = {

    watchId: null,

    dotNetReference: null,

    lastLatitude: null,

    lastLongitude: null,

    lastCallbackTime: 0,

    minimumMovementMeters: 3,

    maximumUpdateIntervalMs: 10000,


    start: function (dotNetReference) {

        this.stop();


        if (!navigator.geolocation) {

            console.warn(
                "Geolocation is not supported by this browser."
            );

            return false;
        }


        this.dotNetReference =
            dotNetReference;

        this.lastLatitude = null;

        this.lastLongitude = null;

        this.lastCallbackTime = 0;


        const self = this;


        this.watchId =
            navigator.geolocation.watchPosition(

                function (position) {

                    const latitude =
                        position.coords.latitude;

                    const longitude =
                        position.coords.longitude;

                    const now =
                        Date.now();


                    let shouldUpdate =
                        self.lastLatitude === null ||
                        self.lastLongitude === null;


                    if (!shouldUpdate) {

                        const movement =
                            self.calculateDistanceMeters(
                                self.lastLatitude,
                                self.lastLongitude,
                                latitude,
                                longitude
                            );

                        const elapsed =
                            now -
                            self.lastCallbackTime;


                        shouldUpdate =
                            movement >=
                            self.minimumMovementMeters ||
                            elapsed >=
                            self.maximumUpdateIntervalMs;
                    }


                    if (!shouldUpdate) {
                        return;
                    }


                    self.lastLatitude =
                        latitude;

                    self.lastLongitude =
                        longitude;

                    self.lastCallbackTime =
                        now;


                    if (self.dotNetReference) {

                        self.dotNetReference
                            .invokeMethodAsync(
                                "UpdateLiveLocation",
                                {
                                    Latitude:
                                        latitude,

                                    Longitude:
                                        longitude,

                                    Accuracy:
                                        Number(
                                            position.coords.accuracy || 0
                                        )
                                }
                            )
                            .catch(function (error) {

                                console.warn(
                                    "Live location callback failed:",
                                    error
                                );

                            });
                    }

                },

                function (error) {

                    /*
                     * A watch timeout is not necessarily
                     * a dead tracking session.
                     *
                     * Do not kill the Blazor live state here.
                     */

                    let message =
                        "Unable to track your location.";

                    switch (error.code) {

                        case error.PERMISSION_DENIED:

                            message =
                                "Location permission was denied.";

                            break;

                        case error.POSITION_UNAVAILABLE:

                            message =
                                "Your location is temporarily unavailable.";

                            break;

                        case error.TIMEOUT:

                            message =
                                "GPS temporarily timed out. Retrying...";

                            break;
                    }


                    console.warn(
                        "GPS watch:",
                        message
                    );


                    if (self.dotNetReference) {

                        self.dotNetReference
                            .invokeMethodAsync(
                                "LocationWatchError",
                                message
                            )
                            .catch(function (callbackError) {

                                console.warn(
                                    "Location error callback failed:",
                                    callbackError
                                );

                            });
                    }

                },

                {
                    enableHighAccuracy: true,

                    /*
                     * Increased from 10 seconds.
                     */
                    timeout: 30000,

                    /*
                     * Allow a recent position.
                     */
                    maximumAge: 10000
                }
            );


        return true;
    },


    stop: function () {

        if (this.watchId !== null) {

            try {

                navigator.geolocation.clearWatch(
                    this.watchId
                );

            }
            catch (e) {

                console.warn(
                    "Unable to stop location watcher:",
                    e
                );

            }
        }


        this.watchId = null;

        this.dotNetReference = null;

        this.lastLatitude = null;

        this.lastLongitude = null;

        this.lastCallbackTime = 0;
    },


    calculateDistanceMeters: function (
        lat1,
        lon1,
        lat2,
        lon2
    ) {

        const earthRadius =
            6371000;

        const dLat =
            (lat2 - lat1) *
            Math.PI / 180;

        const dLon =
            (lon2 - lon1) *
            Math.PI / 180;

        const rLat1 =
            lat1 *
            Math.PI / 180;

        const rLat2 =
            lat2 *
            Math.PI / 180;

        const a =
            Math.sin(dLat / 2) *
            Math.sin(dLat / 2) +
            Math.cos(rLat1) *
            Math.cos(rLat2) *
            Math.sin(dLon / 2) *
            Math.sin(dLon / 2);

        const c =
            2 *
            Math.atan2(
                Math.sqrt(a),
                Math.sqrt(1 - a)
            );

        return earthRadius * c;
    }
};


window.startMobilePunchLocationWatch =
    function (dotNetReference) {

        if (!window.mobilePunchLocationWatch) {

            console.error(
                "mobilePunchLocationWatch is not initialized."
            );

            return false;
        }

        return window.mobilePunchLocationWatch.start(
            dotNetReference
        );
    };


window.stopMobilePunchLocationWatch =
    function () {

        if (window.mobilePunchLocationWatch) {

            window.mobilePunchLocationWatch.stop();

        }
    };

// ============================================================
// PERSISTENT EMPLOYEE GPS TRACKING
// ============================================================
//
// IMPORTANT:
//
// This watcher belongs to EmployeeLayout / EmployeeGpsTracker,
// NOT MobilePunchWidget.
//
// Therefore navigating:
//
// Home
// Attendance
// Payslips
// Leave
// Advances
// Bonuses
//
// does NOT stop GPS.
//
// Only explicit logout / employee portal destruction stops it.
// ============================================================

window.persistentEmployeeGps = {

    watchId: null,

    dotNetReference: null,

    lastLatitude: null,

    lastLongitude: null,

    lastCallbackTime: 0,

    minimumMovementMeters: 3,

    maximumUpdateIntervalMs: 10000,

    start: function (dotNetReference) {

        /*
         * If already running, don't create another watcher.
         */
        if (this.watchId !== null) {

            this.dotNetReference =
                dotNetReference;

            return true;
        }

        if (!navigator.geolocation) {

            console.warn(
                "Persistent employee GPS is not supported."
            );

            return false;
        }

        this.dotNetReference =
            dotNetReference;

        this.lastLatitude = null;

        this.lastLongitude = null;

        this.lastCallbackTime = 0;

        const self = this;

        this.watchId =
            navigator.geolocation.watchPosition(

                function (position) {

                    const latitude =
                        Number(position.coords.latitude);

                    const longitude =
                        Number(position.coords.longitude);

                    const accuracy =
                        Number(
                            position.coords.accuracy || 0
                        );

                    const now =
                        Date.now();

                    if (
                        !Number.isFinite(latitude) ||
                        !Number.isFinite(longitude)
                    ) {
                        return;
                    }

                    let shouldUpdate =
                        self.lastLatitude === null ||
                        self.lastLongitude === null;

                    if (!shouldUpdate) {

                        const movement =
                            self.calculateDistanceMeters(
                                self.lastLatitude,
                                self.lastLongitude,
                                latitude,
                                longitude
                            );

                        const elapsed =
                            now -
                            self.lastCallbackTime;

                        shouldUpdate =
                            movement >=
                            self.minimumMovementMeters ||
                            elapsed >=
                            self.maximumUpdateIntervalMs;
                    }

                    if (!shouldUpdate) {
                        return;
                    }

                    self.lastLatitude =
                        latitude;

                    self.lastLongitude =
                        longitude;

                    self.lastCallbackTime =
                        now;

                    if (!self.dotNetReference) {
                        return;
                    }

                    self.dotNetReference
                        .invokeMethodAsync(
                            "UpdatePersistentEmployeeLocation",
                            {
                                Latitude: latitude,
                                Longitude: longitude,
                                Accuracy: accuracy
                            }
                        )
                        .catch(function (error) {

                            /*
                             * Do NOT stop the browser watcher
                             * just because a Blazor callback
                             * temporarily failed.
                             */

                            console.warn(
                                "Persistent GPS callback failed:",
                                error
                            );
                        });
                },

                function (error) {

                    let message =
                        "Unable to track your location.";

                    switch (error.code) {

                        case error.PERMISSION_DENIED:

                            message =
                                "Location permission was denied.";

                            break;

                        case error.POSITION_UNAVAILABLE:

                            message =
                                "Your location is temporarily unavailable.";

                            break;

                        case error.TIMEOUT:

                            message =
                                "GPS temporarily timed out. Retrying...";

                            break;
                    }

                    console.warn(
                        "Persistent employee GPS:",
                        message
                    );

                    /*
                     * IMPORTANT:
                     *
                     * Do not clear the last server location.
                     *
                     * Admin will automatically transition:
                     *
                     * LIVE → STALE → OFFLINE
                     */
                    if (self.dotNetReference) {

                        self.dotNetReference
                            .invokeMethodAsync(
                                "PersistentEmployeeGpsError",
                                message
                            )
                            .catch(function (callbackError) {

                                console.warn(
                                    "Persistent GPS error callback failed:",
                                    callbackError
                                );

                            });
                    }
                },

                {
                    enableHighAccuracy: true,

                    timeout: 30000,

                    maximumAge: 10000
                }
            );

        return true;
    },

    stop: function () {

        if (this.watchId !== null) {

            try {

                navigator.geolocation.clearWatch(
                    this.watchId
                );

            }
            catch (e) {

                console.warn(
                    "Unable to stop persistent employee GPS:",
                    e
                );
            }
        }

        this.watchId = null;

        this.dotNetReference = null;

        this.lastLatitude = null;

        this.lastLongitude = null;

        this.lastCallbackTime = 0;
    },

    calculateDistanceMeters: function (
        lat1,
        lon1,
        lat2,
        lon2
    ) {

        const earthRadius =
            6371000;

        const dLat =
            (lat2 - lat1) *
            Math.PI / 180;

        const dLon =
            (lon2 - lon1) *
            Math.PI / 180;

        const rLat1 =
            lat1 *
            Math.PI / 180;

        const rLat2 =
            lat2 *
            Math.PI / 180;

        const a =
            Math.sin(dLat / 2) *
            Math.sin(dLat / 2) +
            Math.cos(rLat1) *
            Math.cos(rLat2) *
            Math.sin(dLon / 2) *
            Math.sin(dLon / 2);

        const c =
            2 *
            Math.atan2(
                Math.sqrt(a),
                Math.sqrt(1 - a)
            );

        return earthRadius * c;
    }
};


// ============================================================
// START PERSISTENT EMPLOYEE GPS
// ============================================================

window.startPersistentEmployeeGps =
    function (dotNetReference) {

        if (!window.persistentEmployeeGps) {

            console.error(
                "Persistent employee GPS is not initialized."
            );

            return false;
        }

        return window.persistentEmployeeGps.start(
            dotNetReference
        );
    };

// ============================================================
// EMPLOYEE GPS BROWSER SESSION ID
// ============================================================
//
// IMPORTANT:
//
// sessionStorage is destroyed when a browser tab is closed.
//
// localStorage is used here because we want:
//
//     Login
//       ↓
//     GPS Session A
//       ↓
//     Close tab
//       ↓
//     Open tab again
//       ↓
//     GPS Session A
//
// The key is employee-specific.
//
// The value is cleared ONLY during real logout.
// ============================================================

window.getOrCreateEmployeeGpsSessionId =
    function (storageKey, employeeId) {

        const baseKey =
            storageKey ||
            "payroll_employee_gps_session_id";

        const employeeKey =
            Number(employeeId) > 0
                ? baseKey + "_" + Number(employeeId)
                : baseKey;

        try {

            let existing =
                window.localStorage.getItem(
                    employeeKey
                );

            if (existing) {

                const parsed =
                    String(existing).trim();

                /*
                 * Validate GUID.
                 */
                if (
                    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
                        .test(parsed)
                ) {
                    return parsed;
                }

                /*
                 * Invalid value.
                 */
                window.localStorage.removeItem(
                    employeeKey
                );
            }

            /*
             * Generate UUID v4.
             */
            const bytes =
                new Uint8Array(16);

            if (
                window.crypto &&
                window.crypto.getRandomValues
            ) {
                window.crypto.getRandomValues(
                    bytes
                );
            }
            else {
                for (
                    let i = 0;
                    i < bytes.length;
                    i++
                ) {
                    bytes[i] =
                        Math.floor(
                            Math.random() * 256
                        );
                }
            }

            /*
             * RFC 4122 UUID v4.
             */
            bytes[6] =
                (bytes[6] & 0x0f) | 0x40;

            bytes[8] =
                (bytes[8] & 0x3f) | 0x80;

            const hex =
                Array.from(
                    bytes,
                    function (b) {
                        return b
                            .toString(16)
                            .padStart(2, "0");
                    }
                );

            const id =
                hex.slice(0, 4).join("") + "-" +
                hex.slice(4, 6).join("") + "-" +
                hex.slice(6, 8).join("") + "-" +
                hex.slice(8, 10).join("") + "-" +
                hex.slice(10, 16).join("");

            window.localStorage.setItem(
                employeeKey,
                id
            );

            return id;
        }
        catch (error) {

            console.warn(
                "Unable to use localStorage for employee GPS session:",
                error
            );

            /*
             * Last-resort in-memory fallback.
             */
            const fallbackKey =
                "__payrollFallbackGpsSessionId_" +
                String(employeeId || "unknown");

            if (
                window[fallbackKey]
            ) {
                return window[fallbackKey];
            }

            const fallbackBytes =
                new Uint8Array(16);

            if (
                window.crypto &&
                window.crypto.getRandomValues
            ) {
                window.crypto.getRandomValues(
                    fallbackBytes
                );
            }
            else {
                for (
                    let i = 0;
                    i < fallbackBytes.length;
                    i++
                ) {
                    fallbackBytes[i] =
                        Math.floor(
                            Math.random() * 256
                        );
                }
            }

            fallbackBytes[6] =
                (fallbackBytes[6] & 0x0f) | 0x40;

            fallbackBytes[8] =
                (fallbackBytes[8] & 0x3f) | 0x80;

            const fallbackHex =
                Array.from(
                    fallbackBytes,
                    function (b) {
                        return b
                            .toString(16)
                            .padStart(2, "0");
                    }
                );

            const fallback =
                fallbackHex.slice(0, 4).join("") + "-" +
                fallbackHex.slice(4, 6).join("") + "-" +
                fallbackHex.slice(6, 8).join("") + "-" +
                fallbackHex.slice(8, 10).join("") + "-" +
                fallbackHex.slice(10, 16).join("");

            window[fallbackKey] =
                fallback;

            return fallback;
        }
    };


// ============================================================
// CREATE COMPLETELY NEW GPS SESSION
// ============================================================
//
// Used when the previously stored SessionId has already ended.
//
// Example:
//
// Session A
//     ↓
// TIMED_OUT
//     ↓
// Employee opens portal
//     ↓
// Remove Session A
//     ↓
// Create Session B
// ============================================================

window.createNewEmployeeGpsSessionId =
    function (storageKey, employeeId) {

        const baseKey =
            storageKey ||
            "payroll_employee_gps_session_id";

        const employeeKey =
            Number(employeeId) > 0
                ? baseKey + "_" + Number(employeeId)
                : baseKey;

        try {

            window.localStorage.removeItem(
                employeeKey
            );

        }
        catch (error) {

            console.warn(
                "Unable to remove old employee GPS session:",
                error
            );
        }

        /*
         * Also remove fallback.
         */
        try {

            delete window[
                "__payrollFallbackGpsSessionId_" +
                String(employeeId || "unknown")
            ];

        }
        catch {
        }

        return window.getOrCreateEmployeeGpsSessionId(
            baseKey,
            employeeId
        );
    };


// ============================================================
// CLEAR EMPLOYEE GPS SESSION
// ============================================================
//
// ONLY call this during REAL LOGOUT.
//
// Do NOT call this from:
//     DisposeAsync()
//     circuit disconnect
//     network failure
//     GPS failure
// ============================================================

window.clearEmployeeGpsSessionId =
    function (storageKey, employeeId) {

        const baseKey =
            storageKey ||
            "payroll_employee_gps_session_id";

        const employeeKey =
            Number(employeeId) > 0
                ? baseKey + "_" + Number(employeeId)
                : baseKey;

        try {

            window.localStorage.removeItem(
                employeeKey
            );

        }
        catch (error) {

            console.warn(
                "Unable to clear employee GPS session ID:",
                error
            );
        }

        try {

            delete window[
                "__payrollFallbackGpsSessionId_" +
                String(employeeId || "unknown")
            ];

        }
        catch {
        }
    };

window.submitEmployeeLogoutForm = function (formId) {

    const form = document.getElementById(formId);

    if (!form) {
        console.error(
            "Logout form not found:",
            formId
        );
        return;
    }

    // ------------------------------------------------------------
    // REAL LOGOUT ONLY
    //
    // Clear persistent employee GPS session IDs.
    //
    // We intentionally do NOT clear these during:
    // - tab close
    // - page navigation
    // - circuit disconnect
    // - network interruption
    // - GPS failure
    // ------------------------------------------------------------

    try {

        const prefix =
            "payroll_employee_gps_session_id_";

        const keysToRemove = [];

        for (
            let i = 0;
            i < window.localStorage.length;
            i++
        ) {

            const key =
                window.localStorage.key(i);

            if (
                key &&
                key.startsWith(prefix)
            ) {
                keysToRemove.push(key);
            }
        }

        keysToRemove.forEach(
            function (key) {

                try {
                    window.localStorage.removeItem(key);
                }
                catch (error) {
                    console.warn(
                        "Unable to remove GPS session key:",
                        key,
                        error
                    );
                }

            }
        );

        // Also clear the old non-employee-specific key
        try {
            window.localStorage.removeItem(
                "payroll_employee_gps_session_id"
            );
        }
        catch {
        }

    }
    catch (error) {

        console.warn(
            "Unable to clear employee GPS browser sessions:",
            error
        );
    }

    // ------------------------------------------------------------
    // Finally submit the Identity logout form.
    // ------------------------------------------------------------

    form.submit();
};

// ============================================================
// STOP PERSISTENT EMPLOYEE GPS
// ============================================================

window.stopPersistentEmployeeGps =
    function () {

        if (
            window.persistentEmployeeGps
        ) {

            window.persistentEmployeeGps.stop();

        }
    };

// ============================================================
// GPS CLEANUP
// ============================================================

window.stopAllPayrollGps = function () {

    try {

        if (window.persistentEmployeeGps) {
            window.persistentEmployeeGps.stop();
        }

    }
    catch (e) {

        console.warn(
            "Unable to stop persistent employee GPS:",
            e
        );

    }

    try {

        if (window.mobilePunchLocationWatch) {
            window.mobilePunchLocationWatch.stop();
        }

    }
    catch (e) {

        console.warn(
            "Unable to stop mobile punch GPS:",
            e
        );

    }

    try {

        Object.keys(
            window.adminHistoryPlayback || {}
        ).forEach(
            function (mapId) {

                window.stopAdminHistoryPlayback(
                    mapId
                );

            }
        );

    }
    catch (e) {

        console.warn(
            "Unable to stop GPS playback:",
            e
        );

    }
};

// ============================================================
// PRINT
// ============================================================

window.PrintElement = function (elementId) {
    const elementToPrint =
        document.getElementById(elementId);

    if (!elementToPrint) {
        console.error(
            'Element to print not found:',
            elementId
        );
        return;
    }

    elementToPrint.classList.add(
        'printable-payslip'
    );

    window.print();

    setTimeout(function () {
        elementToPrint.classList.remove(
            'printable-payslip'
        );
    }, 500);
};

// ============================================================
// DOWNLOAD FILE
// ============================================================

window.downloadFileFromStream =
    async function (
        fileName,
        contentStreamReference
    ) {
        const arrayBuffer =
            await contentStreamReference.arrayBuffer();

        const blob =
            new Blob([arrayBuffer]);

        const url =
            URL.createObjectURL(blob);

        const anchorElement =
            document.createElement('a');

        anchorElement.href = url;
        anchorElement.download = fileName ?? '';

        document.body.appendChild(
            anchorElement
        );

        anchorElement.click();
        anchorElement.remove();

        URL.revokeObjectURL(url);
    };

// ============================================================
// BOOTSTRAP DROPDOWNS
// ============================================================

window.initBootstrapDropdowns =
    function () {
        var dropdownElementList =
            document.querySelectorAll(
                '[data-bs-toggle="dropdown"]'
            );

        if (
            typeof bootstrap === 'undefined' ||
            !bootstrap.Dropdown
        ) {
            return;
        }

        dropdownElementList.forEach(
            function (dropdownToggleEl) {
                bootstrap.Dropdown
                    .getOrCreateInstance(
                        dropdownToggleEl
                    );
            }
        );
    };

// ============================================================
// LEAFLET SHARED LOADER
// ============================================================

window.payrollLeafletPromise = null;

window.loadPayrollLeaflet =
    function () {
        if (window.L) {
            return Promise.resolve();
        }

        if (window.payrollLeafletPromise) {
            return window.payrollLeafletPromise;
        }

        window.payrollLeafletPromise =
            new Promise(
                function (resolve, reject) {
                    if (
                        !document.querySelector(
                            'link[data-payroll-leaflet]'
                        )
                    ) {
                        const css =
                            document.createElement(
                                'link'
                            );

                        css.rel = 'stylesheet';

                        css.href =
                            'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.css';

                        css.dataset.payrollLeaflet =
                            '1';

                        document.head.appendChild(
                            css
                        );
                    }

                    const script =
                        document.createElement(
                            'script'
                        );

                    script.src =
                        'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.js';

                    script.onload =
                        function () {
                            resolve();
                        };

                    script.onerror =
                        function () {
                            window.payrollLeafletPromise = null;
                            reject(
                                new Error(
                                    'Unable to load map library.'
                                )
                            );
                        };

                    document.head.appendChild(
                        script
                    );
                }
            );

        return window.payrollLeafletPromise;
    };

// ============================================================
// SMOOTH LIVE GPS MOVEMENT
// ============================================================
// The GPS/network layer intentionally reports real coordinates at a
// controlled rate.  These helpers only interpolate the marker between
// real GPS points in the browser.  They never invent a new GPS point or
// write anything to the database.
// ============================================================

window.payrollGeoAnimationState =
    window.payrollGeoAnimationState || {};

window.payrollSmoothMoveMarker =
    function (marker, key, target, durationMs, onFrame) {
        if (!marker || !Array.isArray(target) || target.length < 2) {
            return;
        }

        const stateStore = window.payrollGeoAnimationState;
        const previous = stateStore[key];

        if (previous && previous.frame) {
            try {
                cancelAnimationFrame(previous.frame);
            }
            catch { }
        }

        const startLatLng = marker.getLatLng();
        const start = [
            Number(startLatLng.lat),
            Number(startLatLng.lng)
        ];

        const end = [
            Number(target[0]),
            Number(target[1])
        ];

        if (
            !Number.isFinite(start[0]) ||
            !Number.isFinite(start[1]) ||
            !Number.isFinite(end[0]) ||
            !Number.isFinite(end[1])
        ) {
            marker.setLatLng(end);
            if (typeof onFrame === 'function') {
                onFrame(end);
            }
            return;
        }

        const deltaLat = end[0] - start[0];
        const deltaLng = end[1] - start[1];

        if (
            Math.abs(deltaLat) < 0.00000001 &&
            Math.abs(deltaLng) < 0.00000001
        ) {
            marker.setLatLng(end);
            if (typeof onFrame === 'function') {
                onFrame(end);
            }
            return;
        }

        const duration = Math.max(
            250,
            Math.min(
                6000,
                Number(durationMs) || 4000
            )
        );

        const startedAt = performance.now();
        const animation = {
            frame: 0
        };

        stateStore[key] = animation;

        function step(now) {
            const raw = Math.min(
                1,
                Math.max(
                    0,
                    (now - startedAt) / duration
                )
            );

            // Smooth but constant-looking travel between GPS fixes.
            const progress =
                raw < 0.5
                    ? 2 * raw * raw
                    : 1 - Math.pow(-2 * raw + 2, 2) / 2;

            const position = [
                start[0] + deltaLat * progress,
                start[1] + deltaLng * progress
            ];

            marker.setLatLng(position);

            if (typeof onFrame === 'function') {
                try {
                    onFrame(position);
                }
                catch { }
            }

            if (raw < 1) {
                animation.frame =
                    requestAnimationFrame(step);
            }
            else {
                marker.setLatLng(end);

                if (typeof onFrame === 'function') {
                    try {
                        onFrame(end);
                    }
                    catch { }
                }

                if (stateStore[key] === animation) {
                    delete stateStore[key];
                }
            }
        }

        animation.frame = requestAnimationFrame(step);
    };

window.payrollCancelGeoAnimation =
    function (key) {
        const state =
            window.payrollGeoAnimationState?.[key];

        if (state?.frame) {
            try {
                cancelAnimationFrame(state.frame);
            }
            catch { }
        }

        if (window.payrollGeoAnimationState) {
            delete window.payrollGeoAnimationState[key];
        }
    };

// ============================================================
// ROAD ROUTING + JOURNEY DETAILS
// ============================================================
// Presentation-only road routing. Existing GPS, attendance, session and
// database flows remain unchanged. The routing URL is replaceable for a
// production/self-hosted routing service.

window.payrollRoutingServiceUrl = window.payrollRoutingServiceUrl || 'https://router.project-osrm.org';
window.payrollJourneyState = window.payrollJourneyState || {};

window.payrollHaversineMeters = function (a, b) {
    if (!Array.isArray(a) || !Array.isArray(b)) return 0;
    const v = [Number(a[0]), Number(a[1]), Number(b[0]), Number(b[1])];
    if (!v.every(Number.isFinite)) return 0;
    const R = 6371000;
    const dLat = (v[2] - v[0]) * Math.PI / 180;
    const dLon = (v[3] - v[1]) * Math.PI / 180;
    const p1 = v[0] * Math.PI / 180;
    const p2 = v[2] * Math.PI / 180;
    const h = Math.sin(dLat / 2) ** 2 + Math.cos(p1) * Math.cos(p2) * Math.sin(dLon / 2) ** 2;
    return 2 * R * Math.asin(Math.min(1, Math.sqrt(h)));
};

window.payrollFormatRouteDistance = function (meters) {
    const m = Number(meters) || 0;
    return m < 1000 ? `${Math.round(m)} m` : `${(m / 1000).toFixed(m >= 10000 ? 1 : 2)} km`;
};

window.payrollFormatRouteDuration = function (seconds) {
    const s = Math.max(0, Math.round(Number(seconds) || 0));
    if (s < 60) return `${s}s`;
    const minutes = Math.round(s / 60);
    if (minutes < 60) return `${minutes} min`;
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return mins ? `${hours}h ${mins}m` : `${hours}h`;
};

window.payrollFormatSpeed = function (metersPerSecond) {
    const speed = Number(metersPerSecond);
    if (!Number.isFinite(speed) || speed < 0.4) return 'Stopped';
    const kmh = speed * 3.6;
    return `${kmh.toFixed(kmh < 10 ? 1 : 0)} km/h`;
};

window.payrollEscapeHtml = function (value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;').replaceAll("'", '&#039;');
};

window.payrollFetchRoadRoute = async function (from, to, options = {}) {
    const a = [Number(from[0]), Number(from[1])];
    const b = [Number(to[0]), Number(to[1])];
    if (![...a, ...b].every(Number.isFinite)) return null;
    const base = String(window.payrollRoutingServiceUrl || '').replace(/\/$/, '');
    if (!base) return null;
    const url = `${base}/route/v1/driving/${a[1]},${a[0]};${b[1]},${b[0]}?overview=full&geometries=geojson&steps=true&annotations=false`;
    const response = await fetch(url, { method: 'GET', mode: 'cors', cache: 'no-store', signal: options.controller?.signal });
    if (!response.ok) throw new Error(`Routing service HTTP ${response.status}`);
    const data = await response.json();
    if (data.code !== 'Ok' || !data.routes?.[0]) return null;
    const route = data.routes[0];
    const geometry = (route.geometry?.coordinates || [])
        .filter(c => Array.isArray(c) && c.length >= 2)
        .map(c => [Number(c[1]), Number(c[0])])
        .filter(c => c.every(Number.isFinite));
    if (geometry.length < 2) return null;
    const steps = (route.legs || []).flatMap(leg => Array.isArray(leg.steps) ? leg.steps : [])
        .filter(step => step && (step.name || step.ref));
    return {
        distanceMeters: Number(route.distance) || 0,
        durationSeconds: Number(route.duration) || 0,
        geometry,
        steps,
        generatedAt: Date.now()
    };
};

window.payrollGetNextRoadName = function (route) {
    const step = (route?.steps || []).find(s => String(s.name || '').trim());
    if (!step) return 'Road route';
    const name = String(step.name || '').trim();
    const ref = String(step.ref || '').trim();
    return ref && ref !== name ? `${name} (${ref})` : name;
};

window.payrollCreateJourneyOverlay = function (mapElement, className) {
    if (!mapElement) return null;
    if (!document.getElementById('payroll-journey-map-global-style')) {
        const style = document.createElement('style');
        style.id = 'payroll-journey-map-global-style';
        style.textContent = `
.payroll-admin-journey-tooltip{background:transparent!important;border:0!important;box-shadow:none!important;padding:0!important;color:inherit!important}.payroll-admin-journey-tooltip:before{display:none!important}.payroll-admin-journey-label{min-width:178px;max-width:235px;padding:7px 8px;border-radius:13px;background:rgba(255,255,255,.96);border:1px solid rgba(22,136,255,.20);box-shadow:0 9px 24px rgba(15,31,55,.24),0 2px 8px rgba(15,31,55,.12);backdrop-filter:blur(12px);-webkit-backdrop-filter:blur(12px);color:#172238;font-size:9px;line-height:1.15}.payroll-admin-journey-head{display:flex;align-items:center;justify-content:space-between;gap:7px}.payroll-admin-journey-name{font-size:11px;font-weight:900;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.payroll-admin-journey-state{font-size:7px;font-weight:900;white-space:nowrap}.payroll-admin-journey-destination{margin-top:3px;color:#718096;font-size:7px;font-weight:800}.payroll-admin-journey-grid{display:grid;grid-template-columns:1fr 1fr;gap:3px;margin-top:5px}.payroll-admin-journey-grid span{display:block;padding:4px 4px;border-radius:7px;background:#f1f5fa;border:1px solid rgba(19,43,77,.07);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.payroll-admin-journey-grid b{font-weight:900}.payroll-admin-journey-tooltip .leaflet-tooltip-content{margin:0!important}[data-theme="dark"] .payroll-admin-journey-label,[data-bs-theme="dark"] .payroll-admin-journey-label{background:rgba(14,22,35,.96);border-color:rgba(79,166,255,.25);box-shadow:0 12px 28px rgba(0,0,0,.48);color:#edf5ff}.payroll-admin-journey-grid span,[data-theme="dark"] .payroll-admin-journey-grid span,[data-bs-theme="dark"] .payroll-admin-journey-grid span{color:#25354a}.payroll-admin-journey-grid span{color:#25354a}[data-theme="dark"] .payroll-admin-journey-grid span,[data-bs-theme="dark"] .payroll-admin-journey-grid span{background:rgba(29,43,61,.78);border-color:rgba(143,177,214,.12);color:#dbeaff}.payroll-admin-journey-destination{color:#718096}[data-theme="dark"] .payroll-admin-journey-destination,[data-bs-theme="dark"] .payroll-admin-journey-destination{color:#8fa4bb}@media(max-width:900px){.payroll-admin-journey-label{min-width:150px;max-width:190px;padding:6px 7px}.payroll-admin-journey-name{font-size:10px}.payroll-admin-journey-grid{gap:2px}.payroll-admin-journey-grid span{padding:3px;font-size:8px}}.admin-employee-label,.payroll-employee-name-label{background:rgba(10,18,30,.92)!important;color:#fff!important;border:1px solid rgba(255,255,255,.18)!important;border-radius:10px!important;box-shadow:0 5px 14px rgba(0,0,0,.25)!important;font-size:11px!important;font-weight:800!important;padding:4px 8px!important}.admin-distance-label{background:rgba(13,110,253,.94)!important;color:#fff!important;border:0!important;border-radius:9px!important;font-weight:800!important;padding:3px 7px!important}
.payroll-admin-card-marker{background:transparent!important;border:0!important;box-shadow:none!important;pointer-events:none!important}.payroll-admin-map-card{width:220px;min-height:86px;box-sizing:border-box;padding:9px 10px 8px;border-radius:14px;background:rgba(12,20,32,.96);border:1px solid rgba(113,180,255,.28);box-shadow:0 14px 30px rgba(0,0,0,.40),0 3px 10px rgba(0,0,0,.22);backdrop-filter:blur(12px) saturate(140%);-webkit-backdrop-filter:blur(12px) saturate(140%);color:#edf5ff;font-family:inherit;line-height:1.1}.payroll-admin-map-card .card-head{display:flex;align-items:center;gap:7px}.payroll-admin-map-card .card-avatar{width:24px;height:24px;border-radius:8px;display:grid;place-items:center;background:rgba(38,197,126,.15);color:#5de2a1;font-size:13px;flex:0 0 24px}.payroll-admin-map-card .card-name{font-size:11px;font-weight:900;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;flex:1}.payroll-admin-map-card .card-status{font-size:7px;font-weight:900;white-space:nowrap;color:#62e3a3}.payroll-admin-map-card .card-destination{margin-top:5px;font-size:8px;font-weight:800;color:#a9bbcf}.payroll-admin-map-card .card-metrics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:4px;margin-top:6px}.payroll-admin-map-card .card-metric{min-width:0;padding:5px 4px;border-radius:8px;background:rgba(35,51,71,.78);border:1px solid rgba(151,183,214,.10);text-align:center}.payroll-admin-map-card .card-value{font-size:8px;font-weight:900;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.payroll-admin-map-card .card-label{margin-top:2px;font-size:6px;text-transform:uppercase;letter-spacing:.35px;color:#8398af;font-weight:800}.payroll-admin-map-card .card-road{margin-top:5px;font-size:7px;font-weight:800;color:#a9d4ff;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.payroll-admin-map-card .card-footer{margin-top:4px;display:flex;justify-content:space-between;gap:8px;font-size:6.5px;color:#8195ab;font-weight:800}.payroll-admin-map-card .live-dot{color:#55e09c}.payroll-admin-map-card.outside .card-status{color:#ff7d7d}[data-theme="light"] .payroll-admin-map-card,[data-bs-theme="light"] .payroll-admin-map-card{background:rgba(255,255,255,.97);color:#172238;border-color:rgba(19,43,77,.12);box-shadow:0 14px 30px rgba(15,31,55,.22)}[data-theme="light"] .payroll-admin-map-card .card-destination,[data-bs-theme="light"] .payroll-admin-map-card .card-destination{color:#718096}[data-theme="light"] .payroll-admin-map-card .card-metric,[data-bs-theme="light"] .payroll-admin-map-card .card-metric{background:#f1f5fa;border-color:rgba(19,43,77,.07)}[data-theme="light"] .payroll-admin-map-card .card-value,[data-bs-theme="light"] .payroll-admin-map-card .card-value{color:#172238}@media(max-width:900px){.payroll-admin-map-card{width:190px;padding:7px 8px 7px}.payroll-admin-map-card .card-metrics{gap:3px}.payroll-admin-map-card .card-value{font-size:7.5px}}
.payroll-journey-overlay{position:absolute;left:10px;top:10px;z-index:1000;width:min(285px,calc(100% - 20px));min-width:0;max-width:calc(100% - 20px);padding:0!important;border-radius:16px!important;overflow:hidden;pointer-events:none;color:#162033;background:rgba(255,255,255,.94);border:1px solid rgba(19,43,77,.12);box-shadow:0 14px 34px rgba(15,31,55,.22),0 3px 10px rgba(15,31,55,.10);backdrop-filter:blur(18px) saturate(145%);-webkit-backdrop-filter:blur(18px) saturate(145%);font-size:10px;line-height:1.15}
.payroll-journey-card{padding:7px 8px 6px;background:linear-gradient(145deg,rgba(255,255,255,.98),rgba(244,248,253,.94));}.payroll-journey-top{display:flex;align-items:center;gap:7px;margin-bottom:5px;min-height:31px}.payroll-journey-avatar{width:30px;height:30px;display:grid;place-items:center;flex:0 0 30px;border-radius:10px;background:linear-gradient(145deg,#1688ff,#5b5df0);color:#fff;font-size:15px;box-shadow:0 5px 12px rgba(22,136,255,.28)}.payroll-journey-title{min-width:0;flex:1}.payroll-journey-name{font-size:12px;font-weight:900;letter-spacing:.1px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.payroll-journey-destination{margin-top:1px;color:#718096;font-size:8px;font-weight:800;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.payroll-journey-status{display:inline-flex;align-items:center;gap:4px;padding:4px 6px;border-radius:999px;font-size:7px;font-weight:900;letter-spacing:.2px;white-space:nowrap;background:#e9f9ef;color:#168447;border:1px solid rgba(22,132,71,.12)}.payroll-journey-status.live{background:#eaf4ff;color:#1268cf;border-color:rgba(18,104,207,.12)}.payroll-journey-status .dot{width:5px;height:5px;border-radius:50%;background:currentColor;box-shadow:0 0 0 2px rgba(22,132,71,.10)}.payroll-journey-status.live .dot{box-shadow:0 0 0 2px rgba(18,104,207,.10);animation:payrollJourneyPulse 1.5s ease-in-out infinite}@keyframes payrollJourneyPulse{0%,100%{opacity:.55;transform:scale(.85)}50%{opacity:1;transform:scale(1.1)}}.payroll-journey-progress{height:4px;border-radius:99px;background:#e8edf4;overflow:hidden;margin:1px 0 6px}.payroll-journey-progress>span{display:block;height:100%;width:68%;border-radius:inherit;background:linear-gradient(90deg,#1688ff,#35b8ff,#625cff);box-shadow:0 0 8px rgba(22,136,255,.28)}.payroll-journey-metrics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:4px}.payroll-journey-metric{min-width:0;min-height:29px;padding:4px 4px 3px;border-radius:9px;background:rgba(244,247,251,.92);border:1px solid rgba(19,43,77,.07);text-align:center}.payroll-journey-icon{font-size:10px;line-height:1;margin-bottom:2px}.payroll-journey-label{font-size:6.5px;text-transform:uppercase;letter-spacing:.35px;font-weight:800;color:#8491a5;line-height:1}.payroll-journey-value{margin-top:2px;font-size:9px;line-height:1;font-weight:900;color:#172238;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.payroll-journey-road{display:flex;align-items:center;gap:5px;margin-top:5px;padding:5px 6px;border-radius:9px;background:rgba(22,136,255,.07);border:1px solid rgba(22,136,255,.10);color:#2f5f8e;min-height:22px}.payroll-journey-road-icon{font-size:11px}.payroll-journey-road-text{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:800;font-size:8px}.payroll-journey-road-caption{display:inline;color:#8292a7;font-size:6px;text-transform:uppercase;letter-spacing:.3px;font-weight:800;margin-right:3px}.payroll-journey-footer{display:flex;justify-content:space-between;align-items:center;margin-top:4px;color:#8491a5;font-size:6.5px;font-weight:800}.payroll-journey-live-dot{color:#18a058}.payroll-journey-arrived .payroll-journey-avatar{background:linear-gradient(145deg,#19a765,#0f8f7a);box-shadow:0 5px 12px rgba(25,167,101,.25)}[data-theme="dark"] .payroll-journey-overlay,[data-bs-theme="dark"] .payroll-journey-overlay{color:#e9f1fb;background:rgba(14,22,35,.91);border-color:rgba(143,177,214,.18);box-shadow:0 18px 44px rgba(0,0,0,.46),0 3px 12px rgba(0,0,0,.28)}[data-theme="dark"] .payroll-journey-card,[data-bs-theme="dark"] .payroll-journey-card{background:linear-gradient(145deg,rgba(19,29,45,.97),rgba(11,20,34,.94))}[data-theme="dark"] .payroll-journey-destination,[data-bs-theme="dark"] .payroll-journey-destination,[data-theme="dark"] .payroll-journey-label,[data-bs-theme="dark"] .payroll-journey-label,[data-theme="dark"] .payroll-journey-footer,[data-bs-theme="dark"] .payroll-journey-footer{color:#8fa4bb}[data-theme="dark"] .payroll-journey-metric,[data-bs-theme="dark"] .payroll-journey-metric{background:rgba(28,41,59,.74);border-color:rgba(143,177,214,.12)}[data-theme="dark"] .payroll-journey-value,[data-bs-theme="dark"] .payroll-journey-value{color:#edf5ff}[data-theme="dark"] .payroll-journey-progress,[data-bs-theme="dark"] .payroll-journey-progress{background:#263548}[data-theme="dark"] .payroll-journey-road,[data-bs-theme="dark"] .payroll-journey-road{background:rgba(44,145,255,.11);border-color:rgba(44,145,255,.18);color:#a8d3ff}[data-theme="dark"] .payroll-journey-status.live,[data-bs-theme="dark"] .payroll-journey-status.live{background:rgba(39,139,255,.15);color:#7dc0ff;border-color:rgba(39,139,255,.22)}[data-theme="dark"] .payroll-journey-status,[data-bs-theme="dark"] .payroll-journey-status{background:rgba(35,176,108,.14);color:#6ee2a7;border-color:rgba(35,176,108,.20)}@media (max-width:520px){.payroll-journey-overlay{left:7px;top:7px;width:calc(100% - 14px);max-width:calc(100% - 14px);border-radius:14px!important}.payroll-journey-card{padding:6px 7px 5px}.payroll-journey-top{gap:6px;margin-bottom:4px}.payroll-journey-avatar{width:28px;height:28px;flex-basis:28px;border-radius:9px;font-size:14px}.payroll-journey-name{font-size:11px}.payroll-journey-destination{font-size:7px}.payroll-journey-status{font-size:6.5px;padding:3px 5px}.payroll-journey-metrics{gap:3px}.payroll-journey-metric{padding:4px 3px;min-height:28px}.payroll-journey-value{font-size:8.5px}}
`;
        document.head.appendChild(style);
    }
    let overlay = mapElement.querySelector(`.${className}`);
    if (overlay) return overlay;
    mapElement.style.position = 'relative';
    overlay = document.createElement('div');
    overlay.className = `${className} payroll-journey-overlay`;
    mapElement.appendChild(overlay);
    return overlay;
};

window.payrollRenderJourneyOverlay = function (overlay, data) {
    if (!overlay) return;
    const name = window.payrollEscapeHtml(data.name || 'Employee');
    const road = window.payrollEscapeHtml(data.road || 'Calculating road route...');
    const distance = window.payrollFormatRouteDistance(data.distanceMeters);
    const eta = data.durationSeconds > 0 ? window.payrollFormatRouteDuration(data.durationSeconds) : 'Calculating...';
    const speed = window.payrollFormatSpeed(data.speedMps);
    const accuracy = Number(data.accuracyMeters) > 0 ? `±${Math.round(Number(data.accuracyMeters))} m` : 'Unknown';
    const elapsed = data.journeyStartedAt ? window.payrollFormatRouteDuration((Date.now() - data.journeyStartedAt) / 1000) : '0s';
    const status = data.arrived ? 'ARRIVED' : 'LIVE';
    const statusClass = data.arrived ? '' : ' live';
    const cardClass = data.arrived ? ' payroll-journey-arrived' : '';
    overlay.innerHTML =
        `<div class="payroll-journey-card${cardClass}">` +
        `<div class="payroll-journey-top">` +
        `<div class="payroll-journey-avatar">${data.arrived ? '🏁' : '🛵'}</div>` +
        `<div class="payroll-journey-title"><div class="payroll-journey-name">${name}</div><div class="payroll-journey-destination">📍 Destination • To Office</div></div>` +
        `<div class="payroll-journey-status${statusClass}"><span class="dot"></span>${status}</div>` +
        `</div>` +
        `<div class="payroll-journey-progress"><span></span></div>` +
        `<div class="payroll-journey-metrics">` +
        `<div class="payroll-journey-metric"><div class="payroll-journey-icon">📏</div><div class="payroll-journey-label">Remaining</div><div class="payroll-journey-value">${distance}</div></div>` +
        `<div class="payroll-journey-metric"><div class="payroll-journey-icon">⏱️</div><div class="payroll-journey-label">ETA</div><div class="payroll-journey-value">${eta}</div></div>` +
        `<div class="payroll-journey-metric"><div class="payroll-journey-icon">🚦</div><div class="payroll-journey-label">Speed</div><div class="payroll-journey-value">${window.payrollEscapeHtml(speed)}</div></div>` +
        `<div class="payroll-journey-metric"><div class="payroll-journey-icon">🎯</div><div class="payroll-journey-label">Accuracy</div><div class="payroll-journey-value">${accuracy}</div></div>` +
        `<div class="payroll-journey-metric"><div class="payroll-journey-icon">🕐</div><div class="payroll-journey-label">Journey</div><div class="payroll-journey-value">${elapsed}</div></div>` +
        `<div class="payroll-journey-metric"><div class="payroll-journey-icon">🛣️</div><div class="payroll-journey-label">Route</div><div class="payroll-journey-value">Road</div></div>` +
        `</div>` +
        `<div class="payroll-journey-road"><span class="payroll-journey-road-icon">🛣️</span><div class="payroll-journey-road-text"><span class="payroll-journey-road-caption">Current road</span>${road}</div></div>` +
        `<div class="payroll-journey-footer"><span class="payroll-journey-live-dot">● GPS LIVE</span><span>🏢 Office destination</span></div>` +
        `</div>`;
};

window.payrollRequestJourneyRoute = async function (state, from, to, options = {}) {
    if (!state) return null;
    state.routeState = state.routeState || {};
    const rs = state.routeState;
    const now = Date.now();
    const minMove = Number(options.minMoveMeters) || 20;
    const minInterval = Number(options.minIntervalMs) || 20000;
    const moved = rs.lastRoutedPosition ? window.payrollHaversineMeters(rs.lastRoutedPosition, from) : Infinity;
    if (rs.pending) return rs.route || null;
    if (rs.route && now - (rs.lastRequestedAt || 0) < minInterval) return rs.route;
    rs.pending = true;
    rs.lastRequestedAt = now;
    rs.controller?.abort();
    rs.controller = new AbortController();
    try {
        const route = await window.payrollFetchRoadRoute(from, to, { controller: rs.controller });
        if (route) { rs.route = route; rs.lastRoutedPosition = [from[0], from[1]]; }
        return route || rs.route || null;
    } catch (error) {
        if (error?.name !== 'AbortError') console.warn('Road route unavailable:', error);
        return rs.route || null;
    } finally { rs.pending = false; }
};

// ============================================================
// EMPLOYEE GEO MAP
// FINAL ROBUST VERSION
// ============================================================

window.payrollGeoMaps =
    window.payrollGeoMaps || {};

window.updateGeoMap = async function (
    mapId,
    officeLat,
    officeLng,
    userLat,
    userLng,
    radius,
    isWithin,
    employeeName,
    sessionStartedIso,
    accuracyMeters
) {
    const officeLatitude = Number(officeLat);
    const officeLongitude = Number(officeLng);
    const userLatitude = Number(userLat);
    const userLongitude = Number(userLng);
    const allowedRadius = Number(radius) || 100;

    // --------------------------------------------------------
    // VALIDATE COORDINATES
    // --------------------------------------------------------

    if (
        !Number.isFinite(officeLatitude) ||
        !Number.isFinite(officeLongitude) ||
        !Number.isFinite(userLatitude) ||
        !Number.isFinite(userLongitude)
    ) {
        console.warn(
            "Employee geo map: invalid coordinates."
        );

        return false;
    }

    // --------------------------------------------------------
    // FIND MAP CONTAINER
    // --------------------------------------------------------

    const mapElement =
        document.getElementById(mapId);

    if (!mapElement) {
        console.warn(
            "Employee geo map element not found:",
            mapId
        );

        return false;
    }

    try {

        // ----------------------------------------------------
        // LOAD LEAFLET
        // ----------------------------------------------------

        await window.loadPayrollLeaflet();

        if (!window.L) {
            throw new Error(
                "Leaflet library is not available."
            );
        }

        const office = [
            officeLatitude,
            officeLongitude
        ];

        const user = [
            userLatitude,
            userLongitude
        ];

        let mapData =
            window.payrollGeoMaps[mapId];

        // ----------------------------------------------------
        // PROTECT AGAINST BLOZOR DOM REPLACEMENT
        // ----------------------------------------------------

        if (
            mapData &&
            mapData.map &&
            mapData.map.getContainer() !== mapElement
        ) {
            try {
                mapData.map.remove();
            }
            catch {
            }

            delete window.payrollGeoMaps[mapId];

            mapData = null;
        }

        // ----------------------------------------------------
        // CREATE MAP
        // ----------------------------------------------------

        if (!mapData) {

            // Remove any stale Leaflet state attached
            // to this exact DOM element.
            if (mapElement._leaflet_id) {
                try {
                    delete mapElement._leaflet_id;
                }
                catch {
                }
            }

            const map =
                L.map(
                    mapElement,
                    {
                        zoomControl: true,
                        attributionControl: true,
                        preferCanvas: false
                    }
                );

            // ------------------------------------------------
            // OPEN STREET MAP
            // ------------------------------------------------

            const tileLayer =
                L.tileLayer(
                    "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
                    {
                        maxZoom: 19,
                        attribution:
                            "© OpenStreetMap contributors"
                    }
                );

            tileLayer.addTo(map);

            // ------------------------------------------------
            // OFFICE ICON
            // ------------------------------------------------

            const officeIcon =
                L.divIcon({
                    className:
                        "payroll-office-marker",

                    html:
                        '<div class="payroll-map-office">' +
                        '<i class="bi bi-building-fill"></i>' +
                        '</div>',

                    iconSize: [
                        36,
                        36
                    ],

                    iconAnchor: [
                        18,
                        18
                    ]
                });

            // ------------------------------------------------
            // USER ICON
            // ------------------------------------------------

            const userIcon =
                L.divIcon({
                    className:
                        "payroll-user-marker",

                    html:
                        '<div class="payroll-map-user">' +
                        '<i class="bi bi-geo-alt-fill"></i>' +
                        '</div>',

                    iconSize: [
                        40,
                        40
                    ],

                    iconAnchor: [
                        20,
                        36
                    ]
                });

            // ------------------------------------------------
            // OFFICE MARKER
            // ------------------------------------------------

            const officeMarker =
                L.marker(
                    office,
                    {
                        icon:
                            officeIcon
                    }
                )
                    .addTo(map);

            officeMarker.bindPopup(
                "<b>OFFICE</b><br>Configured location"
            );

            // ------------------------------------------------
            // USER MARKER
            // ------------------------------------------------

            const userMarker =
                L.marker(
                    user,
                    {
                        icon:
                            userIcon
                    }
                )
                    .addTo(map);

            userMarker.bindPopup(
                "<b>YOU</b><br>Current location"
            );
            userMarker.bindTooltip(employeeName || 'You', {
                permanent: true, direction: 'top', offset: [0, -30], className: 'payroll-employee-name-label'
            });

            // ------------------------------------------------
            // ROUTE LINE
            // ------------------------------------------------

            const routeLine = L.polyline([office, user], { color: "#0d6efd", weight: 3, opacity: 0.9, dashArray: "7,7" }).addTo(map);

            const roadRouteCasing = L.polyline([office, user], { color: '#ffffff', weight: 8, opacity: .78, lineCap: 'round', lineJoin: 'round' }).addTo(map);
            const roadRouteLine = L.polyline([office, user], { color: '#1688ff', weight: 5, opacity: .98, lineCap: 'round', lineJoin: 'round' }).addTo(map);
            const journeyOverlay = window.payrollCreateJourneyOverlay(mapElement, 'payroll-employee-journey-overlay');

            // ------------------------------------------------
            // GEOFENCE CIRCLE
            // ------------------------------------------------

            const rangeColor =
                isWithin
                    ? "#198754"
                    : "#dc3545";

            const radiusCircle =
                L.circle(
                    office,
                    {
                        radius:
                            allowedRadius,

                        color:
                            rangeColor,

                        weight:
                            1,

                        fillColor:
                            rangeColor,

                        fillOpacity:
                            0.08
                    }
                )
                    .addTo(map);

            // ------------------------------------------------
            // SAVE MAP STATE
            // ------------------------------------------------

            mapData = {
                map:
                    map,

                tileLayer:
                    tileLayer,

                officeMarker:
                    officeMarker,

                userMarker:
                    userMarker,

                routeLine: routeLine,
                roadRouteCasing: roadRouteCasing,
                roadRouteLine: roadRouteLine,
                journeyOverlay: journeyOverlay,
                routeState: {},
                journeyStartedAt: (sessionStartedIso && !Number.isNaN(Date.parse(sessionStartedIso))) ? Date.parse(sessionStartedIso) : Date.now(),
                employeeName: employeeName || 'You',
                lastRawPosition: user.slice(),
                lastRawPositionAt: Date.now(),
                speedMps: 0,
                lastAccuracyMeters: 0,

                radiusCircle:
                    radiusCircle,

                office:
                    office,

                radius:
                    allowedRadius,

                isWithin:
                    !!isWithin,

                hasInitialView:
                    false,

                lastLiveUpdateAt:
                    0
            };

            window.payrollGeoMaps[mapId] =
                mapData;
        }

        // ----------------------------------------------------
        // UPDATE POSITIONS
        // ----------------------------------------------------

        mapData.officeMarker
            .setLatLng(office);

        // Apply a pending browser-side GPS point, if the watcher reported
        // it before the Blazor component finished creating the map.
        const pendingEmployeePoint =
            window.payrollGeoLivePending?.[mapId];

        if (pendingEmployeePoint &&
            Number.isFinite(Number(pendingEmployeePoint.latitude)) &&
            Number.isFinite(Number(pendingEmployeePoint.longitude))) {
            user[0] = Number(pendingEmployeePoint.latitude);
            user[1] = Number(pendingEmployeePoint.longitude);
            delete window.payrollGeoLivePending[mapId];
        }

        mapData.employeeName = employeeName || mapData.employeeName || 'You';
        try { mapData.userMarker.getTooltip()?.setContent(mapData.employeeName); } catch { }
        if (sessionStartedIso && !Number.isNaN(Date.parse(sessionStartedIso))) mapData.journeyStartedAt = Date.parse(sessionStartedIso);
        mapData.lastAccuracyMeters = Number(accuracyMeters) || mapData.lastAccuracyMeters || 0;

        const rawNow = Date.now();
        if (mapData.lastRawPosition) {
            const seconds = Math.max(.25, (rawNow - (mapData.lastRawPositionAt || rawNow)) / 1000);
            mapData.speedMps = Math.min(55, window.payrollHaversineMeters(mapData.lastRawPosition, user) / seconds);
        }
        mapData.lastRawPosition = user.slice();
        mapData.lastRawPositionAt = rawNow;

        const employeeRoute = await window.payrollRequestJourneyRoute(mapData, user, office, { minMoveMeters: 20, minIntervalMs: 18000 });
        if (employeeRoute?.geometry?.length > 1) {
            mapData.roadRouteCasing.setLatLngs(employeeRoute.geometry);
            mapData.roadRouteLine.setLatLngs(employeeRoute.geometry);
            mapData.routeLine.setStyle({ opacity: 0 });
        } else {
            mapData.routeLine.setStyle({ opacity: .9 });
        }
        const employeeRemaining = employeeRoute?.distanceMeters || window.payrollHaversineMeters(user, office);
        window.payrollRenderJourneyOverlay(mapData.journeyOverlay, {
            name: mapData.employeeName, distanceMeters: employeeRemaining,
            durationSeconds: employeeRoute?.durationSeconds || 0, speedMps: mapData.speedMps,
            accuracyMeters: mapData.lastAccuracyMeters, journeyStartedAt: mapData.journeyStartedAt,
            road: window.payrollGetNextRoadName(employeeRoute), arrived: employeeRemaining <= Math.max(25, allowedRadius)
        });

        const employeeAnimationKey =
            'employee:' + mapId;

        if (!mapData.hasInitialView) {
            mapData.userMarker.setLatLng(user);
            mapData.routeLine.setLatLngs([office, user]);
        }
        else {
            window.payrollSmoothMoveMarker(
                mapData.userMarker,
                employeeAnimationKey,
                user,
                900,
                function (position) {
                    mapData.routeLine.setLatLngs([
                        office,
                        position
                    ]);
                }
            );
        }

        mapData.radiusCircle
            .setLatLng(office);

        mapData.radiusCircle
            .setRadius(
                allowedRadius
            );

        // ----------------------------------------------------
        // UPDATE GEOFENCE COLOR
        // ----------------------------------------------------

        const rangeColor =
            isWithin
                ? "#198754"
                : "#dc3545";

        mapData.radiusCircle
            .setStyle({
                color:
                    rangeColor,

                fillColor:
                    rangeColor
            });

        // ----------------------------------------------------
        // FIT OFFICE + USER
        // ----------------------------------------------------

        if (!mapData.hasInitialView) {
            const bounds =
                L.latLngBounds([
                    office,
                    user
                ]);

            if (bounds.isValid()) {
                mapData.map.fitBounds(
                    bounds,
                    {
                        padding: [25, 25],
                        maxZoom: 17,
                        animate: false
                    }
                );
            }
            else {
                mapData.map.setView(user, 17);
            }

            mapData.hasInitialView = true;
        }

        // ----------------------------------------------------
        // FORCE LEAFLET RESIZE
        // ----------------------------------------------------

        const resizeMap =
            function () {

                try {

                    if (
                        mapData &&
                        mapData.map
                    ) {
                        mapData.map.invalidateSize(
                            true
                        );
                    }

                }
                catch (error) {

                    console.warn(
                        "Employee map resize failed:",
                        error
                    );

                }
            };

        // Immediate
        resizeMap();

        // After layout
        requestAnimationFrame(
            function () {
                resizeMap();
            }
        );

        // After browser paint
        setTimeout(
            resizeMap,
            100
        );

        setTimeout(
            resizeMap,
            300
        );

        setTimeout(
            resizeMap,
            700
        );

        mapData.office = office;
    mapData.radius = allowedRadius;
    mapData.isWithin = !!isWithin;

    return true;

    }
    catch (error) {

        console.error(
            "Employee geo map initialization failed:",
            error
        );

        return false;
    }
};




// ============================================================
// DIRECT EMPLOYEE LIVE MAP UPDATE
// ============================================================
// Called by the persistent GPS watcher directly in the browser. This
// keeps the employee's own map moving even when the Blazor circuit is
// busy or between server-side renders.
// ============================================================

window.updateEmployeeLiveGeoMap =
    function (employeeId, latitude, longitude) {
        const id = Number(employeeId);
        const lat = Number(latitude);
        const lng = Number(longitude);

        if (
            !Number.isFinite(id) ||
            id <= 0 ||
            !Number.isFinite(lat) ||
            !Number.isFinite(lng)
        ) {
            return false;
        }

        const mapId = 'geo-map-' + id;
        const mapData =
            window.payrollGeoMaps?.[mapId];

        window.payrollGeoLivePending =
            window.payrollGeoLivePending || {};

        if (!mapData || !mapData.map || !mapData.userMarker) {
            window.payrollGeoLivePending[mapId] = {
                latitude: lat,
                longitude: lng,
                receivedAt: Date.now()
            };
            return false;
        }

        const target = [lat, lng];
        const now = Date.now();
        const previousAt =
            Number(mapData.lastLiveUpdateAt) || 0;

        const elapsed = previousAt > 0
            ? now - previousAt
            : 4500;

        mapData.lastLiveUpdateAt = now;

        const duration = Math.max(
            900,
            Math.min(
                4800,
                elapsed > 250
                    ? elapsed * 0.9
                    : 2200
            )
        );

        const office =
            mapData.office ||
            [mapData.officeMarker.getLatLng().lat, mapData.officeMarker.getLatLng().lng];

        if (mapData.lastRawPosition) {
            const seconds = Math.max(.25, (now - (mapData.lastRawPositionAt || now)) / 1000);
            mapData.speedMps = Math.min(55, window.payrollHaversineMeters(mapData.lastRawPosition, target) / seconds);
        }
        mapData.lastRawPosition = target.slice();
        mapData.lastRawPositionAt = now;

        window.payrollRequestJourneyRoute(mapData, target, office, { minMoveMeters: 20, minIntervalMs: 18000 }).then(function(route) {
            if (route?.geometry?.length > 1) {
                mapData.roadRouteCasing?.setLatLngs(route.geometry);
                mapData.roadRouteLine?.setLatLngs(route.geometry);
                mapData.routeLine?.setStyle({ opacity: 0 });
            }
            const remaining = route?.distanceMeters || window.payrollHaversineMeters(target, office);
            window.payrollRenderJourneyOverlay(mapData.journeyOverlay, {
                name: mapData.employeeName || 'You', distanceMeters: remaining, durationSeconds: route?.durationSeconds || 0,
                speedMps: mapData.speedMps, accuracyMeters: mapData.lastAccuracyMeters, journeyStartedAt: mapData.journeyStartedAt,
                road: window.payrollGetNextRoadName(route), arrived: remaining <= Math.max(25, Number(mapData.radius) || 100)
            });
        }).catch(function() {});

        window.payrollSmoothMoveMarker(
            mapData.userMarker,
            'employee:' + mapId,
            target,
            duration,
            function (position) {
                try {
                    mapData.routeLine.setLatLngs([office, position]);
                    const route = mapData.routeState?.route;
                    const remaining = route?.distanceMeters || window.payrollHaversineMeters(position, office);
                    window.payrollRenderJourneyOverlay(mapData.journeyOverlay, {
                        name: mapData.employeeName || 'You', distanceMeters: remaining, durationSeconds: route?.durationSeconds || 0,
                        speedMps: mapData.speedMps, accuracyMeters: mapData.lastAccuracyMeters, journeyStartedAt: mapData.journeyStartedAt,
                        road: window.payrollGetNextRoadName(route), arrived: remaining <= Math.max(25, Number(mapData.radius) || 100)
                    });
                } catch { }
            }
        );

        return true;
    };

// ============================================================
// DESTROY EMPLOYEE MAP
// ============================================================

window.destroyGeoMap =
    function (mapId) {

        window.payrollCancelGeoAnimation?.(
            'employee:' + mapId);

        const mapData =
            window.payrollGeoMaps[mapId];


        if (!mapData) {
            return;
        }


        try { mapData.routeState?.controller?.abort(); } catch { }

        try {

            mapData.map.remove();

        }
        catch {
        }


        delete window.payrollGeoMaps[mapId];
    };

// ============================================================
// ADMIN LIVE STAFF MAP
// ============================================================

window.adminLiveMaps = {};

/*
 * Visually fan out co-located admin staff markers without changing their
 * actual GPS coordinates. Routes and journey calculations continue to use
 * the real position.
 */
window.payrollBuildAdminMarkerDisplayPositions = function (map, liveStaff, selectedId) {
    const items = [];
    const byId = {};
    const useCollisionOffsets = Number(selectedId) <= 0;

    (Array.isArray(liveStaff) ? liveStaff : []).forEach(function (x) {
        const employeeId = Number(x.employeeId);
        const lat = Number(x.latitude);
        const lng = Number(x.longitude);
        if (!Number.isFinite(employeeId) || !Number.isFinite(lat) || !Number.isFinite(lng)) return;
        const item = { employeeId, lat, lng, offsetX: 0, offsetY: 0 };
        items.push(item);
        byId[employeeId] = item;
    });

    if (!useCollisionOffsets || items.length < 2) return byId;

    // Group staff whose map markers would visually collide.
    const collisionMeters = 45;
    const parent = items.map(function (_, i) { return i; });
    function find(i) {
        while (parent[i] !== i) {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }
        return i;
    }
    function union(a, b) {
        const ra = find(a), rb = find(b);
        if (ra !== rb) parent[rb] = ra;
    }

    for (let i = 0; i < items.length; i++) {
        for (let j = i + 1; j < items.length; j++) {
            const d = window.payrollHaversineMeters(
                [items[i].lat, items[i].lng],
                [items[j].lat, items[j].lng]
            );
            if (d <= collisionMeters) union(i, j);
        }
    }

    const groups = {};
    items.forEach(function (item, index) {
        const root = find(index);
        if (!groups[root]) groups[root] = [];
        groups[root].push(item);
    });

    Object.keys(groups).forEach(function (root) {
        const group = groups[root];
        if (group.length < 2) return;

        // Stable employee-ID ordering prevents markers from swapping places.
        group.sort(function (a, b) { return a.employeeId - b.employeeId; });
        const center = [group[0].lat, group[0].lng];
        const centerPoint = map.latLngToLayerPoint(center);
        const count = group.length;
        const radius = count <= 2 ? 28 : count <= 4 ? 34 : count <= 7 ? 40 : 46;

        group.forEach(function (item, index) {
            const angle = (-Math.PI / 2) + (index * (Math.PI * 2 / count));
            const point = L.point(
                centerPoint.x + Math.cos(angle) * radius,
                centerPoint.y + Math.sin(angle) * radius
            );
            const display = map.layerPointToLatLng(point);
            item.offsetX = display.lng - item.lng;
            item.offsetY = display.lat - item.lat;
        });
    });

    return byId;
};

window.payrollBuildAdminCardDisplayPositions = function (map, liveStaff, selectedId, markerDisplayPositions) {
    const valid = [];
    const result = {};
    const selected = Number(selectedId) || 0;
    (Array.isArray(liveStaff) ? liveStaff : []).forEach(function (x) {
        const employeeId = Number(x.employeeId), lat = Number(x.latitude), lng = Number(x.longitude);
        if (!Number.isFinite(employeeId) || !Number.isFinite(lat) || !Number.isFinite(lng)) return;
        valid.push({ employeeId, lat, lng });
    });
    valid.forEach(function (item) {
        const markerItem = markerDisplayPositions?.[item.employeeId];
        const markerLat = item.lat + Number(markerItem?.offsetY || 0);
        const markerLng = item.lng + Number(markerItem?.offsetX || 0);
        const pt = map.latLngToLayerPoint([markerLat, markerLng]);
        result[item.employeeId] = map.layerPointToLatLng(L.point(pt.x, pt.y - 92));
    });
    if (selected > 0) return result;
    const groups = [];
    valid.forEach(function (item) {
        let group = null;
        for (const g of groups) {
            if (g.some(function (other) { return window.payrollHaversineMeters([item.lat,item.lng],[other.lat,other.lng]) <= 60; })) { group = g; break; }
        }
        if (!group) { group = []; groups.push(group); }
        group.push(item);
    });
    groups.forEach(function (group) {
        if (group.length <= 1) return;
        group.sort(function(a,b){return a.employeeId-b.employeeId;});
        const center = group.reduce(function(a,i){a[0]+=i.lat;a[1]+=i.lng;return a;},[0,0]);
        center[0]/=group.length; center[1]/=group.length;
        const cp = map.latLngToLayerPoint(center);
        const count=group.length, columns=count<=2?count:Math.min(3,Math.ceil(Math.sqrt(count))), rows=Math.ceil(count/columns);
        const colGap=235, rowGap=102;
        group.forEach(function(item,index){
            const col=index%columns,row=Math.floor(index/columns);
            const x=(col-(columns-1)/2)*colGap;
            const y=-82+(row-(rows-1)/2)*rowGap;
            result[item.employeeId]=map.layerPointToLatLng(L.point(cp.x+x,cp.y+y));
        });
    });
    return result;
};
window.payrollCreateAdminJourneyCard = function(map, position, html) {
    const icon=function(content){return L.divIcon({className:'payroll-admin-card-marker',html:content,iconSize:[220,120],iconAnchor:[110,120]});};
    const marker=L.marker(position,{icon:icon(html),interactive:false,keyboard:false,zIndexOffset:1500}).addTo(map);
    const originalSetOpacity=marker.setOpacity.bind(marker);
    marker.setContent=function(content){marker.setIcon(icon(content));return marker;};
    marker.setOpacity=function(opacity){originalSetOpacity(opacity);return marker;};
    return marker;
};

window.updateAdminLiveStaffMap =
    async function (
        mapId,
        officeLat,
        officeLng,
        staff,
        selectedId
    ) {
        const parsedOfficeLat = Number(officeLat);
        const parsedOfficeLng = Number(officeLng);

        if (
            !Number.isFinite(parsedOfficeLat) ||
            !Number.isFinite(parsedOfficeLng) ||
            parsedOfficeLat === 0 ||
            parsedOfficeLng === 0
        ) {
            console.warn(
                "Admin live map: valid office GPS coordinates are not configured."
            );
            return;
        }

        try {
            await window.loadPayrollLeaflet();

            const office = [
                parsedOfficeLat,
                parsedOfficeLng
            ];

            const liveStaff =
                Array.isArray(staff)
                    ? staff
                    : [];

            let state =
                window.adminLiveMaps[mapId];

            if (!state) {
                const map =
                    L.map(
                        mapId,
                        {
                            zoomControl: true,
                            attributionControl: true
                        }
                    );

                L.tileLayer(
                    'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
                    {
                        maxZoom: 19,
                        attribution:
                            '© OpenStreetMap contributors'
                    }
                ).addTo(map);

                const officeIcon =
                    L.divIcon({
                        className:
                            'payroll-office-marker',
                        html:
                            '<div class="payroll-map-office">' +
                            '<i class="bi bi-building-fill"></i>' +
                            '</div>',
                        iconSize: [38, 38],
                        iconAnchor: [19, 19]
                    });

                const officeMarker =
                    L.marker(
                        office,
                        {
                            icon: officeIcon
                        }
                    ).addTo(map);

                officeMarker.bindPopup(
                    '<strong>OFFICE</strong><br>Configured location'
                );

                officeMarker.bindTooltip(
                    'OFFICE',
                    {
                        permanent: true,
                        direction: 'top',
                        offset: [0, -12],
                        className:
                            'admin-office-label'
                    }
                );

                state = {
                    map: map,
                    officeMarker:
                        officeMarker,
                    circle: null,
                    markers: {},
                    lines: {},
                    trails: {},
                    trailPoints: {},
                    labels: {},
                    journeyLabels: {},
                    journeyCardPositions: {},
                    collisionConnectors: {},
                    lastOfficeRadius: 0,
                    historyRoute: null,
                    historyMarkers: [],
                    historyStartMarker: null,
                    historyEndMarker: null,
                    hasInitialFit: false,
                    lastStaffSignature: '',
                    lastSelectedId: 0,
                    lastLocationAt: {},
                    routeStates: {},
                    roadRouteLines: {},
                    roadRouteCasings: {},
                    journeyStartedAt: {}
                };

                window.adminLiveMaps[mapId] =
                    state;

                setTimeout(
                    function () {
                        map.invalidateSize();
                    },
                    150
                );
            }

            state.officeMarker
                .setLatLng(office);

            const staffIds =
                new Set(
                    liveStaff.map(
                        function (x) {
                            return Number(
                                x.employeeId
                            );
                        }
                    )
                );

            Object.keys(
                state.markers
            ).forEach(
                function (id) {
                    const employeeId =
                        Number(id);

                    if (
                        !staffIds.has(
                            employeeId
                        )
                    ) {
                        try {
                            state.map.removeLayer(
                                state.markers[id]
                            );
                        }
                        catch { }

                        try {
                            if (
                                state.lines[id]
                            ) {
                                state.map.removeLayer(
                                    state.lines[id]
                                );
                            }
                        }
                        catch { }

                        try {
                            if (state.trails[id]) {
                                state.map.removeLayer(state.trails[id]);
                            }
                        }
                        catch { }

                        try {
                            if (
                                state.labels[id]
                            ) {
                                state.map.removeLayer(
                                    state.labels[id]
                                );
                            }
                        }
                        catch { }

                        delete state.markers[id];
                        delete state.lines[id];
                        delete state.trails[id];
                        delete state.trailPoints[id];
                        delete state.labels[id];
                        delete state.journeyLabels[id];
                        delete state.journeyCardPositions[id];
                        try {
                            if (state.collisionConnectors[id]) {
                                state.map.removeLayer(state.collisionConnectors[id]);
                            }
                        }
                        catch { }
                        delete state.collisionConnectors[id];
                        try { state.roadRouteLines[id] && state.map.removeLayer(state.roadRouteLines[id]); } catch { }
                        try { state.roadRouteCasings[id] && state.map.removeLayer(state.roadRouteCasings[id]); } catch { }
                        try { state.routeStates[id]?.controller?.abort(); } catch { }
                        delete state.roadRouteLines[id];
                        delete state.roadRouteCasings[id];
                        delete state.routeStates[id];
                        delete state.journeyStartedAt[id];
                    }
                }
            );

            let maximumRadius = 100;

            const staffSignature =
                liveStaff
                    .map(function (x) {
                        return Number(x.employeeId);
                    })
                    .sort(function (a, b) { return a - b; })
                    .join(',');

            const membershipChanged =
                state.lastStaffSignature !== staffSignature ||
                state.lastSelectedId !== Number(selectedId);

            const markerDisplayPositions =
                window.payrollBuildAdminMarkerDisplayPositions(
                    state.map,
                    liveStaff,
                    selectedId
                );
            const cardDisplayPositions =
                window.payrollBuildAdminCardDisplayPositions(
                    state.map,
                    liveStaff,
                    selectedId,
                    markerDisplayPositions
                );

            liveStaff.forEach(
                function (x) {
                    const employeeId =
                        Number(
                            x.employeeId
                        );

                    const lat =
                        Number(
                            x.latitude
                        );

                    const lng =
                        Number(
                            x.longitude
                        );

                    if (
                        !Number.isFinite(lat) ||
                        !Number.isFinite(lng)
                    ) {
                        return;
                    }

                    const position = [
                        lat,
                        lng
                    ];

                    const displayItem = markerDisplayPositions[employeeId];
                    const displayPosition = displayItem
                        ? [
                            lat + Number(displayItem.offsetY || 0),
                            lng + Number(displayItem.offsetX || 0)
                        ]
                        : position.slice();
                    const cardDisplayPosition = cardDisplayPositions[employeeId] || displayPosition.slice();
                    state.journeyCardPositions[employeeId] = cardDisplayPosition.slice();

                    if (!state.routeStates[employeeId]) {
                        state.routeStates[employeeId] = {};
                    }
                    if (!state.journeyStartedAt[employeeId] && x.sessionStartedUtc) {
                        const parsedStart = Date.parse(x.sessionStartedUtc);
                        if (!Number.isNaN(parsedStart)) state.journeyStartedAt[employeeId] = parsedStart;
                    }

                    const isSelected =
                        Number(selectedId) === employeeId;

                    if (!Array.isArray(state.trailPoints[employeeId])) {
                        state.trailPoints[employeeId] = [];
                    }

                    const points = state.trailPoints[employeeId];
                    const previousPoint = points[points.length - 1];
                    if (!previousPoint ||
                        previousPoint[0] !== position[0] ||
                        previousPoint[1] !== position[1]) {
                        points.push(position);
                        if (points.length > 60) {
                            points.shift();
                        }
                    }

                    const withinRange =
                        Boolean(
                            x.isWithinAllowedRadius
                        );

                    const status =
                        String(
                            x.status || 'Live'
                        ).toLowerCase();

                    const allowedRadius =
                        Number(
                            x.allowedRadiusMeters
                        ) || 100;

                    if (
                        allowedRadius >
                        maximumRadius
                    ) {
                        maximumRadius =
                            allowedRadius;
                    }

                    let markerColor =
                        '#198754';

                    if (!withinRange) {
                        markerColor =
                            '#dc3545';
                    }
                    else if (
                        status === 'stale'
                    ) {
                        markerColor =
                            '#ffc107';
                    }

                    const icon =
                        L.divIcon({
                            className:
                                'payroll-user-marker',
                            html:
                                '<div style="' +
                                'width:38px;' +
                                'height:38px;' +
                                'border-radius:50%;' +
                                'display:flex;' +
                                'align-items:center;' +
                                'justify-content:center;' +
                                'background:#fff;' +
                                'color:' +
                                markerColor +
                                ';' +
                                'border:3px solid ' +
                                markerColor +
                                ';' +
                                'box-shadow:0 2px 8px rgba(0,0,0,.28);' +
                                'font-size:18px">' +
                                '<i class="bi bi-person-fill"></i>' +
                                '</div>',
                            iconSize: [38, 38],
                            iconAnchor: [19, 19]
                        });

                    let markerCreated = false;

                    if (
                        !state.markers[
                        employeeId
                        ]
                    ) {
                        state.markers[
                            employeeId
                        ] =
                            L.marker(
                                displayPosition,
                                {
                                    icon: icon
                                }
                            ).addTo(
                                state.map
                            );

                        markerCreated = true;
                    }
                    else {
                        state.markers[
                            employeeId
                        ].setIcon(
                            icon
                        );

                        const now = Date.now();
                        const previousAt =
                            Number(state.lastLocationAt[employeeId]) || 0;
                        const elapsed = previousAt > 0
                            ? now - previousAt
                            : 4500;

                        state.lastLocationAt[employeeId] = now;

                        const moveDuration = Math.max(
                            900,
                            Math.min(
                                4800,
                                elapsed > 250
                                    ? elapsed * 0.9
                                    : 2200
                            )
                        );

                        window.payrollSmoothMoveMarker(
                            state.markers[employeeId],
                            'admin:' + mapId + ':' + employeeId,
                            displayPosition,
                            moveDuration,
                            function (animatedPosition) {
                                try {
                                    // GPS, routes and distances remain anchored to the
                                    // real coordinate. Only the visual marker is offset.
                                    if (state.lines[employeeId]) {
                                        state.lines[employeeId].setLatLngs([
                                            office,
                                            position
                                        ]);
                                    }

                                    if (state.collisionConnectors[employeeId]) {
                                        state.collisionConnectors[employeeId].setLatLngs([
                                            position,
                                            animatedPosition
                                        ]);
                                    }

                                    if (state.journeyLabels[employeeId]) {
                                        const dLat = cardDisplayPosition[0] - displayPosition[0];
                                        const dLng = cardDisplayPosition[1] - displayPosition[1];
                                        state.journeyLabels[employeeId].setLatLng([
                                            animatedPosition[0] + dLat,
                                            animatedPosition[1] + dLng
                                        ]);
                                    }

                                    if (state.labels[employeeId]) {
                                        state.labels[employeeId].setLatLng(
                                            window.getAdminLineMidpoint(
                                                office,
                                                position
                                            )
                                        );
                                    }
                                }
                                catch { }
                            }
                        );
                    }

                    if (markerCreated) {
                        state.lastLocationAt[employeeId] = Date.now();
                    }

                    if (points.length > 1) {
                        if (!state.trails[employeeId]) {
                            state.trails[employeeId] = L.polyline(
                                points,
                                {
                                    color: markerColor,
                                    weight: isSelected ? 5 : 3,
                                    opacity: isSelected ? .9 : .45,
                                    dashArray: isSelected ? null : '5,7'
                                }
                            ).addTo(state.map);
                        }
                        else {
                            state.trails[employeeId].setLatLngs(points);
                            state.trails[employeeId].setStyle({
                                color: markerColor,
                                weight: isSelected ? 5 : 3,
                                opacity: isSelected ? .9 : .45,
                                dashArray: isSelected ? null : '5,7'
                            });
                        }
                    }

                    state.markers[employeeId].setOpacity(
                        Number(selectedId) > 0 && !isSelected ? 0 : 1
                    );

                    const hasCollisionOffset =
                        Math.abs(Number(displayItem?.offsetX || 0)) > 0 ||
                        Math.abs(Number(displayItem?.offsetY || 0)) > 0;

                    if (hasCollisionOffset) {
                        if (!state.collisionConnectors[employeeId]) {
                            state.collisionConnectors[employeeId] = L.polyline(
                                [position, displayPosition],
                                {
                                    color: markerColor,
                                    weight: 2,
                                    opacity: .72,
                                    dashArray: '3,4',
                                    lineCap: 'round'
                                }
                            ).addTo(state.map);
                        }
                        else {
                            state.collisionConnectors[employeeId].setLatLngs([
                                position,
                                displayPosition
                            ]);
                            state.collisionConnectors[employeeId].setStyle({
                                color: markerColor,
                                opacity: .72
                            });
                        }
                    }
                    else if (state.collisionConnectors[employeeId]) {
                        try {
                            state.map.removeLayer(state.collisionConnectors[employeeId]);
                        }
                        catch { }
                        delete state.collisionConnectors[employeeId];
                    }

                    if (state.lines[employeeId]) {
                        state.lines[employeeId].setStyle({
                            color: markerColor,
                            opacity: Number(selectedId) > 0 && !isSelected ? 0 : .8
                        });
                    }

                    if (state.trails[employeeId]) {
                        state.trails[employeeId].setStyle({
                            opacity: Number(selectedId) > 0 && !isSelected ? 0 : (isSelected ? .9 : .45)
                        });
                    }

                    if (state.journeyLabels[employeeId]) {
                        state.journeyLabels[employeeId].setOpacity(
                            Number(selectedId) > 0 && !isSelected ? 0 : .98
                        );
                    }
                    if (state.roadRouteLines[employeeId]) {
                        state.roadRouteLines[employeeId].setStyle({
                            color: '#1688ff',
                            weight: isSelected ? 6 : 4,
                            opacity: Number(selectedId) > 0 && !isSelected ? 0 : (isSelected ? .98 : .72)
                        });
                    }
                    if (state.roadRouteCasings[employeeId]) {
                        state.roadRouteCasings[employeeId].setStyle({
                            weight: isSelected ? 9 : 7,
                            opacity: Number(selectedId) > 0 && !isSelected ? 0 : .72
                        });
                    }

                    const distance =
                        window.formatAdminDistance(
                            Number(
                                x.distanceMeters
                            ) || 0
                        );

                    const allowed =
                        Number(
                            x.allowedRadiusMeters
                        ) || 0;

                    const rangeText =
                        withinRange
                            ? 'Within allowed range'
                            : 'Outside allowed range';

                    const safeName =
                        window.escapeAdminHtml(
                            x.name
                        );

                    state.markers[
                        employeeId
                    ].bindPopup(
                        '<div style="min-width:170px">' +
                        '<strong>' +
                        safeName +
                        '</strong><br>' +
                        '<span>Distance: ' +
                        distance +
                        '</span><br>' +
                        '<span>Allowed: ' +
                        allowed +
                        ' m</span><br>' +
                        '<strong style="color:' +
                        markerColor +
                        '">' +
                        rangeText +
                        '</strong>' +
                        '</div>'
                    );

                    const routeStateForLabel = state.routeStates[employeeId] || {};
                    const cachedRouteForLabel = routeStateForLabel.route;
                    const labelDistance = cachedRouteForLabel?.distanceMeters > 0
                        ? window.payrollFormatRouteDistance(cachedRouteForLabel.distanceMeters)
                        : distance;
                    const labelEta = cachedRouteForLabel?.durationSeconds > 0
                        ? window.payrollFormatRouteDuration(cachedRouteForLabel.durationSeconds)
                        : 'Calculating…';
                    const labelSpeed = window.payrollFormatSpeed(routeStateForLabel.speedMps || 0);
                    const labelAccuracy = Number(x.accuracyMeters) > 0
                        ? `±${Math.round(Number(x.accuracyMeters))} m`
                        : 'Unknown';
                    const labelStatus = withinRange
                        ? 'Within range'
                        : 'Outside range';
                    const labelStatusIcon = withinRange ? '🟢' : '🔴';
                    const journeyLabelHtml =
                        `<div class="payroll-admin-map-card${withinRange ? '' : ' outside'}">` +
                        `<div class="card-head"><div class="card-avatar"><i class="bi bi-person-fill"></i></div>` +
                        `<div class="card-name">${safeName}</div><div class="card-status">${labelStatusIcon} ${labelStatus}</div></div>` +
                        `<div class="card-destination"><i class="bi bi-building-fill me-1"></i>To Office</div>` +
                        `<div class="card-metrics">` +
                        `<div class="card-metric"><div class="card-value">${labelDistance}</div><div class="card-label">Distance</div></div>` +
                        `<div class="card-metric"><div class="card-value">${labelEta}</div><div class="card-label">ETA</div></div>` +
                        `<div class="card-metric"><div class="card-value">${window.payrollEscapeHtml(labelSpeed)}</div><div class="card-label">Speed</div></div>` +
                        `<div class="card-metric"><div class="card-value">${window.payrollEscapeHtml(labelAccuracy)}</div><div class="card-label">Accuracy</div></div>` +
                        `<div class="card-metric"><div class="card-value">${window.payrollFormatRouteDuration((Date.now() - (state.journeyStartedAt[employeeId] || Date.now())) / 1000)}</div><div class="card-label">Journey</div></div>` +
                        `<div class="card-metric"><div class="card-value">ROAD</div><div class="card-label">Route</div></div>` +
                        `</div><div class="card-road"><i class="bi bi-signpost-2-fill me-1"></i>Current road · ${window.payrollEscapeHtml(window.payrollGetNextRoadName(cachedRouteForLabel))}</div>` +
                        `<div class="card-footer"><span class="live-dot">● GPS LIVE</span><span>Office destination</span></div></div>`;

                    if (!state.journeyLabels[employeeId]) {
                        state.journeyLabels[employeeId] = window.payrollCreateAdminJourneyCard(
                            state.map,
                            cardDisplayPosition,
                            journeyLabelHtml
                        );
                    } else {
                        state.journeyLabels[employeeId]
                            .setContent(journeyLabelHtml)
                            .setLatLng(cardDisplayPosition);
                    }

                    const lineOptions = {
                        color: markerColor,
                        weight: 2,
                        opacity: .8,
                        dashArray: '6,6'
                    };

                    if (!state.roadRouteCasings[employeeId]) {
                        state.roadRouteCasings[employeeId] = L.polyline([office, position], {
                            color: '#ffffff', weight: 7, opacity: .72, lineCap: 'round', lineJoin: 'round'
                        }).addTo(state.map);
                    }
                    if (!state.roadRouteLines[employeeId]) {
                        state.roadRouteLines[employeeId] = L.polyline([office, position], {
                            color: '#1688ff', weight: 4, opacity: .95, lineCap: 'round', lineJoin: 'round'
                        }).addTo(state.map);
                    }

                    // Throttled road routing: actual GPS points trigger route refreshes,
                    // while marker motion remains smoothly interpolated in the browser.
                    const routeState = state.routeStates[employeeId];
                    const routeNow = Date.now();
                    if (routeState.lastRawPosition) {
                        const seconds = Math.max(.25, (routeNow - (routeState.lastRawPositionAt || routeNow)) / 1000);
                        routeState.speedMps = Math.min(55, window.payrollHaversineMeters(routeState.lastRawPosition, position) / seconds);
                    } else {
                        routeState.speedMps = 0;
                    }
                    routeState.lastRawPosition = position.slice();
                    routeState.lastRawPositionAt = routeNow;

                    window.payrollRequestJourneyRoute(
                        routeState,
                        position,
                        office,
                        { minMoveMeters: 25, minIntervalMs: 30000 }
                    ).then(function(route) {
                        if (!route || !state.markers[employeeId]) return;
                        const remaining = route.distanceMeters || window.payrollHaversineMeters(state.markers[employeeId].getLatLng(), office);
                        state.roadRouteCasings[employeeId]?.setLatLngs(route.geometry);
                        state.roadRouteLines[employeeId]?.setLatLngs(route.geometry);
                        state.lines[employeeId]?.setStyle({ opacity: 0 });
                        const road = window.payrollEscapeHtml(window.payrollGetNextRoadName(route));
                        const routeDistance = window.payrollFormatRouteDistance(remaining);
                        const eta = window.payrollFormatRouteDuration(route.durationSeconds);
                        const name = window.payrollEscapeHtml(x.name || 'Employee');
                        if (state.journeyLabels[employeeId]) {
                            const rs = state.routeStates[employeeId] || {};
                            const within = Boolean(x.isWithinAllowedRadius);
                            const acc = Number(x.accuracyMeters) > 0 ? `±${Math.round(Number(x.accuracyMeters))} m` : 'Unknown';
                            state.journeyLabels[employeeId].setContent(
                                `<div class="payroll-admin-map-card${within ? '' : ' outside'}">` +
                                `<div class="card-head"><div class="card-avatar"><i class="bi bi-person-fill"></i></div><div class="card-name">${name}</div><div class="card-status">${within ? '🟢 Within range' : '🔴 Outside range'}</div></div>` +
                                `<div class="card-destination"><i class="bi bi-building-fill me-1"></i>To Office</div>` +
                                `<div class="card-metrics">` +
                                `<div class="card-metric"><div class="card-value">${routeDistance}</div><div class="card-label">Distance</div></div>` +
                                `<div class="card-metric"><div class="card-value">${eta}</div><div class="card-label">ETA</div></div>` +
                                `<div class="card-metric"><div class="card-value">${window.payrollEscapeHtml(window.payrollFormatSpeed(rs.speedMps || 0))}</div><div class="card-label">Speed</div></div>` +
                                `<div class="card-metric"><div class="card-value">${window.payrollEscapeHtml(acc)}</div><div class="card-label">Accuracy</div></div>` +
                                `<div class="card-metric"><div class="card-value">${window.payrollFormatRouteDuration((Date.now() - (state.journeyStartedAt[employeeId] || Date.now())) / 1000)}</div><div class="card-label">Journey</div></div>` +
                                `<div class="card-metric"><div class="card-value">ROAD</div><div class="card-label">Route</div></div>` +
                                `</div><div class="card-road"><i class="bi bi-signpost-2-fill me-1"></i>Current road · ${road}</div>` +
                                `<div class="card-footer"><span class="live-dot">● GPS LIVE</span><span>Office destination</span></div></div>`
                            );
                        }
                        state.markers[employeeId].bindPopup(
                            `<div style="min-width:210px"><strong>${name}</strong>` +
                            `<div style="margin-top:5px"><b>To Office</b></div>` +
                            `<div>Road distance: ${routeDistance}</div>` +
                            `<div>ETA: ${eta}</div>` +
                            `<div>Current road: ${road}</div>` +
                            `<div>GPS accuracy: ${Number(x.accuracyMeters) > 0 ? '±' + Math.round(Number(x.accuracyMeters)) + ' m' : 'Unknown'}</div>` +
                            `<div>Speed: ${window.payrollFormatSpeed(state.routeStates[employeeId].speedMps || 0)}</div>` +
                            `</div>`
                        );
                    }).catch(function() {});

                    if (!state.lines[employeeId]) {
                        state.lines[
                            employeeId
                        ] =
                            L.polyline(
                                [
                                    office,
                                    position
                                ],
                                lineOptions
                            ).addTo(
                                state.map
                            );
                    }
                    else {
                        // The smooth marker animation updates the line on
                        // every animation frame. Only update its styling
                        // here so the line never snaps to the destination.
                        state.lines[
                            employeeId
                        ].setStyle(
                            lineOptions
                        );
                    }

                    if (isSelected && membershipChanged) {
                        state.markers[employeeId].openPopup();
                        state.map.setView(position, 17, { animate: true });
                    }
                }
            );

            if (
                !state.circle ||
                state.lastOfficeRadius !==
                maximumRadius
            ) {
                if (state.circle) {
                    try {
                        state.map.removeLayer(
                            state.circle
                        );
                    }
                    catch { }
                }

                state.circle =
                    L.circle(
                        office,
                        {
                            radius:
                                maximumRadius,
                            color:
                                '#0d6efd',
                            weight: 1,
                            fillColor:
                                '#0d6efd',
                            fillOpacity: .06
                        }
                    ).addTo(
                        state.map
                    );

                state.lastOfficeRadius =
                    maximumRadius;
            }
            else {
                state.circle.setLatLng(
                    office
                );
            }

            if (
                Number(selectedId) <= 0 &&
                (!state.hasInitialFit || membershipChanged)
            ) {
                if (liveStaff.length > 0) {
                    const points = [office];

                    liveStaff.forEach(function (x) {
                        const lat = Number(x.latitude);
                        const lng = Number(x.longitude);

                        if (Number.isFinite(lat) && Number.isFinite(lng)) {
                            points.push([lat, lng]);
                        }
                    });

                    if (points.length > 1) {
                        state.map.fitBounds(
                            L.latLngBounds(points),
                            {
                                padding: [35, 35],
                                maxZoom: 17,
                                animate: true,
                                duration: 0.5
                            }
                        );
                    }
                    else {
                        state.map.setView(office, 17);
                    }
                }
                else {
                    state.map.setView(office, 17);
                }

                state.hasInitialFit = true;
            }

            state.lastStaffSignature = staffSignature;
            state.lastSelectedId = Number(selectedId);

            setTimeout(
                function () {
                    state.map.invalidateSize();
                },
                100
            );
        }
        catch (error) {
            console.error(
                'Admin live map error:',
                error
            );
        }

        function payrollInvalidateMapSize(map, delays) {
            if (!map) return;

            (delays || [0, 100, 300, 700]).forEach(function (delay) {
                setTimeout(function () {
                    try {
                        map.invalidateSize(true);
                    } catch (e) {
                        console.warn("Leaflet invalidateSize failed:", e);
                    }
                }, delay);
            });
        }
    };

// ============================================================
// ADMIN HISTORICAL GPS ROUTE
// ============================================================

window.updateAdminHistoryRoute =
    async function (
        mapId,
        history,
        employeeName
    ) {
        try {
            await window.loadPayrollLeaflet();

            const state =
                window.adminLiveMaps?.[mapId];

            if (!state || !state.map) {
                console.warn(
                    'Admin map not initialized:',
                    mapId
                );
                return;
            }

            window.clearAdminHistoryRoute(
                mapId
            );

            if (
                !Array.isArray(history) ||
                history.length === 0
            ) {
                return;
            }

            const validPoints =
                history
                    .map(
                        function (x, index) {
                            const lat =
                                Number(
                                    x.latitude ??
                                    x.Latitude
                                );

                            const lng =
                                Number(
                                    x.longitude ??
                                    x.Longitude
                                );

                            if (
                                !Number.isFinite(
                                    lat
                                ) ||
                                !Number.isFinite(
                                    lng
                                )
                            ) {
                                return null;
                            }

                            return {
                                index: index,
                                latitude: lat,
                                longitude: lng,
                                distance:
                                    Number(
                                        x.distanceFromOfficeMeters ??
                                        x.DistanceFromOfficeMeters
                                    ) || 0,
                                allowed:
                                    Number(
                                        x.allowedRadiusMeters ??
                                        x.AllowedRadiusMeters
                                    ) || 0,
                                within:
                                    Boolean(
                                        x.isWithinAllowedRadius ??
                                        x.IsWithinAllowedRadius
                                    ),
                                recordedAt:
                                    x.recordedAtUtc ??
                                    x.RecordedAtUtc
                            };
                        }
                    )
                    .filter(
                        function (x) {
                            return x !== null;
                        }
                    );

            if (
                validPoints.length === 0
            ) {
                return;
            }

            const route =
                validPoints.map(
                    function (x) {
                        return [
                            x.latitude,
                            x.longitude
                        ];
                    }
                );

            const routeColor =
                '#0d6efd';

            state.historyRoute =
                L.polyline(
                    route,
                    {
                        color:
                            routeColor,
                        weight: 5,
                        opacity: .85,
                        lineJoin: 'round',
                        lineCap: 'round'
                    }
                ).addTo(
                    state.map
                );

            state.historyMarkers = [];

            validPoints.forEach(
                function (point, index) {
                    const isFirst =
                        index === 0;

                    const isLast =
                        index ===
                        validPoints.length - 1;

                    let markerColor =
                        '#0d6efd';

                    if (isFirst) {
                        markerColor =
                            '#198754';
                    }

                    if (isLast) {
                        markerColor =
                            '#dc3545';
                    }

                    const pointIcon =
                        L.divIcon({
                            className:
                                'payroll-history-point',
                            html:
                                '<div style="' +
                                'width:12px;' +
                                'height:12px;' +
                                'border-radius:50%;' +
                                'background:' +
                                markerColor +
                                ';' +
                                'border:2px solid #fff;' +
                                'box-shadow:0 1px 5px rgba(0,0,0,.35);' +
                                '"></div>',
                            iconSize: [12, 12],
                            iconAnchor: [6, 6]
                        });

                    const marker =
                        L.marker(
                            [
                                point.latitude,
                                point.longitude
                            ],
                            {
                                icon:
                                    pointIcon,
                                zIndexOffset:
                                    isLast
                                        ? 1000
                                        : 100
                            }
                        ).addTo(
                            state.map
                        );

                    const timeText =
                        window.formatAdminHistoryTime(
                            point.recordedAt
                        );

                    const distanceText =
                        window.formatAdminDistance(
                            point.distance
                        );

                    const allowedText =
                        point.allowed > 0
                            ? point.allowed + ' m'
                            : '-';

                    const statusText =
                        point.within
                            ? 'Within allowed range'
                            : 'Outside allowed range';

                    const statusColor =
                        point.within
                            ? '#198754'
                            : '#dc3545';

                    const safeEmployeeName =
                        window.escapeAdminHtml(
                            employeeName ||
                            'Employee'
                        );

                    let title =
                        'GPS Point ' +
                        (index + 1);

                    if (isFirst) {
                        title =
                            'START';
                    }
                    else if (isLast) {
                        title =
                            'LATEST';
                    }

                    marker.bindPopup(
                        '<div style="min-width:210px">' +
                        '<strong>' +
                        safeEmployeeName +
                        '</strong>' +
                        '<hr style="margin:6px 0">' +
                        '<strong>' +
                        title +
                        '</strong><br>' +
                        '<span>Time: ' +
                        timeText +
                        '</span><br>' +
                        '<span>Distance: ' +
                        distanceText +
                        '</span><br>' +
                        '<span>Allowed: ' +
                        allowedText +
                        '</span><br>' +
                        '<span>Latitude: ' +
                        point.latitude.toFixed(6) +
                        '</span><br>' +
                        '<span>Longitude: ' +
                        point.longitude.toFixed(6) +
                        '</span><br>' +
                        '<strong style="color:' +
                        statusColor +
                        '">' +
                        statusText +
                        '</strong>' +
                        '</div>'
                    );

                    marker.bindTooltip(
                        title,
                        {
                            direction: 'top',
                            offset: [0, -8],
                            opacity: .9
                        }
                    );

                    state.historyMarkers.push(
                        marker
                    );
                }
            );

            const first =
                validPoints[0];

            const last =
                validPoints[
                validPoints.length - 1
                ];

            const startIcon =
                L.divIcon({
                    className:
                        'payroll-history-start',
                    html:
                        '<div style="' +
                        'width:30px;' +
                        'height:30px;' +
                        'border-radius:50%;' +
                        'display:flex;' +
                        'align-items:center;' +
                        'justify-content:center;' +
                        'background:#198754;' +
                        'color:#fff;' +
                        'border:3px solid #fff;' +
                        'box-shadow:0 2px 8px rgba(0,0,0,.35);' +
                        'font-size:13px">' +
                        '<i class="bi bi-play-fill"></i>' +
                        '</div>',
                    iconSize: [30, 30],
                    iconAnchor: [15, 15]
                });

            const endIcon =
                L.divIcon({
                    className:
                        'payroll-history-end',
                    html:
                        '<div style="' +
                        'width:34px;' +
                        'height:34px;' +
                        'border-radius:50%;' +
                        'display:flex;' +
                        'align-items:center;' +
                        'justify-content:center;' +
                        'background:#dc3545;' +
                        'color:#fff;' +
                        'border:3px solid #fff;' +
                        'box-shadow:0 2px 8px rgba(0,0,0,.35);' +
                        'font-size:16px">' +
                        '<i class="bi bi-geo-alt-fill"></i>' +
                        '</div>',
                    iconSize: [34, 34],
                    iconAnchor: [17, 17]
                });

            state.historyStartMarker =
                L.marker(
                    [
                        first.latitude,
                        first.longitude
                    ],
                    {
                        icon:
                            startIcon,
                        zIndexOffset:
                            2000
                    }
                ).addTo(
                    state.map
                );

            state.historyStartMarker.bindPopup(
                '<strong>START</strong><br>' +
                window.formatAdminHistoryTime(
                    first.recordedAt
                )
            );

            state.historyEndMarker =
                L.marker(
                    [
                        last.latitude,
                        last.longitude
                    ],
                    {
                        icon:
                            endIcon,
                        zIndexOffset:
                            2100
                    }
                ).addTo(
                    state.map
                );

            state.historyEndMarker.bindPopup(
                '<strong>LATEST LOCATION</strong><br>' +
                window.formatAdminHistoryTime(
                    last.recordedAt
                )
            );

            const bounds =
                L.latLngBounds(
                    route
                );

            state.map.fitBounds(
                bounds,
                {
                    padding: [45, 45],
                    maxZoom: 18
                }
            );

            setTimeout(
                function () {
                    state.map.invalidateSize();
                },
                100
            );
        }
        catch (error) {
            console.error(
                'Admin history route error:',
                error
            );
        }
    };

// ============================================================
// CLEAR ADMIN HISTORICAL ROUTE
// ============================================================

window.clearAdminHistoryRoute =
    function (mapId) {
        const state =
            window.adminLiveMaps?.[mapId];

        if (!state) return;

        try {
            if (state.historyRoute) {
                state.map.removeLayer(
                    state.historyRoute
                );
            }
        }
        catch { }

        if (
            Array.isArray(
                state.historyMarkers
            )
        ) {
            state.historyMarkers.forEach(
                function (marker) {
                    try {
                        state.map.removeLayer(
                            marker
                        );
                    }
                    catch { }
                }
            );
        }

        try {
            if (
                state.historyStartMarker
            ) {
                state.map.removeLayer(
                    state.historyStartMarker
                );
            }
        }
        catch { }

        try {
            if (
                state.historyEndMarker
            ) {
                state.map.removeLayer(
                    state.historyEndMarker
                );
            }
        }
        catch { }

        state.historyRoute = null;
        state.historyMarkers = [];
        state.historyStartMarker = null;
        state.historyEndMarker = null;
    };

// ============================================================
// ADMIN HISTORY TIME FORMATTER
// ============================================================

window.formatAdminHistoryTime =
    function (value) {
        if (!value) return '-';

        try {
            const date =
                new Date(value);

            if (
                Number.isNaN(
                    date.getTime()
                )
            ) {
                return String(value);
            }

            return date.toLocaleString(
                'en-IN',
                {
                    day: '2-digit',
                    month: '2-digit',
                    year: 'numeric',
                    hour: '2-digit',
                    minute: '2-digit',
                    second: '2-digit'
                }
            );
        }
        catch {
            return String(value);
        }
    };

// ============================================================
// ADMIN MAP HELPERS
// ============================================================

window.getAdminLineMidpoint =
    function (a, b) {
        return [
            (
                Number(a[0]) +
                Number(b[0])
            ) / 2,
            (
                Number(a[1]) +
                Number(b[1])
            ) / 2
        ];
    };

window.formatAdminDistance =
    function (meters) {
        meters =
            Number(meters) || 0;

        return meters < 1000
            ? Math.round(meters) + ' m'
            : (
                meters / 1000
            ).toFixed(2) + ' km';
    };

window.escapeAdminHtml =
    function (value) {
        return String(
            value ?? ''
        ).replace(
            /[&<>"']/g,
            function (ch) {
                return {
                    '&': '&amp;',
                    '<': '&lt;',
                    '>': '&gt;',
                    '"': '&quot;',
                    "'": '&#039;'
                }[ch];
            }
        );
    };

// ============================================================
// ADMIN MAP DESTROY
// ============================================================

window.destroyAdminLiveStaffMap =
    function (mapId) {
        const state =
            window.adminLiveMaps?.[mapId];

        if (state?.markers) {
            Object.keys(state.markers).forEach(function (employeeId) {
                window.payrollCancelGeoAnimation?.(
                    'admin:' + mapId + ':' + employeeId);
            });
        }

        if (!state) return;

        try {
            window.clearAdminHistoryRoute(
                mapId
            );
        }
        catch { }

        try {
            state.map.remove();
        }
        catch { }

        delete window.adminLiveMaps[
            mapId
        ];
    };

// ============================================================
// ADMIN GPS HISTORY PLAYBACK
// FINAL REPLACEMENT VERSION
// ============================================================
//
// Features:
// - Session-specific playback
// - Play / Pause / Resume
// - Reset
// - Seek
// - Speed control
// - Start from selected point
// - Animated marker movement
// - Progressive route line
// - Current point popup
// - Playback state available to Blazor
// - Safe cleanup when map/session changes
// ============================================================

window.adminHistoryPlayback =
    window.adminHistoryPlayback || {};

window.startAdminHistoryPlayback =
    async function (
        mapId,
        history,
        employeeName,
        speed,
        startIndex
    ) {
        try {
            const state =
                window.adminLiveMaps?.[mapId];

            if (
                !state ||
                !state.map ||
                !Array.isArray(history) ||
                history.length === 0
            ) {
                return;
            }

            /*
             * Stop any previous playback for this map.
             * Do not remove the historical route itself.
             */
            window.pauseAdminHistoryPlayback(mapId);

            const points =
                history
                    .map(function (x, index) {

                        const latitude =
                            Number(
                                x.latitude ??
                                x.Latitude
                            );

                        const longitude =
                            Number(
                                x.longitude ??
                                x.Longitude
                            );

                        if (
                            !Number.isFinite(latitude) ||
                            !Number.isFinite(longitude)
                        ) {
                            return null;
                        }

                        return {
                            index: index,

                            latitude:
                                latitude,

                            longitude:
                                longitude,

                            distance:
                                Number(
                                    x.distanceFromOfficeMeters ??
                                    x.DistanceFromOfficeMeters
                                ) || 0,

                            allowed:
                                Number(
                                    x.allowedRadiusMeters ??
                                    x.AllowedRadiusMeters
                                ) || 0,

                            within:
                                Boolean(
                                    x.isWithinAllowedRadius ??
                                    x.IsWithinAllowedRadius
                                ),

                            accuracy:
                                Number(
                                    x.accuracyMeters ??
                                    x.AccuracyMeters
                                ) || 0,

                            recordedAt:
                                x.recordedAtUtc ??
                                x.RecordedAtUtc
                        };
                    })
                    .filter(Boolean);

            if (points.length === 0) {
                return;
            }

            /*
             * Preserve the currently selected index when possible.
             */
            const previous =
                window.adminHistoryPlayback[mapId];

            const requestedIndex =
                Number.isFinite(
                    Number(startIndex)
                )
                    ? Number(startIndex)
                    : (
                        previous?.index ?? 0
                    );

            const initialIndex =
                Math.max(
                    0,
                    Math.min(
                        points.length - 1,
                        requestedIndex
                    )
                );

            const playback = {

                mapId:
                    mapId,

                points:
                    points,

                employeeName:
                    employeeName ||
                    "Employee",

                speed:
                    Math.max(
                        0.25,
                        Number(speed) || 1
                    ),

                index:
                    initialIndex,

                timer:
                    null,

                animationFrame:
                    null,

                marker:
                    null,

                routeLine:
                    null,

                completed:
                    initialIndex >=
                    points.length - 1,

                lastTickTime:
                    0
            };

            window.adminHistoryPlayback[mapId] =
                playback;

            /*
             * Playback employee marker.
             */
            const icon =
                L.divIcon({
                    className:
                        "payroll-playback-marker",

                    html:
                        '<div style="' +
                        'width:42px;' +
                        'height:42px;' +
                        'border-radius:50%;' +
                        'display:flex;' +
                        'align-items:center;' +
                        'justify-content:center;' +
                        'background:#6610f2;' +
                        'color:#fff;' +
                        'border:4px solid #fff;' +
                        'box-shadow:0 3px 12px rgba(0,0,0,.4);' +
                        'font-size:19px">' +
                        '<i class="bi bi-person-walking"></i>' +
                        '</div>',

                    iconSize:
                        [42, 42],

                    iconAnchor:
                        [21, 21]
                });

            const initialPoint =
                points[initialIndex];

            playback.marker =
                L.marker(
                    [
                        initialPoint.latitude,
                        initialPoint.longitude
                    ],
                    {
                        icon:
                            icon,

                        zIndexOffset:
                            5000
                    }
                ).addTo(
                    state.map
                );

            playback.marker.bindPopup(
                window.buildAdminPlaybackPopup(
                    playback.employeeName,
                    initialPoint,
                    initialIndex,
                    points.length
                )
            );

            /*
             * Progressive playback route.
             */
            playback.routeLine =
                L.polyline(
                    points
                        .slice(
                            0,
                            initialIndex + 1
                        )
                        .map(function (point) {
                            return [
                                point.latitude,
                                point.longitude
                            ];
                        }),
                    {
                        color:
                            "#6610f2",

                        weight:
                            5,

                        opacity:
                            0.9,

                        lineJoin:
                            "round",

                        lineCap:
                            "round"
                    }
                ).addTo(
                    state.map
                );

            window.moveAdminPlaybackMarker(
                playback,
                initialIndex
            );

            /*
             * Keep playback map focused on selected point.
             */
            state.map.panTo(
                [
                    initialPoint.latitude,
                    initialPoint.longitude
                ],
                {
                    animate:
                        false
                }
            );

            if (
                initialIndex <
                points.length - 1
            ) {
                window.resumeAdminHistoryPlayback(
                    mapId
                );
            }

        }
        catch (error) {

            console.error(
                "Admin history playback error:",
                error
            );
        }
    };


window.resumeAdminHistoryPlayback =
    function (mapId) {

        const playback =
            window.adminHistoryPlayback?.[mapId];

        if (
            !playback ||
            !playback.marker ||
            playback.points.length === 0
        ) {
            return;
        }

        window.pauseAdminHistoryPlayback(
            mapId
        );

        if (
            playback.index >=
            playback.points.length - 1
        ) {
            playback.completed = true;
            return;
        }

        playback.completed = false;

        /*
         * One GPS history point normally represents a 10-second
         * recording interval. Speed controls how quickly the
         * history is replayed.
         */
        const interval =
            Math.max(
                150,
                Math.round(
                    1500 /
                    playback.speed
                )
            );

        playback.timer =
            setInterval(
                function () {

                    const current =
                        window.adminHistoryPlayback?.[mapId];

                    if (!current) {
                        return;
                    }

                    if (
                        current.index >=
                        current.points.length - 1
                    ) {
                        window.pauseAdminHistoryPlayback(
                            mapId
                        );

                        current.completed =
                            true;

                        window.moveAdminPlaybackMarker(
                            current,
                            current.index
                        );

                        return;
                    }

                    current.index++;

                    window.moveAdminPlaybackMarker(
                        current,
                        current.index
                    );
                },
                interval
            );
    };


window.pauseAdminHistoryPlayback =
    function (mapId) {

        const playback =
            window.adminHistoryPlayback?.[mapId];

        if (!playback) {
            return;
        }

        if (playback.timer) {

            clearInterval(
                playback.timer
            );

            playback.timer =
                null;
        }

        if (
            playback.animationFrame
        ) {

            cancelAnimationFrame(
                playback.animationFrame
            );

            playback.animationFrame =
                null;
        }
    };


window.resetAdminHistoryPlayback =
    function (mapId) {

        const playback =
            window.adminHistoryPlayback?.[mapId];

        if (!playback) {
            return;
        }

        window.pauseAdminHistoryPlayback(
            mapId
        );

        playback.index =
            0;

        playback.completed =
            false;

        window.moveAdminPlaybackMarker(
            playback,
            0
        );
    };


window.stopAdminHistoryPlayback =
    function (mapId) {

        const playback =
            window.adminHistoryPlayback?.[mapId];

        if (!playback) {
            return;
        }

        window.pauseAdminHistoryPlayback(
            mapId
        );

        try {

            const state =
                window.adminLiveMaps?.[mapId];

            if (
                state?.map &&
                playback.marker
            ) {
                state.map.removeLayer(
                    playback.marker
                );
            }

            if (
                state?.map &&
                playback.routeLine
            ) {
                state.map.removeLayer(
                    playback.routeLine
                );
            }

        }
        catch {
        }

        delete window.adminHistoryPlayback[
            mapId
        ];
    };


window.seekAdminHistoryPlayback =
    function (
        mapId,
        index
    ) {

        const playback =
            window.adminHistoryPlayback?.[mapId];

        if (!playback) {
            return;
        }

        const target =
            Math.max(
                0,
                Math.min(
                    playback.points.length - 1,
                    Number(index) || 0
                )
            );

        playback.index =
            target;

        playback.completed =
            target >=
            playback.points.length - 1;

        window.moveAdminPlaybackMarker(
            playback,
            target
        );
    };


window.moveAdminPlaybackMarker =
    function (
        playback,
        index
    ) {

        if (
            !playback ||
            !playback.marker
        ) {
            return;
        }

        const point =
            playback.points[index];

        if (!point) {
            return;
        }

        const position = [
            point.latitude,
            point.longitude
        ];

        playback.marker.setLatLng(
            position
        );

        playback.marker.setPopupContent(
            window.buildAdminPlaybackPopup(
                playback.employeeName,
                point,
                index,
                playback.points.length
            )
        );

        /*
         * Update progressive playback route.
         */
        if (
            playback.routeLine
        ) {

            playback.routeLine.setLatLngs(
                playback.points
                    .slice(
                        0,
                        index + 1
                    )
                    .map(function (item) {
                        return [
                            item.latitude,
                            item.longitude
                        ];
                    })
            );
        }

        const state =
            window.adminLiveMaps?.[
            playback.mapId
            ];

        if (
            state?.map
        ) {

            /*
             * Do not open a popup every timer tick.
             * The popup is opened when the marker is clicked
             * or when playback starts.
             */
            if (
                index === 0 ||
                index ===
                playback.points.length - 1
            ) {
                playback.marker.openPopup();
            }

            state.map.panTo(
                position,
                {
                    animate:
                        true,

                    duration:
                        0.35
                }
            );
        }
    };


window.buildAdminPlaybackPopup =
    function (
        employeeName,
        point,
        index,
        total
    ) {

        const safeName =
            window.escapeAdminHtml(
                employeeName
            );

        const time =
            window.formatAdminHistoryTime(
                point.recordedAt
            );

        const distance =
            window.formatAdminDistance(
                point.distance
            );

        const allowed =
            point.allowed > 0
                ? point.allowed + " m"
                : "-";

        const accuracy =
            point.accuracy > 0
                ? Math.round(
                    point.accuracy
                ) + " m"
                : "-";

        const status =
            point.within
                ? "Within allowed range"
                : "Outside allowed range";

        const statusColor =
            point.within
                ? "#198754"
                : "#dc3545";

        return (
            '<div style="min-width:230px">' +

            "<strong>" +
            safeName +
            "</strong>" +

            '<hr style="margin:6px 0">' +

            "<strong>GPS Point " +
            (index + 1) +
            " / " +
            total +
            "</strong><br>" +

            "<span>Time: " +
            time +
            "</span><br>" +

            "<span>Distance: " +
            distance +
            "</span><br>" +

            "<span>Allowed: " +
            allowed +
            "</span><br>" +

            "<span>Accuracy: " +
            accuracy +
            "</span><br>" +

            "<span>Latitude: " +
            point.latitude.toFixed(6) +
            "</span><br>" +

            "<span>Longitude: " +
            point.longitude.toFixed(6) +
            "</span><br>" +

            '<strong style="color:' +
            statusColor +
            '">' +
            status +
            "</strong>" +

            "</div>"
        );
    };


window.setAdminHistoryPlaybackSpeed =
    function (
        mapId,
        speed
    ) {

        const playback =
            window.adminHistoryPlayback?.[
            mapId
            ];

        if (!playback) {
            return;
        }

        const wasPlaying =
            !!playback.timer;

        playback.speed =
            Math.max(
                0.25,
                Number(speed) || 1
            );

        window.pauseAdminHistoryPlayback(
            mapId
        );

        if (
            wasPlaying &&
            playback.index <
            playback.points.length - 1
        ) {
            window.resumeAdminHistoryPlayback(
                mapId
            );
        }
    };


window.getAdminHistoryPlaybackState =
    function (mapId) {

        const playback =
            window.adminHistoryPlayback?.[
            mapId
            ];

        if (!playback) {
            return null;
        }

        return {
            index:
                playback.index,

            total:
                playback.points.length,

            playing:
                !!playback.timer,

            completed:
                !!playback.completed,

            speed:
                playback.speed
        };
    };