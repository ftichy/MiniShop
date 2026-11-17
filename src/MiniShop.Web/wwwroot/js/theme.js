// Vzhled krok E: tmavý režim. Načítá se v <head> BEZ defer – musí nastavit data-bs-theme na <html> dřív, než se stránka
// vykreslí, jinak by při každém načtení na okamžik probliklo bílé pozadí. Soubor, ne inline skript (CSP script-src 'self').
// Volba: „auto“ (výchozí – podle systému telefonu/počítače), „light“, „dark“. Ukládá se jen v tomto prohlížeči
// (localStorage) – bez serveru, bez účtu, platí i pro nepřihlášené.
(function () {
    'use strict';

    var KEY = 'minishop-theme';
    var COLORS = { light: '#4f46e5', dark: '#161b26' };   // barva lišty telefonu (meta theme-color)
    var system = window.matchMedia('(prefers-color-scheme: dark)');

    function stored() {
        try {
            var value = window.localStorage.getItem(KEY);
            return value === 'light' || value === 'dark' ? value : 'auto';
        } catch (e) {
            return 'auto';   // soukromý režim / zakázané úložiště – podle systému
        }
    }

    function resolve(choice) {
        return choice === 'auto' ? (system.matches ? 'dark' : 'light') : choice;
    }

    function apply(choice) {
        var theme = resolve(choice);
        document.documentElement.setAttribute('data-bs-theme', theme);
        document.querySelectorAll('meta[name="theme-color"]').forEach(function (meta) {
            meta.setAttribute('content', COLORS[theme]);
        });
        document.querySelectorAll('[data-theme-choice]').forEach(function (button) {
            var active = button.getAttribute('data-theme-choice') === choice;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
    }

    apply(stored());

    // Systém přepnul (např. večer automaticky do tmavého) – platí jen pro volbu „Automaticky“.
    system.addEventListener('change', function () {
        if (stored() === 'auto') {
            apply('auto');
        }
    });

    // Přepínač v patičce (_Layout) – tlačítka existují až po načtení stránky.
    document.addEventListener('DOMContentLoaded', function () {
        apply(stored());
        document.querySelectorAll('[data-theme-choice]').forEach(function (button) {
            button.addEventListener('click', function () {
                var choice = button.getAttribute('data-theme-choice');
                try {
                    if (choice === 'auto') {
                        window.localStorage.removeItem(KEY);
                    } else {
                        window.localStorage.setItem(KEY, choice);
                    }
                } catch (e) {
                    // bez úložiště platí volba jen do obnovení stránky
                }
                apply(choice);
                document.dispatchEvent(new CustomEvent('minishop:theme-changed', { detail: resolve(choice) }));
            });
        });
    });
})();
