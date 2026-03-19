'use strict';

// ── Tab navigation (identical pattern to admin.html) ──────────────────────────

function showTab(tab) {
    document.querySelectorAll('.tab-content').forEach(el => el.classList.add('hidden'));
    document.querySelectorAll('[id^="tab-"]').forEach(el => {
        el.classList.remove('bg-slate-100', 'text-slate-900', 'font-medium');
        el.classList.add('text-slate-600');
    });

    const tabBtn = document.getElementById('tab-' + tab);
    const contentDiv = document.getElementById('content-' + tab);

    if (tabBtn) {
        tabBtn.classList.remove('text-slate-600');
        tabBtn.classList.add('bg-slate-100', 'text-slate-900', 'font-medium');
    }
    if (contentDiv) contentDiv.classList.remove('hidden');

    // Update URL hash without scroll
    history.replaceState(null, '', '#' + tab);
}

// ── Feedback helpers ──────────────────────────────────────────────────────────

function showFeedback(containerId, message, type = 'error') {
    const el = document.getElementById(containerId);
    if (!el) return;
    const colors = type === 'success'
        ? 'bg-green-50 border-green-200 text-green-700'
        : 'bg-red-50 border-red-200 text-red-700';
    const icon = type === 'success' ? 'circle-check' : 'alert-circle';
    el.innerHTML = `<div class="${colors} border rounded-md px-3.5 py-3 flex items-center gap-2.5">
        <i data-lucide="${icon}" class="h-4 w-4 shrink-0"></i>
        <p class="text-sm font-medium">${message}</p>
    </div>`;
    queueLucideRender();
}

function clearFeedback(containerId) {
    const el = document.getElementById(containerId);
    if (el) el.innerHTML = '';
}

// ── API helper ────────────────────────────────────────────────────────────────

async function apiFetch(url, options = {}) {
    const token = localStorage.getItem('reef_token');
    return fetch(url, {
        ...options,
        headers: {
            'Content-Type': 'application/json',
            ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
            ...(options.headers || {})
        }
    });
}

// ── Load profile ──────────────────────────────────────────────────────────────

let profileData = null;

async function loadProfile() {
    try {
        const res = await apiFetch('/api/account/profile');
        if (!res.ok) throw new Error();
        profileData = await res.json();
    } catch {
        console.error('Failed to load profile');
        return;
    }

    const usernameEl    = document.getElementById('profile-username');
    const displayNameEl = document.getElementById('profile-display-name');
    const emailEl       = document.getElementById('profile-email');
    if (usernameEl)    usernameEl.value    = profileData.username    || '';
    if (displayNameEl) displayNameEl.value = profileData.displayName || '';
    if (emailEl)       emailEl.value       = profileData.email       || '';

    updateMfaUI(profileData);
}

