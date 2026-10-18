// Krok 12B: přehrávač průběhu objednávky. Scénář (snímky) připravil server – JourneyReplayProjection:
// každý snímek = jedna událost z proudu + čas, kdy ji odehrát (playAtMs, dlouhé pauzy už jsou zkrácené).
// Přehrávač jen posouvá čas a „aplikuje“ události na kopii formuláře.
//
// Přetočení (posuvník) = projekce od začátku: formulář se vrátí do výchozího stavu a všechny události až do zvoleného
// okamžiku se aplikují znovu, tentokrát bez animace. Stav v čase T je vždy „fold“ událostí 0…T – nic jiného se neukládá.
(() => {
    const dataElement = document.getElementById('journey-replay');
    if (!dataElement) {
        return;
    }

    const replay = JSON.parse(dataElement.textContent);
    const frames = replay.frames;
    const steps = replay.journey.steps;
    const duration = replay.durationMs;

    const stage = document.getElementById('replay-stage');
    const caption = document.getElementById('replay-caption');
    const pauseNote = document.getElementById('replay-pause');
    const alertBox = document.getElementById('replay-alert');
    const progress = document.getElementById('replay-progress');
    const clock = document.getElementById('replay-clock');
    const playButton = document.getElementById('replay-play');
    const stepRows = [...document.querySelectorAll('[data-step-index]')];
    const select = stage.querySelector('select');

    // Výchozí stav kopie formuláře – sem se vrací přetočení.
    const fields = [...stage.querySelectorAll('input, select')];
    const initialValues = new Map(fields.map(field => [field, field.value]));
    const initialOptions = select.innerHTML;

    let position = 0;      // čas přehrávání (ms, po zkrácení pauz)
    let next = 0;          // index dalšího snímku, který se ještě neodehrál
    let playing = false;
    let speed = 1;
    let lastTick = 0;

    // Prvek podle Target z události: nejdřív kopie formuláře, pak stránka (odkazy v menu mají stejné data-track).
    // Starší proudy (před data-track) mají u tlačítka jen „button“ – pak pomůže popisek.
    function find(target, label) {
        if (target) {
            const selector = `[data-replay-target="${CSS.escape(target)}"]`;
            const element = stage.querySelector(selector) || document.querySelector(`[data-track="${CSS.escape(target)}"]`);
            if (element) {
                return element;
            }
        }

        return label
            ? [...stage.querySelectorAll('button')].find(b => b.textContent.trim() === label) || null
            : null;
    }

    function ensureOption(value, text) {
        if (![...select.options].some(o => o.value === value)) {
            select.add(new Option(text || `Produkt ${value}`, value));
        }
    }

    function clearFocus() {
        stage.querySelectorAll('.replay-focus').forEach(el => el.classList.remove('replay-focus'));
    }

    function pulse(element) {
        if (!element) {
            return;
        }

        element.classList.remove('replay-click');
        void element.offsetWidth;   // restart CSS animace i při opakovaném kliku
        element.classList.add('replay-click');
    }

    function showAlert(kind, text) {
        alertBox.className = `alert alert-${kind} py-2 small`;
        alertBox.textContent = text;
        alertBox.hidden = false;
    }

    function setStep(index) {
        stepRows.forEach(row => row.classList.toggle('table-active', Number(row.dataset.stepIndex) === index));
        const row = stepRows[index];
        if (row) {
            row.scrollIntoView({ block: 'nearest' });
        }

        caption.textContent = index >= 0 ? steps[index].description : 'Stiskněte Přehrát.';
    }

    // Jedna událost → změna kopie formuláře. animate = false při přetáčení (jen stav, bez blikání).
    function apply(frame, animate) {
        const e = frame.event;
        const element = find(e.target, e.label);

        switch (e.type) {
            case 'FieldFocused':
                clearFocus();
                element?.classList.add('replay-focus');
                break;

            case 'FieldBlurred':
                element?.classList.remove('replay-focus');
                break;

            case 'FieldChanged':
                if (element) {
                    if (element.tagName === 'SELECT') {
                        ensureOption(e.value ?? '', e.valueText);
                    }
                    element.value = e.value ?? '';
                    element.classList.remove('is-invalid');   // uživatel pole opravuje
                }
                break;

            case 'FieldInvalid':
                element?.classList.add('is-invalid');
                showAlert('warning', `Prohlížeč nepustil odeslání – „${e.label}“: ${e.value}`);
                break;

            case 'Clicked':
                if (animate) {
                    pulse(element);
                }
                break;

            case 'SubmitClicked':
                clearFocus();
                alertBox.hidden = true;   // nové odeslání – stará chybová hláška zmizí, jako na skutečné stránce
                if (animate) {
                    pulse(find('order-submit'));
                }
                break;

            case 'ValidationFailed':
                showAlert('danger', `Server formulář vrátil: ${e.value}`);
                break;

            case 'OrderSubmitted':
                showAlert('success', `Objednávka č. ${replay.journey.orderId} vznikla.`);
                break;
        }

        setStep(frame.stepIndex);
    }

    function reset() {
        fields.forEach(field => {
            if (field === select) {
                select.innerHTML = initialOptions;
            }
            field.value = initialValues.get(field);
            field.classList.remove('is-invalid');
        });
        clearFocus();
        alertBox.hidden = true;
        next = 0;
        setStep(-1);
    }

    function formatOffset(ms) {
        const seconds = ms / 1000;
        return seconds >= 60
            ? `+${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`
            : `+${seconds.toFixed(1).replace('.', ',')} s`;
    }

    // Ukazatele: skutečný čas formuláře (z poslední odehrané události) a zkrácená pauza, na kterou se právě čeká.
    function render() {
        progress.value = position;
        const last = frames[next - 1];
        clock.textContent = formatOffset(last ? last.event.offsetMs : 0);

        const upcoming = frames[next];
        if (upcoming && upcoming.skippedPauseMs > 0 && (!last || position > last.playAtMs)) {
            const realPause = Math.round((upcoming.skippedPauseMs + replay.maxPauseMs) / 1000);
            pauseNote.textContent = `⏸ uživatel ${realPause} s nic nedělal (pauza zkrácena)`;
            pauseNote.hidden = false;
        } else {
            pauseNote.hidden = true;
        }
    }

    function runUntil(target, animate) {
        while (next < frames.length && frames[next].playAtMs <= target) {
            apply(frames[next++], animate);
        }
    }

    function seek(target) {
        reset();
        position = Math.max(0, Math.min(duration, target));
        runUntil(position, false);
        render();
    }

    function tick(now) {
        if (!playing) {
            return;
        }

        position = Math.min(duration, position + (now - lastTick) * speed);
        lastTick = now;
        runUntil(position, true);
        render();

        if (position >= duration && next >= frames.length) {
            setPlaying(false);
            return;
        }

        requestAnimationFrame(tick);
    }

    function setPlaying(value) {
        playing = value;
        playButton.textContent = playing ? '⏸ Pauza' : '▶ Přehrát';

        if (playing) {
            if (position >= duration && next >= frames.length) {
                seek(0);   // dohráno → přehrát znovu od začátku
            }
            lastTick = performance.now();
            requestAnimationFrame(tick);
        }
    }

    playButton.addEventListener('click', () => setPlaying(!playing));

    document.getElementById('replay-restart').addEventListener('click', () => {
        seek(0);
        setPlaying(true);
    });

    document.querySelectorAll('[data-speed]').forEach(button => button.addEventListener('click', () => {
        speed = Number(button.dataset.speed);
        document.querySelectorAll('[data-speed]').forEach(b => b.classList.toggle('active', b === button));
    }));

    progress.addEventListener('input', () => seek(Number(progress.value)));

    seek(0);

    // Odkazy „▶ Přehrát“ (?autoplay=1) spustí přehrávání hned po načtení.
    if (new URLSearchParams(location.search).get('autoplay') === '1') {
        setPlaying(true);
    }
})();
