window.enableAttendanceTableEdgeScroll = function () {
    const tables = document.querySelectorAll(
        ".attendance-horizontal-scroll");

    for (const table of tables) {
        if (table.dataset.edgeScrollEnabled === "true")
            continue;

        table.dataset.edgeScrollEnabled = "true";

        let pointerX = null;
        let frameId = null;

        const stopScrolling = function () {
            pointerX = null;

            if (frameId !== null) {
                cancelAnimationFrame(frameId);
                frameId = null;
            }
        };

        const scrollAtEdge = function () {
            if (pointerX === null) {
                frameId = null;
                return;
            }

            const bounds = table.getBoundingClientRect();
            const edgeSize = 72;
            let speed = 0;

            if (pointerX < bounds.left + edgeSize) {
                speed = -Math.ceil(
                    (bounds.left + edgeSize - pointerX) / 8);
            }
            else if (pointerX > bounds.right - edgeSize) {
                speed = Math.ceil(
                    (pointerX - (bounds.right - edgeSize)) / 8);
            }

            if (speed !== 0)
                table.scrollLeft += speed;

            frameId = requestAnimationFrame(scrollAtEdge);
        };

        table.addEventListener("mousemove", function (event) {
            pointerX = event.clientX;

            if (frameId === null)
                frameId = requestAnimationFrame(scrollAtEdge);
        });

        table.addEventListener("mouseleave", stopScrolling);
    }
};

window.attendanceRefresh = (function () {

    let connection = null;
    let started = false;
    let starting = false;
    let retryTimer = null;

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

        if (retryTimer || !listeners.length)
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