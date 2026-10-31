(() => {
    // Upload: Knopf sperren, damit ein langer Upload nicht doppelt abgeschickt wird.
    const form = document.getElementById('upload-form');
    if (form) {
        form.addEventListener('submit', () => {
            const knopf = form.querySelector('button[type="submit"]');
            if (knopf) {
                knopf.disabled = true;
                knopf.textContent = 'Wird hochgeladen …';
            }
        });
    }

    // Lightbox: Großansicht mit Vor/Zurück innerhalb eines Oberpunkts.
    const dialog = document.getElementById('lightbox');
    if (!dialog) {
        return;
    }

    const bild = dialog.querySelector('img');
    let kacheln = [];
    let index = 0;

    const zeige = (i) => {
        index = (i + kacheln.length) % kacheln.length;
        bild.src = kacheln[index].href;
    };

    document.querySelectorAll('.kachel').forEach((kachel) => {
        kachel.addEventListener('click', (e) => {
            e.preventDefault();
            kacheln = Array.from(kachel.closest('.raster').querySelectorAll('.kachel'));
            zeige(kacheln.indexOf(kachel));
            dialog.showModal();
        });
    });

    dialog.querySelector('.lb-schliessen').addEventListener('click', () => dialog.close());
    dialog.querySelector('.lb-zurueck').addEventListener('click', () => zeige(index - 1));
    dialog.querySelector('.lb-weiter').addEventListener('click', () => zeige(index + 1));

    // Klick auf den dunklen Hintergrund schließt.
    dialog.addEventListener('click', (e) => {
        if (e.target === dialog) {
            dialog.close();
        }
    });

    dialog.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowLeft') {
            zeige(index - 1);
        } else if (e.key === 'ArrowRight') {
            zeige(index + 1);
        }
    });

    dialog.addEventListener('close', () => bild.removeAttribute('src'));
})();
