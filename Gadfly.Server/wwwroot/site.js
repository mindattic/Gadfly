// Public site (GAD-T308): registers the small service worker that shows offline.html when a page can't be reached, and
// mounts the live examiner (GAD-T692) where a page places <LiveExaminer>: these pages run without a Blazor circuit, so the
// component writes its mount config into data-live-examiner and this starts her. A page that places <ExaminerPresence> alone
// (the glowing orb, GAD-T840) gets its sphere started here too. The pages work without either.
(function () {
    if (!('serviceWorker' in navigator) || location.protocol !== 'https:' && location.hostname !== 'localhost') return;
    window.addEventListener('load', function () {
        navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(function () { /* offline page is a nicety */ });
    });
})();
(function () {
    var hosts = document.querySelectorAll('.live-examiner[data-live-examiner]');
    var orbs = Array.prototype.filter.call(document.querySelectorAll('.examiner-presence'), function (el) { return !el.closest('[data-live-examiner]'); });
    if (!hosts.length && !orbs.length) return;
    import('/js/live-examiner.js').then(function (m) {
        hosts.forEach(function (host) {
            try { m.mount(host, JSON.parse(host.getAttribute('data-live-examiner'))); } catch (e) { /* the presence's CSS orb stays */ }
        });
        orbs.forEach(function (el) {
            try { m.mountPresence(el); } catch (e) { /* the CSS orb stays */ }
        });
    }).catch(function () { /* the presence's CSS orb stays */ });
})();
