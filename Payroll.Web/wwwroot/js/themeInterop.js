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
    isWithin
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

            // ------------------------------------------------
            // ROUTE LINE
            // ------------------------------------------------

            const routeLine =
                L.polyline(
                    [
                        office,
                        user
                    ],
                    {
                        color:
                            "#0d6efd",

                        weight:
                            3,

                        opacity:
                            0.9,

                        dashArray:
                            "7,7"
                    }
                )
                    .addTo(map);

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

                routeLine:
                    routeLine,

                radiusCircle:
                    radiusCircle
            };

            window.payrollGeoMaps[mapId] =
                mapData;
        }

        // ----------------------------------------------------
        // UPDATE POSITIONS
        // ----------------------------------------------------

        mapData.officeMarker
            .setLatLng(office);

        mapData.userMarker
            .setLatLng(user);

        mapData.routeLine
            .setLatLngs([
                office,
                user
            ]);

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

        const bounds =
            L.latLngBounds([
                office,
                user
            ]);

        if (
            bounds.isValid()
        ) {
            mapData.map.fitBounds(
                bounds,
                {
                    padding:
                        [25, 25],

                    maxZoom:
                        17,

                    animate:
                        false
                }
            );
        }
        else {
            mapData.map.setView(
                user,
                17
            );
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
// DESTROY EMPLOYEE MAP
// ============================================================

window.destroyGeoMap =
    function (mapId) {

        const mapData =
            window.payrollGeoMaps[mapId];


        if (!mapData) {
            return;
        }


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
                    lastOfficeRadius: 0,
                    historyRoute: null,
                    historyMarkers: [],
                    historyStartMarker: null,
                    historyEndMarker: null
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
                    }
                }
            );

            let maximumRadius = 100;

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

                    if (
                        !state.markers[
                        employeeId
                        ]
                    ) {
                        state.markers[
                            employeeId
                        ] =
                            L.marker(
                                position,
                                {
                                    icon: icon
                                }
                            ).addTo(
                                state.map
                            );
                    }
                    else {
                        state.markers[
                            employeeId
                        ].setLatLng(
                            position
                        );

                        state.markers[
                            employeeId
                        ].setIcon(
                            icon
                        );
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

                    if (state.labels[employeeId]) {
                        state.labels[employeeId].setOpacity(
                            Number(selectedId) > 0 && !isSelected ? 0 : .95
                        );
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

                    state.markers[
                        employeeId
                    ].bindTooltip(
                        safeName,
                        {
                            direction: 'top',
                            offset: [0, -18],
                            className:
                                'admin-employee-label'
                        }
                    );

                    const lineOptions = {
                        color:
                            markerColor,
                        weight: 2,
                        opacity: .8,
                        dashArray: '6,6'
                    };

                    if (
                        !state.lines[
                        employeeId
                        ]
                    ) {
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
                        state.lines[
                            employeeId
                        ].setLatLngs(
                            [
                                office,
                                position
                            ]
                        );

                        state.lines[
                            employeeId
                        ].setStyle(
                            lineOptions
                        );
                    }

                    if (
                        !state.labels[
                        employeeId
                        ]
                    ) {
                        state.labels[
                            employeeId
                        ] =
                            L.tooltip({
                                permanent:
                                    true,
                                direction:
                                    'center',
                                className:
                                    'admin-distance-label',
                                opacity: .95
                            })
                                .setContent(
                                    distance
                                )
                                .setLatLng(
                                    window.getAdminLineMidpoint(
                                        office,
                                        position
                                    )
                                )
                                .addTo(
                                    state.map
                                );
                    }
                    else {
                        state.labels[
                            employeeId
                        ].setContent(
                            distance
                        );

                        state.labels[
                            employeeId
                        ].setLatLng(
                            window.getAdminLineMidpoint(
                                office,
                                position
                            )
                        );
                    }

                    if (isSelected) {
                        state.markers[
                            employeeId
                        ].openPopup();

                        state.map.setView(
                            position,
                            17,
                            {
                                animate: true
                            }
                        );
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
                liveStaff.length > 0 &&
                Number(selectedId) <= 0
            ) {
                const points = [
                    office
                ];

                liveStaff.forEach(
                    function (x) {
                        const lat =
                            Number(
                                x.latitude
                            );

                        const lng =
                            Number(
                                x.longitude
                            );

                        if (
                            Number.isFinite(
                                lat
                            ) &&
                            Number.isFinite(
                                lng
                            )
                        ) {
                            points.push([
                                lat,
                                lng
                            ]);
                        }
                    }
                );

                if (points.length > 1) {
                    state.map.fitBounds(
                        L.latLngBounds(
                            points
                        ),
                        {
                            padding: [35, 35],
                            maxZoom: 17
                        }
                    );
                }
                else {
                    state.map.setView(
                        office,
                        17
                    );
                }
            }
            else if (
                liveStaff.length === 0 &&
                Number(selectedId) <= 0
            ) {
                state.map.setView(
                    office,
                    17
                );
            }

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