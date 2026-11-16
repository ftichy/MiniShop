// Vzhled krok D: MiniShop jako instalovatelná aplikace (PWA).
// 1. Zaregistruje service worker (/sw.js) – jen v bezpečném kontextu (HTTPS nebo localhost), jinak ho prohlížeč nenabízí.
// 2. Tlačítka [data-pwa-install]: Android/Chrome/Edge pošle událost beforeinstallprompt – tlačítka ukážeme a klepnutí
//    otevře systémový dotaz „Nainstalovat aplikaci?“. Bez události (už nainstalováno, nepodporovaný prohlížeč) zůstanou skrytá.
// 3. iPhone/iPad (Safari) instalaci tlačítkem neumí – ukážeme [data-pwa-ios-hint] s návodem „Sdílet → Přidat na plochu“.
// Soubor, ne inline skript (CSP script-src 'self').
(function () {
    'use strict';

    if ('serviceWorker' in navigator) {
        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(function () {
                // Bez service workeru web funguje dál, jen bez offline stránky a rychlejšího startu.
            });
        });
    }

    var standalone = window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    if (standalone) {
        document.documentElement.classList.add('is-standalone');   // spuštěno z ikony – pro styly
        return;   // už je nainstalovaná, nic nenabízíme
    }

    var installButtons = document.querySelectorAll('[data-pwa-install]');
    var deferredPrompt = null;

    window.addEventListener('beforeinstallprompt', function (event) {
        event.preventDefault();   // vlastní tlačítko místo automatické lišty prohlížeče
        deferredPrompt = event;
        installButtons.forEach(function (button) { button.hidden = false; });
    });

    installButtons.forEach(function (button) {
        button.addEventListener('click', function () {
            if (!deferredPrompt) {
                return;
            }

            deferredPrompt.prompt();
            deferredPrompt.userChoice.finally(function () {
                deferredPrompt = null;   // dotaz jde ukázat jen jednou
                installButtons.forEach(function (b) { b.hidden = true; });
            });
        });
    });

    window.addEventListener('appinstalled', function () {
        installButtons.forEach(function (button) { button.hidden = true; });
    });

    // iPadOS se hlásí jako Mac – pozná se podle dotyku.
    var ios = /iPhone|iPad|iPod/.test(navigator.userAgent)
        || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    if (ios) {
        document.querySelectorAll('[data-pwa-ios-hint]').forEach(function (hint) { hint.hidden = false; });
    }
})();
