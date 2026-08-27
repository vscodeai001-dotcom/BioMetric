window.attendanceRefresh = (function () {

    let connection = null;
    let started = false;
    let starting = false;

    let viewerRef = null;
    let listeners = [];

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
        }
        finally {

            starting = false;
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


    return {

        start: start,

        registerViewer:
            registerViewer,

        unregisterViewer:
            unregisterViewer,

        register:
            register,

        unregister:
            unregister

    };

})();