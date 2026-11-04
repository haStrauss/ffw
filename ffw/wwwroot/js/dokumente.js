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

    // Löschen: vor dem Absenden nachfragen.
    document.querySelectorAll('.dokument-loeschen').forEach((loeschenForm) => {
        loeschenForm.addEventListener('submit', (e) => {
            if (!confirm(loeschenForm.dataset.bestaetigung || 'Wirklich löschen?')) {
                e.preventDefault();
            }
        });
    });
})();
