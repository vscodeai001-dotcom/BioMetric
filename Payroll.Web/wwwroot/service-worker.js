'use strict';

// Minimal service worker to support Background Sync for queued GPS locations.
// A service worker can flush queued coordinates, but it cannot obtain a new
// browser geolocation reading after the page has been fully closed.

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
        // Service worker can use IndexedDB to read queued locations
        const items = await idbGetAllLocations();

        if (!items || items.length === 0) return;

        for (const item of items) {
            try {
                await fetch('/api/employee-location/update', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'include',
                    body: JSON.stringify({
                        employeeId: item.employeeId,
                        sessionId: item.sessionId,
                        latitude: item.latitude,
                        longitude: item.longitude,
                        accuracy: item.accuracy,
                        timestamp: item.timestamp
                    })
                });
            }
            catch (e) {
                // If any send fails, re-add remaining items and abort
                await idbReAddItems(items.slice(items.indexOf(item)));
                throw e;
            }
        }
    }
    catch (e) {
        // ignore
    }
}

// IndexedDB helpers in service worker
const IDB_DB_NAME = 'gps-tracker-db';
const IDB_STORE_NAME = 'queuedLocations';
const IDB_VERSION = 1;

function idbOpen() {
    return new Promise(function (resolve, reject) {
        try {
            const req = indexedDB.open(IDB_DB_NAME, IDB_VERSION);

            req.onupgradeneeded = function (ev) {
                const db = ev.target.result;
                if (!db.objectStoreNames.contains(IDB_STORE_NAME)) {
                    db.createObjectStore(IDB_STORE_NAME, { keyPath: 'id', autoIncrement: true });
                }
            };

            req.onsuccess = function () { resolve(req.result); };
            req.onerror = function (err) { reject(err); };
        }
        catch (e) { reject(e); }
    });
}

function idbGetAllLocations() {
    return idbOpen().then(function (db) {
        return new Promise(function (resolve, reject) {
            try {
                const tx = db.transaction(IDB_STORE_NAME, 'readwrite');
                const store = tx.objectStore(IDB_STORE_NAME);
                const req = store.getAll();
                req.onsuccess = function () {
                    const items = req.result || [];
                    store.clear();
                    resolve(items);
                };
                req.onerror = function (e) { reject(e); };
            }
            catch (e) { reject(e); }
        });
    });
}

function idbReAddItems(items) {
    return idbOpen().then(function (db) {
        return new Promise(function (resolve, reject) {
            try {
                const tx = db.transaction(IDB_STORE_NAME, 'readwrite');
                const store = tx.objectStore(IDB_STORE_NAME);
                for (const it of items) store.add(it);
                tx.oncomplete = function () { resolve(); };
                tx.onerror = function (e) { reject(e); };
            }
            catch (e) { reject(e); }
        });
    });
}
