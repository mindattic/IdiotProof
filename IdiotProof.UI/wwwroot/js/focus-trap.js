// Minimal modal focus trap, shared by every Blazor modal in the app (both hosts,
// served from this RCL's static assets). Keeps Tab/Shift+Tab cycling inside the
// dialog while it's open and restores focus to the triggering element on close.
(function () {
    const FOCUSABLE_SELECTOR =
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';

    let activeDialog = null;
    let previousFocus = null;
    let keydownHandler = null;

    function getFocusable(dialogEl) {
        return Array.from(dialogEl.querySelectorAll(FOCUSABLE_SELECTOR))
            .filter(el => !el.disabled && el.offsetParent !== null);
    }

    function activate(dialogEl) {
        if (!dialogEl) return;
        previousFocus = document.activeElement;
        activeDialog = dialogEl;

        const focusable = getFocusable(dialogEl);
        (focusable[0] || dialogEl).focus();

        keydownHandler = function (e) {
            if (e.key !== "Tab") return;
            const items = getFocusable(dialogEl);
            if (items.length === 0) return;
            const first = items[0];
            const last = items[items.length - 1];
            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        };
        dialogEl.addEventListener("keydown", keydownHandler);
    }

    function deactivate() {
        if (activeDialog && keydownHandler) {
            activeDialog.removeEventListener("keydown", keydownHandler);
        }
        activeDialog = null;
        keydownHandler = null;
        if (previousFocus && typeof previousFocus.focus === "function") {
            previousFocus.focus();
        }
        previousFocus = null;
    }

    window.focusTrap = { activate, deactivate };
})();
