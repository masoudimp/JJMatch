// Browser-side helpers for the playground. Everything is event-delegated on document, so Blazor may re-render
// any element at any time without re-initialization.
(function () {
    "use strict";

    // Theme: "light", "dark" or "auto" (follow the OS). index.html applies the same rule before first paint.
    const themeKey = "jjmatch.theme";
    const darkMedia = window.matchMedia("(prefers-color-scheme: dark)");

    function themeGet() {
        try {
            const stored = localStorage.getItem(themeKey);
            return stored === "light" || stored === "dark" ? stored : "auto";
        } catch {
            return "auto";
        }
    }

    function themeApply(preference) {
        const effective = preference === "auto" ? (darkMedia.matches ? "dark" : "light") : preference;
        document.documentElement.setAttribute("data-bs-theme", effective);
    }

    function themeSet(preference) {
        try {
            if (preference === "auto") localStorage.removeItem(themeKey);
            else localStorage.setItem(themeKey, preference);
        } catch {
            // Storage may be unavailable; the choice then lasts for this page only.
        }
        themeApply(preference);
    }

    darkMedia.addEventListener("change", () => themeApply(themeGet()));
    themeApply(themeGet());

    // Tooltips: a ".tip" holds a ".tip-trigger" button and a ".tip-body". The body is fixed-positioned next to
    // its trigger and flipped or shifted to stay inside the viewport. It opens on hover or focus, and a click or
    // tap on the trigger pins it until the next click elsewhere or Escape.
    let openTip = null;
    let pinnedTip = null;

    function bodyOf(tip) {
        return tip.querySelector(".tip-body");
    }

    function place(tip) {
        const body = bodyOf(tip);
        const anchor = tip.querySelector(".tip-trigger").getBoundingClientRect();
        body.style.left = "0px";
        body.style.top = "0px";
        const width = body.offsetWidth;
        const height = body.offsetHeight;
        const gap = 6;
        const margin = 8;
        const left = Math.max(margin, Math.min(anchor.left, window.innerWidth - width - margin));
        let top = anchor.bottom + gap;
        if (top + height > window.innerHeight - margin && anchor.top - gap - height >= margin) {
            top = anchor.top - gap - height;
        }
        body.style.left = left + "px";
        body.style.top = top + "px";
    }

    function show(tip) {
        if (openTip && openTip !== tip) hide();
        openTip = tip;
        bodyOf(tip).classList.add("show");
        place(tip);
    }

    function hide() {
        if (!openTip) return;
        const body = bodyOf(openTip);
        if (body) body.classList.remove("show");
        openTip = null;
    }

    function tipAt(target) {
        return target instanceof Element ? target.closest(".tip") : null;
    }

    document.addEventListener("mouseover", event => {
        const tip = tipAt(event.target);
        if (tip) show(tip);
    });

    document.addEventListener("mouseout", event => {
        if (!openTip || openTip === pinnedTip) return;
        if (tipAt(event.relatedTarget) === openTip) return;
        if (openTip.contains(document.activeElement)) return;
        hide();
    });

    document.addEventListener("focusin", event => {
        const tip = tipAt(event.target);
        if (tip) show(tip);
    });

    document.addEventListener("focusout", event => {
        if (!openTip || openTip === pinnedTip) return;
        if (tipAt(event.relatedTarget) === openTip) return;
        hide();
    });

    document.addEventListener("click", event => {
        const trigger = event.target instanceof Element ? event.target.closest(".tip-trigger") : null;
        if (trigger) {
            const tip = trigger.closest(".tip");
            if (pinnedTip === tip) {
                pinnedTip = null;
                hide();
            } else {
                pinnedTip = tip;
                show(tip);
            }
            return;
        }
        if (openTip && !openTip.contains(event.target)) {
            pinnedTip = null;
            hide();
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && openTip) {
            pinnedTip = null;
            hide();
        }
    });

    window.addEventListener("resize", () => { if (openTip) place(openTip); });
    window.addEventListener("scroll", () => { if (openTip) place(openTip); }, true);

    async function copy(text) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch {
            // Fall through to the legacy path.
        }
        try {
            const area = document.createElement("textarea");
            area.value = text;
            area.setAttribute("readonly", "");
            area.style.position = "fixed";
            area.style.opacity = "0";
            document.body.appendChild(area);
            area.select();
            const copied = document.execCommand("copy");
            area.remove();
            return copied;
        } catch {
            return false;
        }
    }

    // Brings the result into view when a comparison leaves it below the fold.
    function revealResults() {
        const results = document.getElementById("results");
        if (!results) return;
        const top = results.getBoundingClientRect().top;
        if (top > window.innerHeight - 160) results.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    window.jj = {
        init: () => ({ theme: themeGet(), mac: /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent) }),
        setTheme: themeSet,
        copy,
        revealResults
    };
})();
