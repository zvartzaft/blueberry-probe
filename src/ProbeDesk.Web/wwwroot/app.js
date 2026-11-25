window.pd = {
  setTheme(t) { document.documentElement.dataset.theme = t; localStorage.setItem('pd-theme', t); },
  getTheme() { return document.documentElement.dataset.theme || 'light'; },
  getRail() { return localStorage.getItem('pd-rail') === 'collapsed'; },
  setRail(collapsed) { localStorage.setItem('pd-rail', collapsed ? 'collapsed' : 'open'); },
  copy(text) { return navigator.clipboard.writeText(text); },
  focus(id) { document.getElementById(id)?.focus(); },
  registerPalette(ref) {
    document.addEventListener('keydown', e => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        ref.invokeMethodAsync('OpenPalette');
      }
    });
  }
};
(function () {
  const saved = localStorage.getItem('pd-theme');
  document.documentElement.dataset.theme = saved || 'light';
})();
