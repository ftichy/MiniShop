// E-shop krok 23: zapnutí a vypnutí oznámení push na tomto zařízení (stránka Nastavení e-mailů a oznámení).
// 1. Prohlížeč se zeptá na souhlas (Notification.requestPermission – jen po kliknutí, jinak ho prohlížeč zablokuje).
// 2. pushManager.subscribe s veřejným klíčem VAPID → prohlížeč si u své push služby (Google, Apple, Mozilla) vytvoří
//    „schránku“ a vrátí její adresu (endpoint) a dva klíče pro šifrování.
// 3. Ty pošleme MiniShopu (POST ?handler=PushSubscribe, JSON, antiforgery token v hlavičce RequestVerificationToken).
// Oznámení samotné ukazuje service worker (sw.js, událost push). Soubor, ne inline skript (CSP script-src 'self').
(function () {
    'use strict';

    var root = document.querySelector('[data-push-settings]');
    if (!root) {
        return;
    }

    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var error = root.querySelector('[data-push-error]');

    function show(state) {
        root.querySelectorAll('[data-push-state]').forEach(function (element) {
            element.hidden = element.getAttribute('data-push-state') !== state;
        });
    }

    function fail() {
        if (error) {
            error.hidden = false;
        }
    }

    // Veřejný klíč VAPID je base64url – prohlížeč ho chce jako bajty.
    function keyBytes(base64Url) {
        var base64 = (base64Url + '='.repeat((4 - base64Url.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
        var raw = window.atob(base64);
        var bytes = new Uint8Array(raw.length);
        for (var i = 0; i < raw.length; i++) {
            bytes[i] = raw.charCodeAt(i);
        }
        return bytes;
    }

    function post(handler, body) {
        return fetch('?handler=' + handler, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': tokenInput ? tokenInput.value : ''
            },
            body: JSON.stringify(body)
        }).then(function (response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }
        });
    }

    function register(subscription) {
        var json = subscription.toJSON();
        return post('PushSubscribe', { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth });
    }

    var standalone = window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    var ios = /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) {
        show(ios && !standalone ? 'ios-install' : 'unsupported');
        return;
    }

    var publicKey = keyBytes(root.getAttribute('data-push-key'));

    navigator.serviceWorker.ready.then(function (registration) {
        return registration.pushManager.getSubscription().then(function (subscription) {
            if (Notification.permission === 'denied') {
                show('denied');
            } else if (subscription) {
                show('on');
                // Server o zařízení mohl zapomenout (odebrané ze seznamu, push služba ho hlásila jako neplatné) –
                // přihlášení je idempotentní, tak ho tiše obnovíme.
                register(subscription).catch(function () { });
            } else {
                show('off');
            }

            root.querySelector('[data-push-subscribe]').addEventListener('click', function () {
                if (error) {
                    error.hidden = true;
                }

                Notification.requestPermission().then(function (permission) {
                    if (permission !== 'granted') {
                        show(permission === 'denied' ? 'denied' : 'off');
                        return null;
                    }

                    return registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: publicKey })
                        .catch(function () {
                            // Staré přihlášení s jiným klíčem (klíče VAPID se vyměnily) – zrušit a zkusit znovu.
                            return registration.pushManager.getSubscription()
                                .then(function (old) { return old ? old.unsubscribe() : true; })
                                .then(function () {
                                    return registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: publicKey });
                                });
                        })
                        .then(register)
                        .then(function () { window.location.reload(); });
                }).catch(fail);
            });

            root.querySelector('[data-push-unsubscribe]').addEventListener('click', function () {
                registration.pushManager.getSubscription().then(function (current) {
                    if (!current) {
                        show('off');
                        return null;
                    }

                    return post('PushUnsubscribe', { endpoint: current.endpoint })
                        .then(function () { return current.unsubscribe(); })
                        .then(function () { window.location.reload(); });
                }).catch(fail);
            });
        });
    }).catch(function () {
        show('unsupported');
    });
})();
