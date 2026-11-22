// Public site (GAD-T308): registers the small service worker that shows offline.html when a page can't be reached.
// Nothing else; the public pages need no script to work.
(function () {
    if (!('serviceWorker' in navigator) || location.protocol !== 'https:' && location.hostname !== 'localhost') return;
    window.addEventListener('load', function () {
        navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(function () { /* offline page is a nicety */ });
    });
})();
