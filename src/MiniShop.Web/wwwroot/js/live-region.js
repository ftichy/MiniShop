// Živé výřezy (tag helper live-region): část stránky, kterou htmx obnoví na událost „live-refresh“ – stáhne tutéž
// stránku na pozadí a vymění jen výřez. Tenhle soubor rozhoduje, KDY obnovit:
//  - vzhled krok F3a: výřez s tématem (data-live-topic) a připojený SignalR (/hubs/live) → hned na signál od serveru
//    „changed(téma)“, pojistně jednou za 60 s (signál se mohl ztratit);
//  - bez tématu, bez spojení nebo bez práva na téma → každé 3 s (jako vzhled krok F2).
// A pojistka (vzhled krok F2): výměnu odloží, dokud ve výřezu uživatel něco mění (zaškrtnutí, text, rozbalený formulář).
(function () {
    'use strict';

    if (!window.htmx) {
        return;
    }

    const FALLBACK_MS = 3000;       // bez signálu – jako F2
    const SIGNALED_MS = 60000;      // se signálem – jen pojistka (vzhled F3b: signál chodí i pro kroky zásilky)
    const RECONNECT_MS = 15000;     // nové spojení po úplném odpojení
    const HUB_URL = '/hubs/live';

    let connection = null;
    let connected = false;
    let nextAttempt = 0;
    const subscriptions = new Map();   // téma → 'pending' | 'ok' | 'denied'

    // Pro E2E testy a ladění v konzoli: window.minishopLive.connected.
    window.minishopLive = {
        get connected() { return connected; },
        topics: subscriptions
    };

    function regions() {
        return document.querySelectorAll('[data-live-region][hx-get]');
    }

    function refresh(region) {
        region.dataset.liveLast = String(Date.now());
        htmx.trigger(region, 'live-refresh');
    }

    function intervalFor(region) {
        const topic = region.dataset.liveTopic;
        return connected && topic && subscriptions.get(topic) === 'ok' ? SIGNALED_MS : FALLBACK_MS;
    }

    // Jeden časovač pro všechny výřezy (i ty, které htmx právě vyměnil – nový prvek začíná počítat znovu).
    setInterval(function () {
        const now = Date.now();
        for (const region of regions()) {
            if (!region.dataset.liveLast) {
                region.dataset.liveLast = String(now);
                continue;
            }

            if (now - Number(region.dataset.liveLast) >= intervalFor(region)) {
                refresh(region);
            }
        }

        connectIfNeeded(now);
        subscribeAll();
    }, 1000);

    function wantedTopics() {
        const topics = new Set();
        for (const region of regions()) {
            if (region.dataset.liveTopic) {
                topics.add(region.dataset.liveTopic);
            }
        }
        return topics;
    }

    function connectIfNeeded(now) {
        if (!window.signalR || connection || now < nextAttempt || wantedTopics().size === 0) {
            return;
        }

        // Rovnou WebSocket bez úvodního vyjednávání: dvě kopie Webu za Caddy – vyjednávání a spojení by jinak mohly trefit
        // různé kopie. Když WebSocket nejde, zůstane dotazování každé 3 s.
        connection = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL, { skipNegotiation: true, transport: signalR.HttpTransportType.WebSockets })
            .withAutomaticReconnect({ nextRetryDelayInMilliseconds: ctx => Math.min(30000, 1000 * Math.pow(2, ctx.previousRetryCount)) })
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        connection.on('changed', function (topic) {
            for (const region of regions()) {
                if (region.dataset.liveTopic === topic) {
                    refresh(region);
                }
            }
        });
        connection.onreconnecting(function () { connected = false; });
        connection.onreconnected(function () {
            connected = true;
            subscriptions.clear();   // skupiny na serveru se s novým spojením ztratily
            subscribeAll();
        });
        connection.onclose(function () {
            connected = false;
            connection = null;
            subscriptions.clear();
            nextAttempt = Date.now() + RECONNECT_MS;
        });

        connection.start()
            .then(function () { connected = true; subscribeAll(); })
            .catch(function () {
                connection = null;
                nextAttempt = Date.now() + RECONNECT_MS;
            });
    }

    function subscribeAll() {
        if (!connection || !connected) {
            return;
        }

        for (const topic of wantedTopics()) {
            if (subscriptions.has(topic)) {
                continue;
            }

            subscriptions.set(topic, 'pending');
            connection.invoke('Subscribe', topic)
                .then(function () {
                    subscriptions.set(topic, 'ok');
                    // Mezi vykreslením stránky a odběrem se mohlo něco změnit – jednou dohnat.
                    for (const region of regions()) {
                        if (region.dataset.liveTopic === topic) {
                            refresh(region);
                        }
                    }
                })
                .catch(function () { subscriptions.set(topic, 'denied'); });   // bez práva – zůstane dotazování
        }
    }

    // Pojistka rozepsaného formuláře (vzhled krok F2).
    function isEditing(region) {
        const active = document.activeElement;
        if (active && region.contains(active) && active.matches('input, textarea, select')) {
            return true;   // kurzor v poli – uživatel právě píše nebo vybírá
        }

        if (region.querySelector('details[open]')) {
            return true;   // rozbalený formulář (ruční odeslání, vratka)
        }

        for (const field of region.querySelectorAll('input, textarea, select')) {
            if (field.type === 'hidden') {
                continue;
            }

            if (field.type === 'checkbox' || field.type === 'radio') {
                if (field.checked !== field.defaultChecked) {
                    return true;
                }
            } else if (field.tagName === 'SELECT') {
                if (Array.from(field.options).some(o => o.selected !== o.defaultSelected)) {
                    return true;
                }
            } else if (field.value !== field.defaultValue) {
                return true;
            }
        }

        return false;
    }

    document.body.addEventListener('htmx:beforeSwap', function (e) {
        const target = e.detail.target;
        if (target && target.hasAttribute && target.hasAttribute('data-live-region') && isEditing(target)) {
            e.detail.shouldSwap = false;
        }
    });
})();
