// E-shop krok 21: našeptávač hledání (hlavička i stránka Obchod). Pole označí atribut data-suggest="/api/catalog/suggest".
// Bez JavaScriptu (nebo při chybě) zůstává obyčejný formulář GET /Shop?q=… – našeptávač je jen zkratka.
// CSP pouští jen skripty ze souborů (účty krok 8). Texty z odpovědi jdou jen přes textContent (žádné innerHTML → žádné XSS).
(function () {
    'use strict';

    var DELAY_MS = 200;       // počkat, až zákazník dopíše – ne dotaz na každé písmeno
    var MIN_LENGTH = 2;       // stejně jako server (CatalogQueryService.SuggestionMinLength)
    var price = new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 0 });
    var counter = 0;

    document.querySelectorAll('input[data-suggest]').forEach(function (input) {
        var url = input.getAttribute('data-suggest');
        var container = input.closest('.search-suggest') || input.parentElement;
        var list = document.createElement('ul');
        var listId = 'search-suggest-' + (++counter);
        var timer = null;
        var request = null;
        var options = [];
        var active = -1;

        list.id = listId;
        list.className = 'search-suggest-list';
        list.setAttribute('role', 'listbox');
        list.setAttribute('aria-label', 'Návrhy hledání');
        list.hidden = true;
        container.appendChild(list);

        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-controls', listId);

        input.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(load, DELAY_MS);
        });

        input.addEventListener('keydown', function (e) {
            if (list.hidden) {
                return;
            }

            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                var step = e.key === 'ArrowDown' ? 1 : -1;
                setActive((active + 1 + step + options.length + 1) % (options.length + 1) - 1);   // -1 = zpět do pole
            } else if (e.key === 'Enter' && active >= 0) {
                e.preventDefault();
                window.location.href = options[active].href;
            } else if (e.key === 'Escape') {
                close();
            }
        });

        input.addEventListener('blur', function () {
            setTimeout(close, 150);   // klik do nabídky musí stihnout odkaz
        });

        function load() {
            var query = input.value.trim();
            if (query.length < MIN_LENGTH) {
                close();
                return;
            }

            if (request) {
                request.abort();   // starší odpověď by mohla přijít až po novější
            }

            request = new AbortController();
            fetch(url + '?q=' + encodeURIComponent(query), { signal: request.signal, headers: { Accept: 'application/json' } })
                .then(function (response) { return response.ok ? response.json() : null; })
                .then(function (data) {
                    if (data && input.value.trim() === query) {
                        render(data, query);
                    }
                })
                .catch(function () { /* zrušeno nebo bez sítě – formulář funguje dál */ });
        }

        function render(data, query) {
            list.replaceChildren();
            options = [];
            active = -1;
            input.removeAttribute('aria-activedescendant');

            if (data.isSimilarMatch) {
                list.appendChild(heading('Podobné výsledky'));
            }

            (data.categories || []).forEach(function (c) {
                addOption('/Shop?category=' + c.id, 'search-suggest-category', function (a) {
                    a.appendChild(text('span', 'Kategorie '));
                    a.appendChild(text('strong', c.name));
                    a.appendChild(text('span', ' (' + c.productCount + ')', 'text-muted'));
                });
            });

            (data.products || []).forEach(function (p) {
                addOption('/Shop/' + p.id, 'search-suggest-product', function (a) {
                    var thumb = document.createElement('span');
                    thumb.className = 'search-suggest-thumb';
                    if (p.imageFileName) {
                        var img = document.createElement('img');
                        img.src = '/media/products/' + encodeURIComponent(p.imageFileName);
                        img.alt = '';
                        img.loading = 'lazy';
                        thumb.appendChild(img);
                    }
                    a.appendChild(thumb);
                    a.appendChild(text('span', p.name, 'search-suggest-name'));
                    a.appendChild(text('span', price.format(p.price), 'search-suggest-price'));
                });
            });

            if (options.length === 0) {
                close();
                return;
            }

            addOption('/Shop?q=' + encodeURIComponent(query), 'search-suggest-all', function (a) {
                a.textContent = 'Zobrazit všechny výsledky';
            });

            list.hidden = false;
            input.setAttribute('aria-expanded', 'true');
        }

        function addOption(href, className, fill) {
            var li = document.createElement('li');
            var a = document.createElement('a');
            li.setAttribute('role', 'option');
            li.id = listId + '-' + options.length;
            a.href = href;
            a.className = className;
            a.tabIndex = -1;
            a.setAttribute('data-track', 'search-suggest');
            fill(a);
            li.appendChild(a);
            list.appendChild(li);
            options.push({ li: li, href: href });
        }

        function setActive(index) {
            options.forEach(function (o, i) {
                o.li.classList.toggle('active', i === index);
                o.li.setAttribute('aria-selected', i === index ? 'true' : 'false');
            });
            active = index;
            if (index >= 0) {
                input.setAttribute('aria-activedescendant', options[index].li.id);
                options[index].li.scrollIntoView({ block: 'nearest' });
            } else {
                input.removeAttribute('aria-activedescendant');
            }
        }

        function close() {
            list.hidden = true;
            active = -1;
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
        }
    });

    function heading(value) {
        var li = document.createElement('li');
        li.className = 'search-suggest-heading';
        li.setAttribute('role', 'presentation');
        li.textContent = value;
        return li;
    }

    function text(tag, value, className) {
        var el = document.createElement(tag);
        el.textContent = value;
        if (className) {
            el.className = className;
        }
        return el;
    }
})();
