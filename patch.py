from pathlib import Path
p=Path('/mnt/data/admin_card_work/Payroll.Web/wwwroot/js/themeInterop.js')
s=p.read_text(encoding='utf-8')
old=".payroll-admin-journey-stack{position:absolute;left:10px;top:10px;z-index:1200;width:min(245px,calc(100% - 20px));max-height:calc(100% - 20px);overflow-y:auto;display:flex;flex-direction:column;gap:7px;pointer-events:none;padding-right:2px}.payroll-admin-journey-card{width:245px;max-width:100%;box-sizing:border-box;padding:7px 8px 6px;border-radius:12px;background:rgba(12,19,30,.94);border:1px solid rgba(255,255,255,.18);box-shadow:0 8px 20px rgba(0,0,0,.30),0 0 0 1px rgba(28,145,255,.08);color:#fff;line-height:1.05;backdrop-filter:blur(12px);-webkit-backdrop-filter:blur(12px);font-size:9px}.payroll-admin-journey-card.selected{border-color:rgba(22,136,255,.65);box-shadow:0 9px 24px rgba(0,0,0,.34),0 0 0 2px rgba(22,136,255,.16)}"
new=".payroll-admin-journey-stack{position:absolute;inset:0;z-index:1200;pointer-events:none;overflow:hidden}.payroll-admin-journey-card{position:absolute;left:0;top:0;width:210px;box-sizing:border-box;padding:7px 8px 6px;border-radius:10px;background:rgba(12,19,30,.95);border:1px solid rgba(255,255,255,.20);box-shadow:0 7px 18px rgba(0,0,0,.34),0 0 0 1px rgba(28,145,255,.08);color:#fff;line-height:1.05;backdrop-filter:blur(10px);-webkit-backdrop-filter:blur(10px);font-size:9px;transform:translate(-50%,-100%);transition:left .18s ease-out,top .18s ease-out}.payroll-admin-journey-card.selected{border-color:rgba(22,136,255,.65);box-shadow:0 9px 24px rgba(0,0,0,.34),0 0 0 2px rgba(22,136,255,.16)}.payroll-admin-journey-card::after{content:\"\";position:absolute;left:50%;bottom:-7px;width:10px;height:10px;background:rgba(12,19,30,.95);border-right:1px solid rgba(255,255,255,.20);border-bottom:1px solid rgba(255,255,255,.20);transform:translateX(-50%) rotate(45deg)}"
if old not in s: raise SystemExit('CSS old not found')
s=s.replace(old,new,1)
old_func_start=s.index('window.payrollEnsureAdminJourneyCard = function')
old_func_end=s.index('\n\nwindow.updateAdminLiveStaffMap =', old_func_start)
func=s[old_func_start:old_func_end]
# Insert a positioning helper and replace function body with cleaner version preserving card content.
new_func=r'''window.payrollPositionAdminJourneyCard = function (state, employeeId, anchorLatLng) {
    const card = state?.journeyCards?.[employeeId];
    const map = state?.map;
    if (!card || !map || !anchorLatLng) return;
    try {
        const point = map.latLngToContainerPoint(anchorLatLng);
        const width = map.getSize().x;
        const height = map.getSize().y;
        const cardWidth = 210;
        const half = cardWidth / 2;
        const x = Math.max(half + 6, Math.min(width - half - 6, point.x));
        const y = Math.max(92, Math.min(height - 14, point.y - 12));
        card.style.left = `${x}px`;
        card.style.top = `${y}px`;
    } catch { }
};

window.payrollRepositionAdminJourneyCards = function (state) {
    if (!state?.journeyCards || !state?.map) return;
    Object.keys(state.journeyCards).forEach(function (id) {
        const employeeId = Number(id);
        const marker = state.markers?.[employeeId];
        if (marker) {
            window.payrollPositionAdminJourneyCard(state, employeeId, marker.getLatLng());
        }
    });
};

window.payrollEnsureAdminJourneyCard = function (state, x, selectedId) {
    if (!state?.map || !x) return null;
    state.journeyCards = state.journeyCards || {};
    const employeeId = Number(x.employeeId);
    if (!Number.isFinite(employeeId) || employeeId <= 0) return null;

    let stack = state.journeyCardStack;
    if (!stack || !stack.isConnected) {
        stack = document.createElement('div');
        stack.className = 'payroll-admin-journey-stack';
        state.map.getContainer().appendChild(stack);
        state.journeyCardStack = stack;
    }

    let card = state.journeyCards[employeeId];
    if (!card || !card.isConnected) {
        card = document.createElement('div');
        card.className = 'payroll-admin-journey-card';
        stack.appendChild(card);
        state.journeyCards[employeeId] = card;
    }

    const name = window.payrollEscapeHtml(x.name || 'Employee');
    const distance = window.payrollFormatRouteDistance(Number(x.distanceMeters) || 0);
    const accuracy = Number(x.accuracyMeters) > 0 ? `±${Math.round(Number(x.accuracyMeters))} m` : 'Unknown';
    const within = Boolean(x.isWithinAllowedRadius);
    const status = String(x.status || 'Live').toLowerCase();
    const isLive = status === 'live';
    const started = x.sessionStartedUtc ? Date.parse(x.sessionStartedUtc) : 0;
    const elapsed = started && !Number.isNaN(started)
        ? window.payrollFormatRouteDuration(Math.max(0, (Date.now() - started) / 1000))
        : '0s';
    const routeState = state.routeStates?.[employeeId] || {};
    const route = routeState.route;
    const routeDistance = route?.distanceMeters > 0
        ? window.payrollFormatRouteDistance(route.distanceMeters)
        : distance;
    const eta = route?.durationSeconds > 0
        ? window.payrollFormatRouteDuration(route.durationSeconds)
        : 'Calculating...';
    const speed = window.payrollFormatSpeed(routeState.speedMps || 0);
    const road = window.payrollEscapeHtml(window.payrollGetNextRoadName(route));
    const arrived = route?.distanceMeters > 0
        ? route.distanceMeters <= Math.max(25, Number(x.allowedRadiusMeters) || 100)
        : (Number(x.distanceMeters) || 0) <= Math.max(25, Number(x.allowedRadiusMeters) || 100);
    const statusText = arrived ? 'ARRIVED' : (isLive ? 'LIVE' : String(x.status || 'STALE').toUpperCase());
    const statusClass = arrived ? 'arrived' : (isLive ? 'live' : 'stale');
    const selectedClass = Number(selectedId) === employeeId ? ' selected' : '';

    card.className = `payroll-admin-journey-card ${statusClass}${selectedClass}`;
    card.innerHTML =
        `<div class="payroll-admin-journey-top">` +
        `<div class="payroll-admin-journey-avatar">${arrived ? '🏁' : '🛵'}</div>` +
        `<div class="payroll-admin-journey-title"><div class="payroll-admin-journey-name">${name}</div><div class="payroll-admin-journey-destination">📍 To Office</div></div>` +
        `<div class="payroll-admin-journey-status ${statusClass}"><span class="dot"></span>${statusText}</div>` +
        `</div>` +
        `<div class="payroll-admin-journey-progress"><span></span></div>` +
        `<div class="payroll-admin-journey-metrics">` +
        `<div><small>REMAINING</small><strong>${routeDistance}</strong></div>` +
        `<div><small>ETA</small><strong>${eta}</strong></div>` +
        `<div><small>SPEED</small><strong>${window.payrollEscapeHtml(speed)}</strong></div>` +
        `<div><small>ACCURACY</small><strong>${accuracy}</strong></div>` +
        `<div><small>JOURNEY</small><strong>${elapsed}</strong></div>` +
        `<div><small>RANGE</small><strong class="${within ? 'ok' : 'bad'}">${within ? 'Within' : 'Outside'}</strong></div>` +
        `</div>` +
        `<div class="payroll-admin-journey-road"><span>🛣️</span><span><b>ROAD</b> ${road}</span></div>` +
        `<div class="payroll-admin-journey-footer"><span>● GPS ${isLive ? 'LIVE' : 'STALE'}</span><span>🏢 Office</span></div>`;

    const marker = state.markers?.[employeeId];
    if (marker) window.payrollPositionAdminJourneyCard(state, employeeId, marker.getLatLng());
    return card;
};'''
s=s[:old_func_start]+new_func+s[old_func_end:]
# Hide old Leaflet rich label markers completely. The new map cards are the sole employee detail cards.
s=s.replace('.payroll-admin-employee-badge-wrap{background:transparent!important;border:0!important;pointer-events:none!important}', '.payroll-admin-employee-badge-wrap{display:none!important;background:transparent!important;border:0!important;pointer-events:none!important}', 1)
# Remove old distance tooltip fallback block, which could recreate a second small label.
start=s.find('                    if (\n                        !state.labels[\n                        employeeId\n                        ]\n                    ) {')
if start != -1:
    end=s.find('\n                    if (isSelected && membershipChanged)', start)
    if end == -1: raise SystemExit('tooltip end not found')
    s=s[:start]+s[end:]
