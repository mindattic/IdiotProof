// ============================================================================
// Audio Alert System
// ============================================================================
// Plays sounds when alerts fire, trades execute, etc.
// Uses Web Audio API for reliable playback without user interaction issues.
// ============================================================================

const AudioAlerts = (function() {
    let audioContext = null;
    let isInitialized = false;
    let isEnabled = true;
    let volume = 0.5;
    
    // Sound definitions (frequencies and durations for synthesized sounds)
    const SOUNDS = {
        // Alert: Attention-grabbing two-tone
        alert: {
            frequencies: [880, 1100, 880, 1100],
            durations: [100, 100, 100, 200],
            type: 'sine'
        },
        // Critical: Urgent, fast beeps
        critical: {
            frequencies: [1200, 1400, 1200, 1400, 1200],
            durations: [80, 80, 80, 80, 150],
            type: 'square'
        },
        // Success: Pleasant rising tone
        success: {
            frequencies: [523, 659, 784],
            durations: [150, 150, 300],
            type: 'sine'
        },
        // Error: Low warning tone
        error: {
            frequencies: [200, 150],
            durations: [200, 400],
            type: 'sawtooth'
        },
        // Trade executed: Quick confirmation
        executed: {
            frequencies: [660, 880],
            durations: [100, 200],
            type: 'sine'
        },
        // Notification: Soft single tone
        notification: {
            frequencies: [800],
            durations: [150],
            type: 'sine'
        }
    };
    
    /**
     * Initialize the audio context (must be called after user interaction).
     */
    function init() {
        if (isInitialized) return true;
        
        try {
            audioContext = new (window.AudioContext || window.webkitAudioContext)();
            isInitialized = true;
            console.log('Audio alerts initialized');
            return true;
        } catch (e) {
            console.error('Failed to initialize audio:', e);
            return false;
        }
    }
    
    /**
     * Resume audio context if suspended (browser autoplay policy).
     */
    async function resume() {
        if (!audioContext) init();
        if (audioContext && audioContext.state === 'suspended') {
            await audioContext.resume();
        }
    }
    
    /**
     * Play a synthesized sound.
     */
    function playSound(soundName) {
        if (!isEnabled) return;
        if (!audioContext) init();
        if (!audioContext) return;
        
        const sound = SOUNDS[soundName];
        if (!sound) {
            console.warn('Unknown sound:', soundName);
            return;
        }
        
        resume();
        
        let time = audioContext.currentTime;
        
        sound.frequencies.forEach((freq, i) => {
            const duration = sound.durations[i] / 1000;
            
            const oscillator = audioContext.createOscillator();
            const gainNode = audioContext.createGain();
            
            oscillator.type = sound.type;
            oscillator.frequency.setValueAtTime(freq, time);
            
            gainNode.gain.setValueAtTime(volume, time);
            gainNode.gain.exponentialRampToValueAtTime(0.01, time + duration);
            
            oscillator.connect(gainNode);
            gainNode.connect(audioContext.destination);
            
            oscillator.start(time);
            oscillator.stop(time + duration);
            
            time += duration;
        });
    }
    
    /**
     * Play alert sound based on severity.
     */
    function playAlert(severity) {
        const soundMap = {
            'critical': 'critical',
            'high': 'alert',
            'medium': 'notification',
            'low': 'notification'
        };
        
        playSound(soundMap[severity?.toLowerCase()] || 'notification');
    }
    
    /**
     * Enable/disable audio alerts.
     */
    function setEnabled(enabled) {
        isEnabled = enabled;
        console.log('Audio alerts:', enabled ? 'enabled' : 'disabled');
    }
    
    /**
     * Set volume (0.0 to 1.0).
     */
    function setVolume(vol) {
        volume = Math.max(0, Math.min(1, vol));
    }
    
    /**
     * Test all sounds.
     */
    async function testSounds() {
        await resume();
        
        const soundNames = Object.keys(SOUNDS);
        for (let i = 0; i < soundNames.length; i++) {
            console.log('Testing sound:', soundNames[i]);
            playSound(soundNames[i]);
            await new Promise(resolve => setTimeout(resolve, 1000));
        }
    }
    
    // Public API
    return {
        init,
        resume,
        playSound,
        playAlert,
        setEnabled,
        setVolume,
        testSounds,
        
        // Convenience methods
        alert: () => playSound('alert'),
        critical: () => playSound('critical'),
        success: () => playSound('success'),
        error: () => playSound('error'),
        executed: () => playSound('executed'),
        notification: () => playSound('notification')
    };
})();

// Auto-initialize on first user interaction
document.addEventListener('click', () => AudioAlerts.init(), { once: true });
document.addEventListener('keydown', () => AudioAlerts.init(), { once: true });

// Expose to window for Blazor interop
window.AudioAlerts = AudioAlerts;

// IdiotProof global namespace for Blazor interop
window.IdiotProof = window.IdiotProof || {};

// Copy text to the global chatbox (if it exists)
window.IdiotProof.copyToChat = function(text) {
    // Find the chatbox input and set its value
    const chatInput = document.querySelector('.chatbox-input input, #global-chat-input');
    if (chatInput) {
        chatInput.value = text;
        chatInput.dispatchEvent(new Event('input', { bubbles: true }));
        chatInput.focus();
        return true;
    }

    // Fallback: copy to clipboard
    if (navigator.clipboard) {
        navigator.clipboard.writeText(text);
        console.log('Copied to clipboard:', text);
    }
    return false;
};

console.log('Audio alert system loaded');