function updateMfaUI(data) {
    const enabled = data.mfaEnabled;
    const method  = data.mfaMethod;

    document.getElementById('mfa-status-banner').classList.toggle('hidden', !enabled);
    if (enabled) {
        document.getElementById('mfa-method-label').textContent =
            method === 'totp' ? 'Authenticator app (TOTP)' : 'Email one-time code';
    }

    const totpActive  = enabled && method === 'totp';
    const emailActive = enabled && method === 'email';

    document.getElementById('totp-badge').classList.toggle('hidden', !totpActive);
    document.getElementById('totp-status-text').textContent =
        totpActive ? 'Active' : emailActive ? 'Email one-time code is already active' : 'Not configured';
    const totpSetupBtn = document.getElementById('totp-setup-btn');
    totpSetupBtn.textContent = totpActive ? 'Reconfigure' : 'Set up';
    totpSetupBtn.disabled = emailActive;
    totpSetupBtn.onclick = totpActive ? startTotpReconfigure : startTotpSetup;

    document.getElementById('email-mfa-badge').classList.toggle('hidden', !emailActive);

    const emailBtn = document.getElementById('email-mfa-btn');
    const emailStatusText = document.getElementById('email-mfa-status-text');

    if (emailActive) {
        emailStatusText.textContent = `Active! Codes sent to ${data.email || 'your email'}`;
        emailBtn.textContent = 'Reconfigure';
        emailBtn.disabled = false;
    } else if (totpActive) {
        emailStatusText.textContent = 'Authenticator app is already active';
        emailBtn.textContent = 'Enable';
        emailBtn.disabled = true;
    } else if (!data.emailMfaAvailable) {
        emailStatusText.textContent = 'Requires system notifications to be configured in Settings';
        emailBtn.textContent = 'Enable';
        emailBtn.disabled = true;
    } else if (!data.email) {
        emailStatusText.textContent = 'Set an email address in Profile first';
        emailBtn.textContent = 'Enable';
        emailBtn.disabled = true;
    } else {
        emailStatusText.textContent = `Will send codes to ${data.email}`;
        emailBtn.textContent = 'Enable';
        emailBtn.disabled = false;
    }

    // Grey out the card for the inactive method when one is already active
    const totpCard = document.getElementById('totp-card');
    const emailCard = document.getElementById('email-mfa-card');
    if (totpCard) {
        totpCard.classList.toggle('opacity-40', emailActive);
        totpCard.classList.toggle('pointer-events-none', emailActive);
    }
    if (emailCard) {
        emailCard.classList.toggle('opacity-40', totpActive);
        emailCard.classList.toggle('pointer-events-none', totpActive);
    }
}

// ── Profile save ──────────────────────────────────────────────────────────────

async function saveProfile() {
    clearFeedback('profile-feedback');
    const displayName = document.getElementById('profile-display-name').value.trim();
    const email       = document.getElementById('profile-email').value.trim();

    try {
        const res  = await apiFetch('/api/account/profile', {
            method: 'PUT',
            body: JSON.stringify({ displayName, email })
        });
        const data = await res.json();
        if (res.ok && data.success) {
            showFeedback('profile-feedback', 'Profile saved.', 'success');
            profileData = { ...profileData, displayName, email };
            updateMfaUI(profileData);
            const el = document.getElementById('username-display');
            if (el && displayName) el.textContent = displayName;
        } else {
            showFeedback('profile-feedback', data.message || 'Failed to save profile.');
        }
    } catch {
        showFeedback('profile-feedback', 'Connection error. Please try again.');
    }
}

// ── Change password ───────────────────────────────────────────────────────────

async function changePassword() {
    clearFeedback('password-feedback');
    const current  = document.getElementById('current-password').value;
    const next     = document.getElementById('new-password').value;
    const confirm  = document.getElementById('confirm-password').value;

    if (!current || !next || !confirm) {
        showFeedback('password-feedback', 'All fields are required.');
        return;
    }
    if (next !== confirm) {
        showFeedback('password-feedback', 'New passwords do not match.');
        return;
    }
    if (next.length < 6) {
        showFeedback('password-feedback', 'New password must be at least 6 characters.');
        return;
    }

    try {
        const res  = await apiFetch('/api/auth/change-password', {
            method: 'POST',
            body: JSON.stringify({ currentPassword: current, newPassword: next })
        });
        const data = await res.json();
        if (res.ok && data.success) {
            showFeedback('password-feedback', 'Password changed. Signing you out…', 'success');
            ['current-password', 'new-password', 'confirm-password']
                .forEach(id => { document.getElementById(id).value = ''; });
            setTimeout(() => { window.location.href = '/logoff'; }, 1500);
        } else {
            showFeedback('password-feedback', data.message || 'Failed to change password.');
        }
    } catch {
        showFeedback('password-feedback', 'Connection error. Please try again.');
    }
}

// ── TOTP setup ────────────────────────────────────────────────────────────────

