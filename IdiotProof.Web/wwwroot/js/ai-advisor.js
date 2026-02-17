// ============================================================================
// AI Advisor Screenshot Capture
// Uses html2canvas to capture the main content area
// Also extracts data-analysis and data-meta attributes for AI context
// ============================================================================

// Load html2canvas dynamically
(function () {
    if (!window.html2canvas) {
        const script = document.createElement('script');
        script.src = 'https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js';
        script.async = true;
        document.head.appendChild(script);
    }
})();

/**
 * Extracts all data-analysis and data-meta attributes from the page
 * @returns {Object} Object containing analysis context
 */
window.extractAnalysisContext = function () {
    const context = {
        pageTitle: document.title,
        analysisData: [],
        metaData: [],
        visibleText: [],
        statistics: {}
    };

    // Extract data-analysis attributes
    document.querySelectorAll('[data-analysis]').forEach(el => {
        const analysis = el.getAttribute('data-analysis');
        if (analysis && analysis.trim()) {
            context.analysisData.push({
                text: analysis,
                element: el.tagName.toLowerCase(),
                classes: el.className
            });
        }
    });

    // Extract data-meta attributes
    document.querySelectorAll('[data-meta]').forEach(el => {
        const meta = el.getAttribute('data-meta');
        if (meta && meta.trim()) {
            context.metaData.push({
                text: meta,
                element: el.tagName.toLowerCase(),
                label: el.querySelector('.stat-label, .perf-label, label')?.textContent || ''
            });
        }
    });

    // Extract visible statistics (numbers with labels)
    document.querySelectorAll('.stat-value, .perf-value, .trade-pnl').forEach(el => {
        const label = el.closest('.stat-item, .perf-card, .trade-item')?.querySelector('.stat-label, .perf-label')?.textContent;
        const value = el.textContent?.trim();
        if (label && value) {
            context.statistics[label] = value;
        }
    });

    // Get key visible text elements
    document.querySelectorAll('h1, h2, h3, .subtitle, .strategy-description p').forEach(el => {
        const text = el.textContent?.trim();
        if (text && text.length < 200) {
            context.visibleText.push(text);
        }
    });

    return context;
};

/**
 * Captures a screenshot of the main content area
 * @returns {Promise<string>} Base64 encoded PNG image data URL
 */
window.captureScreenshot = async function () {
    try {
        // Wait for html2canvas to load if not already
        if (!window.html2canvas) {
            await new Promise((resolve, reject) => {
                const checkInterval = setInterval(() => {
                    if (window.html2canvas) {
                        clearInterval(checkInterval);
                        resolve();
                    }
                }, 100);

                // Timeout after 5 seconds
                setTimeout(() => {
                    clearInterval(checkInterval);
                    reject(new Error('html2canvas failed to load'));
                }, 5000);
            });
        }

        // Find the main content area (exclude sidebar and AI advisor)
        const mainContent = document.querySelector('.main-container') || 
                           document.querySelector('main') || 
                           document.querySelector('.page-content') ||
                           document.querySelector('.app-layout > :not(.sidebar)') ||
                           document.body;

        // Configure html2canvas options - HIGH QUALITY for accurate financial analysis
        const options = {
            backgroundColor: '#0d1117',
            scale: 2,              // 2x resolution for sharp text/numbers
            logging: false,
            useCORS: true,
            allowTaint: true,
            imageTimeout: 0,       // No timeout for image loading
            ignoreElements: (element) => {
                // Ignore the AI advisor panel itself
                return element.classList?.contains('ai-advisor-container') ||
                       element.classList?.contains('global-chatbox');
            }
        };

        // Capture the screenshot
        const canvas = await html2canvas(mainContent, options);

        // Convert to base64 PNG - MAXIMUM QUALITY for readable numbers
        const dataUrl = canvas.toDataURL('image/png', 1.0);

        return dataUrl;
    } catch (error) {
        console.error('Screenshot capture failed:', error);
        return null;
    }
};

/**
 * Scrolls a container to the bottom
 * @param {HTMLElement} element - The container element
 */
window.scrollToBottom = function (element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};

/**
 * Gets a text summary of the page for AI context
 * @returns {string} Formatted summary
 */
window.getPageSummary = function () {
    const context = window.extractAnalysisContext();
    let summary = `Page: ${context.pageTitle}\n\n`;

    if (context.visibleText.length > 0) {
        summary += `HEADERS:\n${context.visibleText.join('\n')}\n\n`;
    }

    if (Object.keys(context.statistics).length > 0) {
        summary += `STATISTICS:\n`;
        for (const [key, value] of Object.entries(context.statistics)) {
            summary += `- ${key}: ${value}\n`;
        }
        summary += '\n';
    }

    if (context.analysisData.length > 0) {
        summary += `ANALYSIS CONTEXT:\n`;
        context.analysisData.forEach(item => {
            summary += `- ${item.text}\n`;
        });
    }

    return summary;
};

/**
 * Captures specific element by selector
 * @param {string} selector - CSS selector for the element
 * @returns {Promise<string>} Base64 encoded PNG image data URL
 */
window.captureElement = async function (selector) {
    try {
        if (!window.html2canvas) {
            console.error('html2canvas not loaded');
            return null;
        }

        const element = document.querySelector(selector);
        if (!element) {
            console.error('Element not found:', selector);
            return null;
        }

        const canvas = await html2canvas(element, {
            backgroundColor: '#0d1117',
            scale: 1,
            logging: false
        });

        return canvas.toDataURL('image/png', 0.9);
    } catch (error) {
        console.error('Element capture failed:', error);
        return null;
    }
};

