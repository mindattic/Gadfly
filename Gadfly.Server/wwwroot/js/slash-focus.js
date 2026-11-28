// '/' focuses the page's [data-slash-focus] field (My exams' filter, GAD-T846), unless the user is already typing somewhere.
let installed = false;

export function install() {
    if (installed) return;
    installed = true;
    document.addEventListener("keydown", e => {
        if (e.key !== "/" || e.ctrlKey || e.metaKey || e.altKey || e.defaultPrevented) return;
        const t = e.target;
        if (t instanceof HTMLElement && (t.isContentEditable || t.closest("input, textarea, select"))) return;
        const field = document.querySelector("[data-slash-focus]");
        if (!field) return;
        e.preventDefault();
        field.focus();
        field.select?.();
    });
}
