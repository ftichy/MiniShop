// Vzhled krok F2: pojistka živého výřezu (tag helper live-region). htmx výřez každé 3 s vymění za čerstvý ze serveru –
// to ale nesmí smazat, co uživatel ve výřezu právě dělá: zaškrtnuté objednávky k zabalení, rozepsané číslo zásilky,
// důvod zamítnutí vratky, rozbalený formulář. Dokud je ve výřezu něco změněného (nebo v něm má uživatel kurzor),
// výměna se odloží – htmx to zkusí při dalším tiku. (Nahrazuje hlídání „neobnovuj, když uživatel píše“ z orders-reload.js.)
(function () {
    'use strict';

    if (!window.htmx) {
        return;
    }

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
