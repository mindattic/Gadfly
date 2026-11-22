// The UI language switch (GAD-T543): remembers the student's choice in ASP.NET Core's culture cookie, so the next page load
// and the next circuit start in it, and keeps <html lang> in step so screen readers pronounce the page right (WCAG 3.1.1).
// The live page re-renders itself from the server; nothing here reloads it.

const YEAR = 60 * 60 * 24 * 365;

export function remember(cookieName, cookieValue, language) {
    const secure = location.protocol === "https:" ? "; secure" : "";
    document.cookie = `${cookieName}=${encodeURIComponent(cookieValue)}; path=/; max-age=${YEAR}; samesite=lax${secure}`;
    setDocumentLanguage(language);
}

export function setDocumentLanguage(language) {
    if (language) document.documentElement.lang = language;
}

// The UI strings under a prefix in one language, for scripts that write text of their own (the exam room, GAD-T544).
export async function strings(language, prefix) {
    const r = await fetch(`/ui-strings/${encodeURIComponent(language)}?prefix=${encodeURIComponent(prefix || "")}`, { credentials: "same-origin" });
    return r.ok ? await r.json() : {};
}
