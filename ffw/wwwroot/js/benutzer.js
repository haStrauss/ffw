(() => {
    // Gefährliche Aktionen (löschen, Passwort setzen, 2FA zurücksetzen): vor dem Absenden nachfragen.
    document.querySelectorAll('form.bestaetigen').forEach((form) => {
        form.addEventListener('submit', (e) => {
            if (!confirm(form.dataset.bestaetigung || 'Wirklich ausführen?')) {
                e.preventDefault();
            }
        });
    });

    // Zufälliges Passwort erzeugen (16 Zeichen, mit allen geforderten Zeichenarten) und sichtbar machen,
    // damit man es notieren und weitergeben kann. Es entsteht nur im Browser und wird erst beim Absenden übertragen.
    const gruppen = [
        'ABCDEFGHJKLMNPQRSTUVWXYZ',
        'abcdefghijkmnopqrstuvwxyz',
        '23456789',
        '!#$%&*+-=?@'
    ];

    // Gleichverteilte Zufallszahl in [0, n) ohne Modulo-Verzerrung.
    const zufall = (n) => {
        const grenze = Math.floor(0x100000000 / n) * n;
        const puffer = new Uint32Array(1);
        do {
            crypto.getRandomValues(puffer);
        } while (puffer[0] >= grenze);
        return puffer[0] % n;
    };

    const neuesPasswort = () => {
        const alle = gruppen.join('');
        const zeichen = gruppen.map((g) => g[zufall(g.length)]);
        while (zeichen.length < 16) {
            zeichen.push(alle[zufall(alle.length)]);
        }

        // Mischen, damit die vier Pflichtzeichen nicht immer vorne stehen.
        for (let i = zeichen.length - 1; i > 0; i--) {
            const j = zufall(i + 1);
            [zeichen[i], zeichen[j]] = [zeichen[j], zeichen[i]];
        }

        return zeichen.join('');
    };

    document.querySelectorAll('.zufallspasswort').forEach((knopf) => {
        knopf.addEventListener('click', () => {
            const feld = document.querySelector(knopf.dataset.ziel);
            if (feld) {
                feld.value = neuesPasswort();
                feld.type = 'text';
            }
        });
    });
})();
