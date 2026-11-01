// Pre-paint: apply the stored theme (dark default) before CSS to avoid a flash.
// Dark and light share all structure (font, radius, spacing); only colors swap.
// A static file rather than an inline <script> so the auth pages' strict CSP
// (script-src 'self' + nonce, from MindAttic.Authentication) allows it (IP-T350).
(function () {
  try {
    var t = localStorage.getItem('idiotproof.theme');
    t = (t === 'light' || t === 'dark') ? t : 'dark';
    var r = document.documentElement;
    r.setAttribute('data-theme', t);
    r.setAttribute('data-bs-theme', t);
  } catch (e) {}
})();
