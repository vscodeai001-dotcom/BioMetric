window.attendanceRefresh = (function () {

    let connection = null;
    let started = false;
    let starting = false;
    let retryTimer = null;

    let viewerRef = null;
    let listeners = [];
    let applicationListeners = [];
    let applicationRefreshTimer = null;

    async function start() {

        if (started || starting)
            return;

        if (!window.signalR) {
            console.warn(
                "Attendance refresh: SignalR client is not loaded."
            );
            return;
        }

        starting = true;

        try {

            connection =
                new signalR.HubConnectionBuilder()
                    .withUrl("/hubs/attendance-refresh")
                    .withAutomaticReconnect([
                        0,
                        2000,
                        5000,
                        10000,
                        30000
                    ])
                    .configureLogging(
                        signalR.LogLevel.Warning
                    )
                    .build();


            /*
             * ==========================================================
             * GENERIC DATA CHANGED
             * ==========================================================
             */

            connection.on(
                "DataChanged",
                async function (data) {

                    console.log(
                        "Attendance DataChanged",
                        data
                    );

                    await notifyViewer();

                    await notifyListeners(
                        "AttendanceChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "attendance-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );

            /*
             * ==========================================================
             * APPLICATION-WIDE DATABASE CHANGE
             * ==========================================================
             *
             * Emitted centrally after a successful EF Core write.
             * This is a database invalidation signal, not a data
             * payload. The active route reloads from the database.
             *
             * Rapid writes are coalesced so bulk CRUD does not cause
             * a refresh storm.
             */
            connection.on(
                "ApplicationDataChanged",
                async function (data) {

                    console.log(
                        "ApplicationDataChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "application-data-changed",
                            {
                                detail: data
                            }
                        )
                    );

                    /*
                     * The application-level listener lives in MainLayout
                     * and remains mounted while the user navigates between
                     * pages. It is the fallback for routes that do not have
                     * a domain-specific realtime listener.
                     *
                     * Routes with a direct listener receive the invalidation
                     * directly so they can update their existing component
                     * state without a navigation.
                     */
                    /*
                     * Prefer the page-level realtime listeners when the
                     * active route has one. They can reload their exact
                     * component state immediately without a navigation.
                     *
                     * If the active route has no domain-specific listener,
                     * fall back to the persistent MainLayout listener so
                     * that pages which do not subscribe individually still
                     * receive the change.
                     */
                    if (!listeners.length) {
                        if (applicationRefreshTimer) {
                            clearTimeout(applicationRefreshTimer);
                        }

                        applicationRefreshTimer = setTimeout(
                            async function () {
                                applicationRefreshTimer = null;

                                await notifyApplicationListeners(
                                    data
                                );
                            },
                            100
                        );
                    }
                    else {
                        /*
                         * The active page has a direct realtime listener.
                         * Deliver the same database invalidation to it now.
                         */
                        await notifyListeners(
                            "ApplicationDataChanged",
                            data
                        );
                    }
                }
            );


            // LOCATION HEALTH (periodic status of sessions)
            connection.on(
                "LocationHealth",
                async function (data) {
                    console.log('LocationHealth', data);

                    // Dispatch event for admin UI to update status/age indicators
                    window.dispatchEvent(new CustomEvent('location-health-updated', { detail: data }));

                    // Also notify registered Blazor listeners so components refresh lightweight state
                    await notifyViewer();
                    await notifyListeners('LocationChanged', data);
                }
            );

            connection.on(
                "LeaveChanged",
                async function (data) {

                    console.log(
                        "LeaveChanged",
                        data
                    );

                    await notifyViewer();

                    await notifyListeners(
                        "AttendanceChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "attendance-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );

            connection.on(
                "AdvanceChanged",
                async function (data) {

                    console.log(
                        "AdvanceChanged",
                        data
                    );

                    await notifyViewer();

                    await notifyListeners(
                        "AttendanceChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "attendance-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );

            connection.on(
                "PunchChanged",
                async function (data) {

                    console.log(
                        "PunchChanged",
                        data
                    );

                    await notifyViewer();

                    await notifyListeners(
                        "AttendanceChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "attendance-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );

            connection.on(
                "EmployeeChanged",
                async function (data) {

                    console.log(
                        "EmployeeChanged",
                        data
                    );

                    await notifyViewer();

                    await notifyListeners(
                        "AttendanceChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "attendance-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );

            connection.on(
                "ExitChanged",
                async function (data) {

                    console.log(
                        "ExitChanged",
                        data
                    );

                    await notifyViewer();

                    await notifyListeners(
                        "AttendanceChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "attendance-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );

            connection.on(
                "GlobalRefresh",
                async function (data) {

                    console.log(
                        "GlobalRefresh",
                        data
                    );

                    await notifyViewer();

                    if (listeners.length) {
                        await notifyListeners(
                            "AttendanceChanged",
                            data
                        );
                    }
                    else {
                        await notifyApplicationListeners(
                            data
                        );
                    }

                    window.dispatchEvent(
                        new CustomEvent(
                            "attendance-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );


            /*
             * ==========================================================
             * ATTENDANCE CHANGED
             * ==========================================================
             */

            connection.on(
                "AttendanceChanged",
                async function (data) {

                    console.log(
                        "AttendanceChanged",
                        data
                    );

                    await notifyViewer();

                    await notifyListeners(
                        "AttendanceChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "attendance-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );


            /*
             * ==========================================================
             * LOCATION CHANGED
             * ==========================================================
             *
             * Employee GPS sends:
             *
             * Employee
             *     ↓
             * LiveLocationStore
             *     ↓
             * SignalR
             *     ↓
             * LocationChanged
             *     ↓
             * Admin listeners
             *
             */

            connection.on(
                "LocationChanged",
                async function (data) {

                    console.log(
                        "LocationChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "location-data-changed",
                            {
                                detail: data
                            }
                        )
                    );

                    /*
                     * IMPORTANT:
                     * Pass the location payload through.
                     *
                     * The current LiveStaffLocationPanel can still
                     * reload LiveLocationStore, so this remains
                     * backward compatible.
                     */
                    await notifyListeners(
                        "LocationChanged",
                        data
                    );
                }
            );


            /*
             * ==========================================================
             * REGULARIZATION CHANGED
             * ==========================================================
             */

            connection.on(
                "RegularizationChanged",
                async function (data) {

                    console.log(
                        "RegularizationChanged",
                        data
                    );

                    await notifyViewer();

                    await notifyListeners(
                        "RegularizationChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "regularization-data-changed",
                            {
                                detail: data
                            }
                        )
                    );
                }
            );

            connection.on(
                "NotificationChanged",
                async function (data) {
                    await notifyListeners(
                        "NotificationChanged",
                        data
                    );
                }
            );


            /*
             * ==========================================================
             * RECONNECTING
             * ==========================================================
             */

            connection.onreconnecting(
                function () {

                    console.log(
                        "Attendance refresh connection reconnecting..."
                    );
                }
            );


            /*
             * ==========================================================
             * RECONNECTED
             * ==========================================================
             */

            connection.onreconnected(
                async function (connectionId) {

                    console.log(
                        "Attendance refresh connection restored.",
                        connectionId
                    );

                    /*
                     * Refresh normal attendance viewers.
                     */
                    await notifyViewer();

                    /*
                     * IMPORTANT:
                     *
                     * Refresh all live-location listeners too.
                     *
                     * This allows the admin map to recover the latest
                     * in-memory employee positions after reconnect.
                     */
                    await notifyListeners(
                        "LocationChanged",
                        null
                    );

                    const reconnectData = {
                        Entities: [],
                        Reason: "SIGNALR_RECONNECTED"
                    };

                    if (listeners.length) {
                        await notifyListeners(
                            "ApplicationDataChanged",
                            reconnectData
                        );
                    }
                    else {
                        await notifyApplicationListeners(
                            reconnectData
                        );
                    }
                }
            );


            /*
             * ==========================================================
             * CLOSED
             * ==========================================================
             */

            connection.onclose(
                function () {

                    started = false;
                    starting = false;

                    console.warn(
                        "Attendance refresh connection closed."
                    );

                    scheduleRetry();
                }
            );


            await connection.start();

            started = true;

            console.log(
                "Attendance refresh connection started."
            );

        }
        catch (error) {

            console.error(
                "Unable to start attendance refresh:",
                error
            );

            started = false;

            try {

                if (connection) {
                    await connection.stop();
                }

            }
            catch {
            }

            connection = null;

            scheduleRetry();
        }
        finally {

            starting = false;
        }
    }

    function scheduleRetry() {

        if (retryTimer || (!listeners.length && !applicationListeners.length))
            return;

        retryTimer = setTimeout(
            function () {
                retryTimer = null;
                start();
            },
            2000
        );
    }


    /*
     * ==============================================================
     * APPLICATION-WIDE LISTENER NOTIFICATION
     * ==============================================================
     */

    async function notifyApplicationListeners(data) {

        const currentListeners =
            [...applicationListeners];

        for (const listener of currentListeners) {

            try {
                await listener.invokeMethodAsync(
                    "ApplicationDataChanged",
                    data
                );
            }
            catch (error) {
                console.warn(
                    "Application-wide refresh listener failed:",
                    error
                );
            }
        }
    }


    /*
     * ==============================================================
     * VIEWER NOTIFICATION
     * ==============================================================
     */

    async function notifyViewer() {

        if (!viewerRef)
            return;

        try {

            await viewerRef.invokeMethodAsync(
                "RefreshFromNotification"
            );

        }
        catch (error) {

            console.warn(
                "Attendance viewer refresh failed:",
                error
            );
        }
    }


    /*
     * ==============================================================
     * LISTENER NOTIFICATION
     * ==============================================================
     *
     * data is optional.
     *
     * Existing components that define:
     *
     * LocationChanged()
     *
     * continue to work.
     *
     * Components that define:
     *
     * LocationChanged(data)
     *
     * can now receive the actual event payload.
     */

    async function notifyListeners(
        methodName,
        data
    ) {

        const currentListeners =
            [...listeners];

        for (const listener of currentListeners) {

            try {

                if (typeof data === "undefined") {

                    await listener.invokeMethodAsync(
                        methodName
                    );

                }
                else {

                    await listener.invokeMethodAsync(
                        methodName,
                        data
                    );

                }

            }
            catch (error) {

                console.warn(
                    "Attendance refresh listener failed:",
                    error
                );

                // Some older Blazor circuits cannot bind the optional
                // event payload. Retry the same callback without it so a
                // realtime refresh is not lost.
                if (typeof data !== "undefined") {
                    try {
                        await listener.invokeMethodAsync(
                            methodName
                        );
                    }
                    catch (fallbackError) {
                        console.warn(
                            "Attendance refresh fallback failed:",
                            fallbackError
                        );
                    }
                }

            }
        }
    }


    /*
     * ==============================================================
     * VIEWER REGISTRATION
     * ==============================================================
     */

    function registerViewer(dotNetReference) {

        viewerRef = dotNetReference;

        start();
    }


    async function unregisterViewer(
        dotNetReference
    ) {

        if (viewerRef === dotNetReference) {
            viewerRef = null;
        }
    }


    /*
     * ==============================================================
     * GENERAL LISTENER REGISTRATION
     * ==============================================================
     */

    function register(dotNetReference) {

        if (!listeners.includes(dotNetReference)) {

            listeners.push(
                dotNetReference
            );
        }

        start();
    }


    async function unregister(
        dotNetReference
    ) {

        listeners =
            listeners.filter(
                function (item) {

                    return item !== dotNetReference;
                }
            );
    }

    /*
     * ==============================================================
     * APPLICATION-WIDE LISTENER REGISTRATION
     * ==============================================================
     */

    function registerApplication(dotNetReference) {

        if (!applicationListeners.includes(dotNetReference)) {
            applicationListeners.push(dotNetReference);
        }

        start();
    }

    async function unregisterApplication(dotNetReference) {

        applicationListeners =
            applicationListeners.filter(
                function (item) {
                    return item !== dotNetReference;
                }
            );

        if (!listeners.length && !applicationListeners.length) {
            if (applicationRefreshTimer) {
                clearTimeout(applicationRefreshTimer);
                applicationRefreshTimer = null;
            }
        }
    }


    // Allow Blazor components to register for periodic LocationHealth bridge
    function registerLocationHealth(dotNetReference) {
        try {
            const handler = function (ev) {
                try {
                    const detail = ev.detail;
                    // invoke .NET LocationChanged to trigger lightweight refresh
                    dotNetReference.invokeMethodAsync('LocationChanged', null).catch(function () { });
                }
                catch (e) { }
            };

            window.addEventListener('location-health-updated', handler);

            // store handler on the dotNetReference so unregister can remove
            dotNetReference._locationHealthHandler = handler;
        }
        catch (e) { }
    }

    function unregisterLocationHealth(dotNetReference) {
        try {
            if (dotNetReference && dotNetReference._locationHealthHandler) {
                window.removeEventListener('location-health-updated', dotNetReference._locationHealthHandler);
                dotNetReference._locationHealthHandler = null;
            }
        }
        catch (e) { }
    }


    return {

        start: start,

        registerViewer:
            registerViewer,

        unregisterViewer:
            unregisterViewer,

        register:
            register,

        unregister:
            unregister,

        registerApplication:
            registerApplication,

        unregisterApplication:
            unregisterApplication

    };



})();