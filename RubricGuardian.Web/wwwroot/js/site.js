// Progressive enhancement only: forms and buttons work fine without this script.
// Marks a form's submit button as loading (spinner + custom text) the moment a
// slow, AI-backed submit happens, so a 12-90s+ round trip doesn't look frozen.
(function () {
    document.querySelectorAll('form[data-slow-submit]').forEach(function (form) {
        form.addEventListener('submit', function () {
            if (form.dataset.submitting === '1') return;
            form.dataset.submitting = '1';
            var btn = form.querySelector('[data-submit-button]');
            if (!btn) return;
            var text = btn.dataset.loadingText || 'Working…';
            btn.setAttribute('disabled', 'disabled');
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>' + text;
        });
    });
})();
