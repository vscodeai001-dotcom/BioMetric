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
                "Attendance refresh: SignalR client is not loaded. Retrying in 2s..."
            );
            setTimeout(start, 2000);
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
             */
            connection.on(
                "ApplicationDataChanged",
                function (data) {

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
                        250
                    );
                }
            );


            // LOCATION HEALTH (periodic status of sessions)
            connection.on(
                "LocationHealth",
                async function (data) {
                    console.log('LocationHealth', data);

                    window.dispatchEvent(new CustomEvent('location-health-updated', { detail: data }));

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
             * GPS SESSION LIFECYCLE
             * ==========================================================
             */

            connection.on(
                "SessionEnded",
                async function (data) {

                    console.log(
                        "SessionEnded",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "gps-session-ended",
                            {
                                detail: data
                            }
                        )
                    );

                    await notifyListeners(
                        "SessionEnded",
                        data
                    );

                    await notifyApplicationListeners(
                        "SessionEnded",
                        data
                    );
                }
            );

            connection.on(
                "SessionStarted",
                async function (data) {

                    console.log(
                        "SessionStarted",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "gps-session-started",
                            {
                                detail: data
                            }
                        )
                    );

                    await notifyListeners(
                        "SessionStarted",
                        data
                    );
                }
            );


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

            /*
             * ==========================================================
             * NOTIFICATION CHANGED
             * ==========================================================
             */

            connection.on(
                "NotificationChanged",
                async function (data) {

                    console.log(
                        "NotificationChanged",
                        data
                    );

                    await notifyListeners(
                        "NotificationChanged",
                        data
                    );

                    window.dispatchEvent(
                        new CustomEvent(
                            "notification-data-changed",
                            {
                                detail: data
                            }
                        )
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

                    await notifyViewer();

                    await notifyListeners(
                        "LocationChanged",
                        null
                    );

                    await notifyApplicationListeners(
                        {
                            Entities: [],
                            Reason: "SIGNALR_RECONNECTED"
                        }
                    );
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

    async function notifyApplicationListeners(methodName, data) {

        const currentListeners =
            [...applicationListeners];

        for (const listener of currentListeners) {

            try {
                const targetMethod = typeof data === "undefined" ? "ApplicationDataChanged" : (typeof methodName === "string" ? methodName : "ApplicationDataChanged");
                const payload = typeof data === "undefined" ? methodName : data;

                await listener.invokeMethodAsync(
                    targetMethod,
                    payload
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

                // Fallback attempt without payload if C# method doesn't accept parameters
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


    function registerLocationHealth(dotNetReference) {
        try {
            const handler = function (ev) {
                try {
                    const detail = ev.detail;
                    dotNetReference.invokeMethodAsync('LocationChanged', null).catch(function () { });
                }
                catch (e) { }
            };

            window.addEventListener('location-health-updated', handler);

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