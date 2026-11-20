// Vzhled krok F1: „Do košíku“ bez obnovení stránky. Formulář pošle htmx na pozadí (atributy hx-post v _ProductCard
// a na detailu produktu), server vrátí nový odznak košíku a hlášku (partial _CartAddResult, výměna podle id).
// Tenhle soubor jen hlášku ukáže (Bootstrap toast) a postará se o chybu sítě. Bez JavaScriptu formulář funguje jako dřív.
(function () {
    'use strict';

    if (!window.htmx || !window.bootstrap) {
        return;
    }

    function showToast() {
        const toast = document.querySelector('#cart-toast .toast');
        if (toast) {
            bootstrap.Toast.getOrCreateInstance(toast, { delay: 4000 }).show();
        }
    }

    // Hláška přišla v odpovědi (out-of-band výměna #cart-toast).
    document.body.addEventListener('htmx:oobAfterSwap', function (e) {
        if (e.detail.target && e.detail.target.id === 'cart-toast') {
            showToast();
        }
    });

    // Server neodpověděl (výpadek, chyba 5xx, neplatný formulář) – zákazník musí vědět, že se nic nepřidalo.
    function showFailure(e) {
        const form = e.detail.elt && e.detail.elt.closest ? e.detail.elt.closest('form[data-cart-add]') : null;
        if (!form) {
            return;
        }

        const container = document.getElementById('cart-toast');
        container.innerHTML = '';
        const toast = document.createElement('div');
        toast.className = 'toast align-items-center border-0 text-bg-danger';
        toast.setAttribute('role', 'alert');
        toast.setAttribute('data-track', 'cart-toast-error');
        const body = document.createElement('div');
        body.className = 'toast-body';
        body.textContent = 'Zboží se nepodařilo přidat do košíku. Obnovte stránku a zkuste to znovu.';
        toast.appendChild(body);
        container.appendChild(toast);
        showToast();
    }

    document.body.addEventListener('htmx:responseError', showFailure);
    document.body.addEventListener('htmx:sendError', showFailure);
})();
