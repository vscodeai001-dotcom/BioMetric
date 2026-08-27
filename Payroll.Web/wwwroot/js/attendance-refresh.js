window.attendanceRefresh = (function () {

    let connection = null;
    let started = false;
    let starting = false;

    let listeners = [];

    // ============================================================
    // NOTIFY REGISTERED BLAZOR COMPONENTS
    // ============================================================

    async function notifyListeners(methodName, data) {

        for (const listener of [...listeners]) {

            if (!listener)
                continue;

            try {

                if (data === undefined) {

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
                    "[AttendanceRefresh] Listener failed:",
                    methodName,
                    error
                );
            }
        }
    }


    // ============================================================
    // START SIGNALR
    // ============================================================

    async function start() {

        if (started || starting)
            return;

        if (!window.signalR) {

            console.warn(
                "[AttendanceRefresh] SignalR client is not available."
            );

            return;
        }

        starting = true;

        try {

            connection =
                new signalR.HubConnectionBuilder()
                    .withUrl(
                        "/hubs/attendance-refresh"
                    )
                    .withAutomaticReconnect([
                        0,
                        2000,
                        5000,
                        10000,
                        30000
                    ])
                    .build();


            // ====================================================
            // ATTENDANCE CHANGED
            // ====================================================

            connection.on(
                "AttendanceChanged",
                async function (data) {

                    console.log(
                        "[AttendanceRefresh] AttendanceChanged",
                        data
                    );

                    await notifyListeners(
                        "AttendanceChanged"
                    );
                }
            );


            // ====================================================
            // LOCATION CHANGED
            // ====================================================

            connection.on(
                "LocationChanged",
                async function (data) {

                    console.log(
                        "[AttendanceRefresh] LocationChanged",
                        data
                    );

                    // --------------------------------------------
                    // Browser event
                    // --------------------------------------------

                    try {

                        window.dispatchEvent(
                            new CustomEvent(
                                "location-data-changed",
                                {
                                    detail: data
                                }
                            )
                        );

                    }
                    catch (error) {

                        console.warn(
                            "[AttendanceRefresh] Browser location event failed:",
                            error
                        );

                    }


                    // --------------------------------------------
                    // Blazor listeners
                    // --------------------------------------------

                    await notifyListeners(
                        "LocationChanged",
                        data
                    );
                }
            );


            // ====================================================
            // REGULARIZATION CHANGED
            // ====================================================

            connection.on(
                "RegularizationChanged",
                async function (data) {

                    console.log(
                        "[AttendanceRefresh] RegularizationChanged",
                        data
                    );

                    await notifyListeners(
                        "RegularizationChanged"
                    );
                }
            );


            // ====================================================
            // RECONNECTED
            // ====================================================

            connection.onreconnected(
                async function (connectionId) {

                    started = true;

                    console.log(
                        "[AttendanceRefresh] Connection restored.",
                        connectionId
                    );

                    /*
                     * Force all registered Blazor components
                     * to reload their current data.
                     */

                    await notifyListeners(
                        "AttendanceChanged"
                    );

                    await notifyListeners(
                        "LocationChanged"
                    );

                    await notifyListeners(
                        "RegularizationChanged"
                    );
                }
            );


            // ====================================================
            // RECONNECTING
            // ====================================================

            connection.onreconnecting(
                function (error) {

                    console.warn(
                        "[AttendanceRefresh] SignalR reconnecting...",
                        error
                    );
                }
            );


            // ====================================================
            // CLOSED
            // ====================================================

            connection.onclose(
                function (error) {

                    started = false;

                    console.warn(
                        "[AttendanceRefresh] SignalR connection closed.",
                        error
                    );

                }
            );


            // ====================================================
            // CONNECT
            // ====================================================

            await connection.start();

            started = true;

            console.log(
                "[AttendanceRefresh] Live refresh connected."
            );

        }
        catch (error) {

            started = false;

            console.error(
                "[AttendanceRefresh] Live refresh connection failed:",
                error
            );

        }
        finally {

            starting = false;

        }
    }


    // ============================================================
    // REGISTER BLAZOR LISTENER
    // ============================================================

    function register(dotNetRef) {

        if (!dotNetRef)
            return;

        if (!listeners.includes(dotNetRef)) {

            listeners.push(dotNetRef);

            console.log(
                "[AttendanceRefresh] Listener registered. Total:",
                listeners.length
            );
        }

        start();
    }


    // ============================================================
    // UNREGISTER BLAZOR LISTENER
    // ============================================================

    function unregister(dotNetRef) {

        listeners =
            listeners.filter(
                function (x) {
                    return x !== dotNetRef;
                }
            );

        console.log(
            "[AttendanceRefresh] Listener unregistered. Total:",
            listeners.length
        );
    }


    // ============================================================
    // PUBLIC API
    // ============================================================

    return {

        start:
            start,

        register:
            register,

        unregister:
            unregister

    };

})();