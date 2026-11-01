// Automatické obnovení stránky Objednávky (čekající objednávky, načítání katalogu).
// Účty krok 8: dřív inline <script> na stránce – CSP (script-src 'self') pouští jen skripty ze souborů,
// proto stránka jen označí prvek atributem data-reload-when-idle a tenhle soubor zařídí zbytek.
// Krok 12A: jakmile uživatel začal formulář vyplňovat, stránka se neobnoví – jinak by smazala rozepsaný
// text a rozdělila proud událostí na kusy.
(function () {
    'use strict';

    if (!document.querySelector('[data-reload-when-idle]')) {
        return;
    }

    setTimeout(function check() {
        if (window.orderJourney && window.orderJourney.touched) {
            setTimeout(check, 2000);
        } else {
            location.reload();
        }
    }, 2000);
})();
