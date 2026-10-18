// Krok 12A: sledování formuláře objednávky. Každá akce uživatele = jedna událost v proudu (JourneyId z formuláře).
// Události se sbírají do fronty a odcházejí dávkově na POST /api/journeys/{journeyId}/events – každé 2 s
// a při odeslání nebo opuštění stránky (sendBeacon doručí i ve chvíli, kdy prohlížeč stránku zavírá).
// Každá událost má vlastní eventId: když se dávka pošle dvakrát, server duplicity přeskočí (idempotence).
(() => {
    const form = document.getElementById('order-form');
    if (!form) {
        return;
    }

    const journeyId = form.dataset.journeyId;
    const endpoint = `/api/journeys/${journeyId}/events`;

    // Čas = milisekundy od otevření formuláře. Po neúspěšném odeslání stránka pokračuje od času, který poslal server.
    const baseOffset = Number(form.dataset.journeyOffset || 0);
    const now = () => Math.round(baseOffset + performance.now());

    // Stav pro ostatní skripty stránky: touched = uživatel už něco udělal (stránka se pak sama neobnovuje).
    const state = window.orderJourney = { journeyId, touched: false };

    let queue = [];

    const textOf = el => (el.textContent || '').replace(/\s+/g, ' ').trim();

    // Lidský popis prvku: popisek pole, text tlačítka/odkazu, nebo aspoň jeho jméno.
    const labelOf = el =>
        (el.labels && el.labels.length ? textOf(el.labels[0]) : null)
        || el.dataset.trackLabel
        || textOf(el)
        || el.value
        || el.name
        || el.id
        || el.tagName.toLowerCase();

    // Technické jméno prvku – podle něj přehrávání (krok 12B) prvek zase najde.
    const targetOf = el =>
        el.dataset.track || el.name || el.id || el.getAttribute('href') || el.tagName.toLowerCase();

    function record(type, el, extra) {
        queue.push({
            eventId: crypto.randomUUID(),
            type,
            offsetMs: now(),
            target: el ? targetOf(el).slice(0, 200) : null,
            label: el ? labelOf(el).slice(0, 200) : null,
            ...extra
        });

        if (type !== 'FormOpened') {
            state.touched = true;
        }
    }

    function flush(leavingPage) {
        // Jen otevřená stránka bez jediné akce (např. automatické obnovení) se neposílá – nezahlcuje databázi prázdnými proudy.
        if (queue.length === 0 || !state.touched) {
            return;
        }

        const batch = queue;
        queue = [];
        const body = JSON.stringify(batch);

        if (leavingPage && navigator.sendBeacon(endpoint, new Blob([body], { type: 'application/json' }))) {
            return;
        }

        fetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body, keepalive: true })
            .then(response => {
                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}`);
                }
            })
            .catch(() => {
                queue = batch.concat(queue);   // zkusí se znovu v dalším kole (server duplicity přeskočí)
            });
    }

    const fields = form.querySelectorAll('input:not([type=hidden]), select, textarea');
    fields.forEach(field => {
        field.addEventListener('focus', () => record('FieldFocused', field));
        field.addEventListener('blur', () => record('FieldBlurred', field));

        // 'input' = každý napsaný znak i výběr v seznamu. Value je celá aktuální hodnota pole – skutečně napsaný text.
        field.addEventListener('input', () => record('FieldChanged', field, {
            value: field.value,
            valueText: field.tagName === 'SELECT' ? textOf(field.selectedOptions[0] || field) : null
        }));
    });

    // Kliky kamkoli na stránce – jen na „klikací“ prvky (tlačítka, odkazy). Klik do pole zachytí FieldFocused.
    document.addEventListener('click', e => {
        const el = e.target.closest('a, button, [data-track]');
        if (el) {
            record('Clicked', el);
        }
    }, true);

    // Prohlížeč sám odmítl pole (type="email", required…) a odeslání zastavil – na server nic nedorazí.
    // Událost 'invalid' nebublá, proto se poslouchá ve fázi capture.
    form.addEventListener('invalid', e => record('FieldInvalid', e.target, { value: e.target.validationMessage }), true);

    form.addEventListener('submit', () => {
        record('SubmitClicked', null);
        // Server zapíše své události (chyba validace, vznik objednávky) hned za tento okamžik.
        form.elements.JourneyOffsetMs.value = now();
        flush(true);
    });

    window.addEventListener('pagehide', () => flush(true));
    setInterval(() => flush(false), 2000);

    // Po neúspěšném odeslání se formulář načte znovu se stejným proudem – „otevření“ už v proudu je.
    if (baseOffset === 0) {
        record('FormOpened', null);
    } else {
        state.touched = true;   // uživatel je uprostřed vyplňování
    }
})();
