window.IdiotProof = {
    downloadFile: function (filename, mimeType, content) {
        const blob = new Blob([content], { type: mimeType });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    scrollToId: function (id) {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};

// Theme switch (dark default). Sets the DOM attributes the pre-paint script and
// CSS read, and persists to localStorage so the choice survives a reload.
// Dark and light share all structure; only colors swap (light = Alpaca palette).
window.ipSetTheme = function (t) {
    t = (t === 'light' || t === 'dark') ? t : 'dark';
    var r = document.documentElement;
    r.setAttribute('data-theme', t);
    r.setAttribute('data-bs-theme', t);
    try { localStorage.setItem('idiotproof.theme', t); } catch (e) {}
    return t;
};
window.ipGetTheme = function () {
    try { return localStorage.getItem('idiotproof.theme') || 'dark'; } catch (e) { return 'dark'; }
};
