// Příručka v aplikaci (/Docs): vykreslí diagramy Mermaid a k velkým přidá tlačítko „Plná velikost“.
// securityLevel 'loose' = funguje proklik z uzlu diagramu na detailní stránku (click … "/Docs/…").
// Je to bezpečné, protože diagramy píšeme jen my v docs/prirucka (žádný vstup od uživatele).
(async () => {
    mermaid.initialize({ startOnLoad: false, securityLevel: 'loose', theme: 'default' });
    await mermaid.run({ querySelector: '.doc-content .mermaid' });

    for (const diagram of document.querySelectorAll('.doc-content .mermaid')) {
        const wrapper = document.createElement('div');
        wrapper.className = 'doc-diagram';
        diagram.replaceWith(wrapper);
        wrapper.append(diagram);

        const svg = diagram.querySelector('svg');
        const naturalWidth = svg?.viewBox?.baseVal?.width ?? 0;
        if (!svg || naturalWidth <= wrapper.clientWidth) {
            continue;   // diagram se vejde – není co zvětšovat
        }

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-sm btn-outline-secondary doc-diagram-toggle';
        button.textContent = 'Plná velikost';
        button.addEventListener('click', () => {
            const expanded = wrapper.classList.toggle('expanded');
            svg.style.maxWidth = expanded ? 'none' : `${naturalWidth}px`;
            svg.style.width = expanded ? `${naturalWidth}px` : '100%';
            button.textContent = expanded ? 'Přizpůsobit šířce' : 'Plná velikost';
        });
        wrapper.prepend(button);
    }
})();
