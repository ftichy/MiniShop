// Stránkování seznamů (partial _Pager): po výběru „Na stránku“ formulář rovnou odeslat (GET, vrátí se na 1. stránku).
// CSP pouští jen skripty ze souborů (účty krok 8) – stránka jen označí select atributem data-pager-size.
(function () {
    'use strict';

    document.querySelectorAll('select[data-pager-size]').forEach(function (select) {
        select.addEventListener('change', function () {
            select.form.submit();
        });
    });
})();
