'use strict';

// Minimal service worker to support Background Sync for GPS queued locations.
// This file uses Cache API sparingly and implements a sync handler that
// attempts to flush the queued GPS locations stored in localStorage.

self.addEventListener('install', event => {
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('sync', function (event) {
    if (event.tag === 'gps-location-sync') {
        event.waitUntil(processQueuedLocations());
    }
});

async function processQueuedLocations() {
    try {
        // localStorage is not available in service workers. We use the
        // IndexedDB fallback stored under a key in the client pages.
        const clientsList = await clients.matchAll({ includeUncontrolled: true });

        for (const client of clientsList) {
            try {
                // Ask each client to process its queued locations
                client.postMessage({ type: 'PROCESS_GPS_QUEUE' });
            }
            catch (e) {
                // ignore
            }
        }
    }
    catch (e) {
        // ignore
    }
}