async function startTotpSetup() {
    const flow = document.getElementById('totp-setup-flow');
    flow.classList.remove('hidden');
    document.getElementById('totp-setup-btn').classList.add('hidden');
    clearFeedback('totp-verify-feedback');
    document.getElementById('totp-verify-code').value = '';
    document.getElementById('totp-qr-container').innerHTML =
        '<i data-lucide="loader-2" class="h-6 w-6 text-slate-400 animate-spin"></i>';
    document.getElementById('totp-manual-key').textContent = '';
    queueLucideRender();

    try {
        const res = await apiFetch('/api/account/mfa/totp/setup', {
            method: 'POST',
            body: JSON.stringify({})
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            document.getElementById('totp-qr-container').innerHTML =
                `<p class="text-xs text-red-500 p-2">${err.message || 'Failed to start setup'}</p>`;
            return;
        }
        const data = await res.json();
        document.getElementById('totp-qr-container').innerHTML =
            `<img src="data:image/png;base64,${data.qrCodePng}" alt="TOTP QR code" class="w-40 h-40 rounded">`;
        const formatted = data.secretBase32.match(/.{1,4}/g)?.join(' ') ?? data.secretBase32;
        document.getElementById('totp-manual-key').textContent = formatted;
    } catch {
        document.getElementById('totp-qr-container').innerHTML =
            '<p class="text-xs text-red-500 p-2">Failed to load QR code</p>';
    }
}

function cancelTotpSetup() {
    document.getElementById('totp-setup-flow').classList.add('hidden');
    document.getElementById('totp-setup-btn').classList.remove('hidden');
    clearFeedback('totp-verify-feedback');
    // Also hide the reconfigure confirm step if visible
    const reconfirmEl = document.getElementById('totp-reconfirm-step');
    if (reconfirmEl) reconfirmEl.classList.add('hidden');
}

// Shows the current-code verification step before allowing TOTP reconfiguration
function startTotpReconfigure() {
    const reconfirmEl = document.getElementById('totp-reconfirm-step');
    if (!reconfirmEl) {
        // Fallback: no reconfirm UI, just call setup directly (shouldn't happen)
        startTotpSetup(null);
        return;
    }
    clearFeedback('totp-reconfirm-feedback');
    document.getElementById('totp-reconfirm-code').value = '';
    reconfirmEl.classList.remove('hidden');
    document.getElementById('totp-setup-btn').classList.add('hidden');
    document.getElementById('totp-reconfirm-code').focus();
}

async function submitTotpReconfirm() {
    clearFeedback('totp-reconfirm-feedback');
    const code = document.getElementById('totp-reconfirm-code').value.replace(/\s/g, '');
    if (code.length !== 6) {
        showFeedback('totp-reconfirm-feedback', 'Enter the 6-digit code from your current app.');
        return;
    }

    // Try the setup endpoint; if the current code is wrong it returns 400
    try {
        const res = await apiFetch('/api/account/mfa/totp/setup', {
            method: 'POST',
            body: JSON.stringify({ currentCode: code })
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            showFeedback('totp-reconfirm-feedback', err.message || 'Invalid code. Please try again.');
            return;
        }
        const data = await res.json();
        // Current code validated — switch from reconfirm step to the setup flow
        document.getElementById('totp-reconfirm-step').classList.add('hidden');
        const flow = document.getElementById('totp-setup-flow');
        flow.classList.remove('hidden');
        document.getElementById('totp-setup-btn').classList.add('hidden');
        clearFeedback('totp-verify-feedback');
        document.getElementById('totp-verify-code').value = '';
        document.getElementById('totp-qr-container').innerHTML =
            `<img src="data:image/png;base64,${data.qrCodePng}" alt="TOTP QR code" class="w-40 h-40 rounded">`;
        const formatted = data.secretBase32.match(/.{1,4}/g)?.join(' ') ?? data.secretBase32;
        document.getElementById('totp-manual-key').textContent = formatted;
        queueLucideRender();
    } catch {
        showFeedback('totp-reconfirm-feedback', 'Connection error. Please try again.');
    }
}

function cancelTotpReconfirm() {
    const reconfirmEl = document.getElementById('totp-reconfirm-step');
    if (reconfirmEl) reconfirmEl.classList.add('hidden');
    document.getElementById('totp-setup-btn').classList.remove('hidden');
}

async function confirmTotp() {
    clearFeedback('totp-verify-feedback');
    const code = document.getElementById('totp-verify-code').value.replace(/\s/g, '');
    if (code.length !== 6) {
        showFeedback('totp-verify-feedback', 'Enter the 6-digit code from your app.');
        return;
    }
    try {
        const res  = await apiFetch('/api/account/mfa/totp/confirm', {
            method: 'POST',
            body: JSON.stringify({ code })
        });
        const data = await res.json();
        if (res.ok && data.success) {
            cancelTotpSetup();
            profileData.mfaEnabled = true;
            profileData.mfaMethod  = 'totp';
            updateMfaUI(profileData);
            if (data.backupCodes && data.backupCodes.length > 0) {
                downloadBackupCodes(data.backupCodes, profileData.username || 'user');
            }
        } else {
            showFeedback('totp-verify-feedback', data.message || 'Invalid code.');
        }
    } catch {
        showFeedback('totp-verify-feedback', 'Connection error. Please try again.');
    }
}

function downloadBackupCodes(codes, username) {
    const date = new Date().toISOString().split('T')[0];
    const lines = [
        `# Reef – Two-factor authentication backup codes`,
        `# Account: ${username}`,
        `# Generated: ${date}`,
        `# Keep this file somewhere safe. Each code can only be used once.`,
        ``,
        `backup_codes:`,
        ...codes.map(c => `  - ${c}`)
    ];
    const blob = new Blob([lines.join('\n')], { type: 'text/yaml' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = `reef-backup-codes-${username}.yaml`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

// ── Email MFA ─────────────────────────────────────────────────────────────────

async function enableEmailMfa() {
    clearFeedback('email-mfa-feedback');
    try {
        const res  = await apiFetch('/api/account/mfa/email/enable', { method: 'POST' });
        const data = await res.json();
        if (res.ok && data.success) {
            profileData.mfaEnabled = true;
            profileData.mfaMethod  = 'email';
            updateMfaUI(profileData);
        } else {
            showFeedback('email-mfa-feedback', data.message || 'Failed to enable email MFA.');
        }
    } catch {
        showFeedback('email-mfa-feedback', 'Connection error. Please try again.');
    }
}

// ── Disable MFA ───────────────────────────────────────────────────────────────

async function disableMfa() {
    const confirmed = await window.showConfirmModal({
        title: 'Disable two-factor authentication?',
        message: 'Your account will be less secure without 2FA. You can re-enable it at any time.',
        confirmText: 'Disable',
        danger: true
    });
    if (!confirmed) return;
    try {
        const res  = await apiFetch('/api/account/mfa', { method: 'DELETE' });
        const data = await res.json();
        if (res.ok && data.success) {
            profileData.mfaEnabled = false;
            profileData.mfaMethod  = null;
            updateMfaUI(profileData);
        }
    } catch { /* silent */ }
}

// ── Init ──────────────────────────────────────────────────────────────────────

async function init() {
    // Pre-populate username immediately from localStorage so the field isn't
    // blank while the async API call is in flight.
    const cachedUsername = localStorage.getItem('reef_username');
    if (cachedUsername) {
        const el = document.getElementById('profile-username');
        if (el) el.value = cachedUsername;
    }

    await loadProfile();

    const hash = window.location.hash.replace('#', '');
    showTab(['profile', 'appearance', 'password', 'mfa'].includes(hash) ? hash : 'profile');
}

// init() is called by the inline script in account.html, which runs after the
// content swap in SPA navigation and after DOM ready on direct page loads.