// ============================================================================
// AI Help Icon Functions - Clickable robot icons throughout the UI
// ============================================================================

/**
 * Extracts AI context from a specific element or its ancestors
 * @param {string} targetId - Optional ID of the target element
 * @param {string} parentSelector - Optional CSS selector for parent element
 * @returns {string} Extracted context for AI
 */
window.extractAiContext = function (targetId, parentSelector) {
    let element = null;

    if (targetId) {
        element = document.getElementById(targetId);
    } else if (parentSelector) {
        // Find the closest matching parent from the event source
        element = document.querySelector(parentSelector);
    }

    // If no specific element, find the closest parent with data-analysis or data-meta
    if (!element) {
        // Get the most recently clicked AI help icon
        const activeIcon = document.querySelector('.ai-help-icon:focus, .ai-help-icon:active');
        if (activeIcon) {
            element = activeIcon.closest('[data-analysis], [data-meta]');
        }
    }

    if (!element) {
        return '';
    }

    // Build context from the element and its children
    const parts = [];

    // Get data-analysis from this element
    const analysis = element.getAttribute('data-analysis');
    if (analysis) {
        parts.push(`CONTEXT: ${analysis}`);
    }

    // Get data-meta from this element
    const meta = element.getAttribute('data-meta');
    if (meta) {
        parts.push(`INFO: ${meta}`);
    }

    // Get visible text content
    const text = element.innerText?.trim();
    if (text && text.length < 500) {
        parts.push(`VISIBLE DATA:\n${text}`);
    }

    // Get nested data-meta from children
    const childMetas = element.querySelectorAll('[data-meta]');
    if (childMetas.length > 0) {
        parts.push('DETAILS:');
        childMetas.forEach(child => {
            const childMeta = child.getAttribute('data-meta');
            const label = child.querySelector('.stat-label, .perf-label, label')?.textContent || '';
            const value = child.querySelector('.stat-value, .perf-value')?.textContent || child.textContent?.trim().substring(0, 100);
            if (childMeta) {
                parts.push(`- ${label ? label + ': ' : ''}${childMeta}`);
            }
        });
    }

    return parts.join('\n');
};

/**
 * Sends context to the AI Advisor panel and opens it
 * @param {string} context - The context to send
 */
window.sendToAiAdvisor = function (context) {
    if (!context) {
        console.warn('No context to send to AI Advisor');
        return;
    }

    // Dispatch custom event that the AiAdvisorPanel can listen to
    const event = new CustomEvent('ai-advisor-request', {
        detail: {
            context: context,
            timestamp: new Date().toISOString()
        },
        bubbles: true
    });
    document.dispatchEvent(event);

    // Also try to expand the AI Advisor panel if it's collapsed
    const advisorPanel = document.querySelector('.ai-advisor-container');
    if (advisorPanel && advisorPanel.classList.contains('collapsed')) {
        // Click the header to expand
        const header = advisorPanel.querySelector('.ai-advisor-header');
        if (header) {
            header.click();
        }
    }

    // Focus the input if expanded
    setTimeout(() => {
        const input = document.querySelector('.ai-advisor-container input[type="text"]');
        if (input) {
            // Pre-fill with a question about the context
            const question = `Tell me about this: ${context.split('\n')[0].substring(0, 100)}...`;
            input.value = question;
            input.focus();
            // Trigger input event so Blazor picks up the change
            input.dispatchEvent(new Event('input', { bubbles: true }));
        }
    }, 300);
};

/**
 * Gets context from the clicked element's parent hierarchy
 * @param {Event} event - The click event
 * @returns {string} Extracted context
 */
window.getContextFromClick = function (event) {
    let element = event.target;

    // Walk up the DOM to find elements with data attributes
    while (element && element !== document.body) {
        const analysis = element.getAttribute('data-analysis');
        const meta = element.getAttribute('data-meta');

        if (analysis || meta) {
            return window.extractAiContext(element.id, null) || analysis || meta;
        }

        element = element.parentElement;
    }

    return '';
};

// Store the DotNetObjectReference for callback
window._aiAdvisorDotNetRef = null;

/**
 * Registers the Blazor callback for AI Advisor
 * @param {object} dotNetRef - DotNetObjectReference to the AiAdvisorPanel
 */
window.registerAiAdvisorCallback = function (dotNetRef) {
    window._aiAdvisorDotNetRef = dotNetRef;

    // Listen for custom events from AI help icons
    document.addEventListener('ai-advisor-request', async function (e) {
        if (window._aiAdvisorDotNetRef && e.detail?.context) {
            try {
                await window._aiAdvisorDotNetRef.invokeMethodAsync('ReceiveContextFromHelpIcon', e.detail.context);
            } catch (err) {
                console.error('Failed to invoke AI Advisor callback:', err);
            }
        }
    });
};

/**
 * Updated sendToAiAdvisor that uses the registered callback
 * @param {string} context - The context to send
 */
window.sendToAiAdvisor = function (context) {
    if (!context) {
        console.warn('No context to send to AI Advisor');
        return;
    }

    // Dispatch custom event that triggers the Blazor callback
    const event = new CustomEvent('ai-advisor-request', {
        detail: {
            context: context,
            timestamp: new Date().toISOString()
        },
        bubbles: true
    });
    document.dispatchEvent(event);

    // Also try to expand the AI Advisor panel if it's collapsed
    const advisorPanel = document.querySelector('.ai-advisor-container');
    if (advisorPanel && advisorPanel.classList.contains('collapsed')) {
        const header = advisorPanel.querySelector('.ai-advisor-header');
        if (header) {
            header.click();
        }
    }
};