# Add map movement reposition hook after state creation. Find state initialization after admin map.
needle="            let state =\n                window.adminLiveMaps[mapId];"
pos=s.find(needle)
if pos==-1: raise SystemExit('state init needle not found')
# Insert hook later after state initialization block, easiest after journeyCards: {} occurrence nearest.
idx=s.find('journeyCards: {}', pos)
if idx==-1: raise SystemExit('journeyCards init not found')
line_end=s.find('\n', idx)
insert=r'''
                        if (!state.journeyCardMapHandler) {
                            state.journeyCardMapHandler = function () {
                                try { window.payrollRepositionAdminJourneyCards(state); } catch { }
                            };
                            state.map.on('zoom move resize', state.journeyCardMapHandler);
                        }'''
s=s[:line_end+1]+insert+s[line_end+1:]
# Update animation callback to position card with animated marker.
old_cb="""                                    if (state.labels[employeeId]) {
                                        // Keep the employee's rich label attached to the
                                        // same collision-aware visual marker. Never move
                                        // it to the route midpoint, otherwise co-located
                                        // employees' labels collapse into one another.
                                        state.labels[employeeId].setLatLng(animatedPosition);
                                    }"""
new_cb="""                                    if (state.journeyCards?.[employeeId]) {
                                        window.payrollPositionAdminJourneyCard(state, employeeId, animatedPosition);
                                    }"""
if old_cb not in s: raise SystemExit('animation label block not found')
s=s.replace(old_cb,new_cb,1)
# Ensure after card creation at end also reposition all cards.
s=s.replace('                    window.payrollEnsureAdminJourneyCard(state, x, selectedId);\n                    } catch (employeeRenderError)', '                    window.payrollEnsureAdminJourneyCard(state, x, selectedId);\n                    window.payrollRepositionAdminJourneyCards(state);\n                    } catch (employeeRenderError)', 1)
p.write_text(s,encoding='utf-8')
print('patched', p)
