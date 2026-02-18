// ============================================================================
// IdiotProof Tooltips - Using Tippy.js for beautiful, accessible tooltips
// ============================================================================
// Automatically creates tooltips from:
// - title attributes
// - data-tooltip attributes
// - data-meta attributes (with special styling for trading context)
// ============================================================================

// Load Tippy.js and Popper.js dynamically
(function() {
    // Load Popper.js first (required by Tippy)
    if (!window.Popper) {
        const popperScript = document.createElement('script');
        popperScript.src = 'https://unpkg.com/@popperjs/core@2';
        popperScript.async = false;
        document.head.appendChild(popperScript);
    }
    
    // Then load Tippy.js
    if (!window.tippy) {
        const tippyScript = document.createElement('script');
        tippyScript.src = 'https://unpkg.com/tippy.js@6';
        tippyScript.async = false;
        document.head.appendChild(tippyScript);
        
        // Load Tippy CSS
        const tippyCss = document.createElement('link');
        tippyCss.rel = 'stylesheet';
        tippyCss.href = 'https://unpkg.com/tippy.js@6/dist/tippy.css';
        document.head.appendChild(tippyCss);
        
        // Load theme
        const themeCss = document.createElement('link');
        themeCss.rel = 'stylesheet';
        themeCss.href = 'https://unpkg.com/tippy.js@6/themes/material.css';
        document.head.appendChild(themeCss);
    }
})();

/**
 * IdiotProof custom Tippy theme matching app styles
 */
const idiotProofTheme = {
    theme: 'idiotproof',
    animation: 'shift-away',
    arrow: true,
    delay: [200, 0],
    duration: [200, 150],
    hideOnClick: false,
    interactive: false,
    placement: 'top',
    maxWidth: 350
};

/**
 * Initialize tooltips on the page
 */
window.initTooltips = function() {
    // Wait for Tippy to load
    if (!window.tippy) {
        setTimeout(window.initTooltips, 100);
        return;
    }
    
    // Destroy existing tooltips first
    document.querySelectorAll('[data-tippy-root]').forEach(el => el._tippy?.destroy());
    
    // Initialize tooltips from data-tooltip attribute
    tippy('[data-tooltip]', {
        ...idiotProofTheme,
        content: (reference) => reference.getAttribute('data-tooltip')
    });
    
    // Initialize tooltips from title attribute (preserve accessibility)
    tippy('[title]:not([data-tooltip]):not([data-meta])', {
        ...idiotProofTheme,
        content: (reference) => {
            const title = reference.getAttribute('title');
            reference.removeAttribute('title'); // Prevent browser default tooltip
            reference.setAttribute('data-original-title', title);
            return title;
        }
    });
    
    // Initialize meta tooltips (trading context) - special styling
    tippy('[data-meta]', {
        ...idiotProofTheme,
        theme: 'idiotproof-meta',
        placement: 'bottom',
        maxWidth: 400,
        delay: [300, 0],
        content: (reference) => {
            const meta = reference.getAttribute('data-meta');
            return `<div class="meta-tooltip">
                <span class="meta-icon">💡</span>
                <span class="meta-text">${meta}</span>
            </div>`;
        },
        allowHTML: true
    });
    
    // Initialize analysis tooltips (detailed trading analysis)
    tippy('[data-analysis]', {
        ...idiotProofTheme,
        theme: 'idiotproof-analysis',
        placement: 'bottom-start',
        maxWidth: 500,
        interactive: true,
        delay: [400, 100],
        content: (reference) => {
            const analysis = reference.getAttribute('data-analysis');
            return `<div class="analysis-tooltip">
                <div class="analysis-header">
                    <span class="analysis-icon">📊</span>
                    <span class="analysis-title">AI Analysis</span>
                </div>
                <div class="analysis-content">${analysis}</div>
                <div class="analysis-footer">
                    <button class="analysis-ask-ai" onclick="window.askAiAboutElement(this.closest('[data-tippy-root]')?.parentElement)">
                        🤖 Ask AI
                    </button>
                </div>
            </div>`;
        },
        allowHTML: true
    });
    
    // Button tooltips - quick hints
    tippy('button:not([data-tooltip]):not([title]):not([data-meta])', {
        ...idiotProofTheme,
        delay: [500, 0],
        content: (reference) => {
            // Generate tooltip from button text/aria-label
            const label = reference.getAttribute('aria-label') || 
                         reference.textContent?.trim();
            if (label && label.length < 50) {
                return label;
            }
            return null;
        },
        onShow(instance) {
            // Don't show if content is null
            if (!instance.props.content) return false;
        }
    });
    
    console.log('IdiotProof tooltips initialized');
};

/**
 * Ask AI about a specific element
 */
window.askAiAboutElement = function(element) {
    if (!element) return;
    
    const context = element.getAttribute('data-analysis') || 
                   element.getAttribute('data-meta') ||
                   element.textContent?.substring(0, 500);
    
    if (context && window.aiAdvisorCallback) {
        window.aiAdvisorCallback.invokeMethodAsync('ReceiveContextFromHelpIcon', context);
    }
};

/**
 * Register AI Advisor callback for help icons
 */
window.registerAiAdvisorCallback = function(dotNetRef) {
    window.aiAdvisorCallback = dotNetRef;
};

/**
 * Refresh tooltips (call after dynamic content updates)
 */
window.refreshTooltips = function() {
    // Use MutationObserver to detect changes, or call manually
    setTimeout(window.initTooltips, 100);
};

// Initialize on DOM ready
document.addEventListener('DOMContentLoaded', () => {
    setTimeout(window.initTooltips, 500);
});

// Re-initialize on Blazor navigation
if (window.Blazor) {
    Blazor.addEventListener('enhancedload', () => {
        setTimeout(window.initTooltips, 300);
    });
}

// Observe DOM changes to reinitialize tooltips
const tooltipObserver = new MutationObserver((mutations) => {
    let shouldRefresh = false;
    mutations.forEach(mutation => {
        if (mutation.addedNodes.length > 0) {
            mutation.addedNodes.forEach(node => {
                if (node.nodeType === 1) { // Element node
                    if (node.hasAttribute?.('data-tooltip') || 
                        node.hasAttribute?.('data-meta') ||
                        node.hasAttribute?.('data-analysis') ||
                        node.hasAttribute?.('title')) {
                        shouldRefresh = true;
                    }
                    if (node.querySelectorAll?.('[data-tooltip], [data-meta], [data-analysis], [title]').length > 0) {
                        shouldRefresh = true;
                    }
                }
            });
        }
    });
    
    if (shouldRefresh) {
        clearTimeout(window.tooltipRefreshTimer);
        window.tooltipRefreshTimer = setTimeout(window.initTooltips, 200);
    }
});

// Start observing after a delay
setTimeout(() => {
    tooltipObserver.observe(document.body, { 
        childList: true, 
        subtree: true 
    });
}, 1000);
