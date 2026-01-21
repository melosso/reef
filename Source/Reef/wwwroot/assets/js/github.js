/**
 * Sets a cookie with an optional expiration in days,
 * but only if it does not already exist.
 */
function setCookieIfNotExists(name, value, days) {
    if (!getCookie(name)) {
        const date = new Date();
        date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
        document.cookie = name + "=" + (value || "") + "; expires=" + date.toUTCString() + "; path=/";
    }
}

/**
 * Gets a cookie by name.
 */
function getCookie(name) {
    const nameEQ = name + "=";
    const ca = document.cookie.split(';');
    for (let c of ca) {
        c = c.trim();
        if (c.indexOf(nameEQ) === 0) return c.substring(nameEQ.length);
    }
    return null;
}

/**
 * Compares two semantic versions in the format "X.Y.Z".
 */
function compareVersions(v1, v2) {
    const normalize = (v) => v.replace(/^v/, '').split('.').map(Number);
    const parts1 = normalize(v1);
    const parts2 = normalize(v2);

    for (let i = 0; i < 3; i++) {
        const n1 = parts1[i] || 0;
        const n2 = parts2[i] || 0;
        if (n1 > n2) return 1;
        if (n1 < n2) return -1;
    }
    return 0;
}

/**
 * Fetches the latest release and compares it to CURRENT_VERSION,
 * but only if 3 days have passed since the last check.
 * Shows the version banner unless the user has dismissed this specific version.
 */
async function checkLatestVersion() {
    if (getCookie('reef_last_version_check')) {
        console.log('Version check skipped — last check was within 3 days.');
        return;
    }

    if (typeof CURRENT_VERSION === 'undefined') {
        console.error('CURRENT_VERSION is not defined. Cannot check for updates.');
        return;
    }

    try {
        const response = await fetch('https://api.github.com/repos/melosso/reef/releases/latest');
        if (!response.ok) {
            console.warn(`Failed to fetch latest release: ${response.statusText}`);
            return;
        }

        const release = await response.json();
        const latestVersion = release.tag_name;
        const releaseUrl = release.html_url;

        // Only set cookie if not already present
        setCookieIfNotExists('reef_last_version_check', 'true', 3);

        if (compareVersions(latestVersion, CURRENT_VERSION) > 0) {
            // Check if user has dismissed this specific version
            const dismissedVersion = localStorage.getItem('reef_dismissed_version');
            if (dismissedVersion === latestVersion) {
                console.log(`Version ${latestVersion} was previously dismissed by user.`);
            } else {
                showVersionBanner(latestVersion, releaseUrl);
            }
        } else {
            console.log('Reef is up to date.');
        }
    } catch (error) {
        console.error('Error checking for GitHub release:', error);
    }
}

function showVersionBanner(latestVersion, releaseUrl) {
    const banner = document.getElementById('version-notification');
    const versionTag = document.getElementById('new-version-tag');
    const versionLink = document.getElementById('new-version-link');

    if (banner && versionTag && versionLink) {
        versionTag.textContent = latestVersion;
        versionLink.href = releaseUrl;
        banner.classList.remove('hidden');
    }
}

function dismissVersionCheck() {
    const banner = document.getElementById('version-notification');
    const versionTag = document.getElementById('new-version-tag');

    if (banner) banner.classList.add('hidden');

    // Store the dismissed version in localStorage so it doesn't show again until there's a newer version
    if (versionTag && versionTag.textContent) {
        localStorage.setItem('reef_dismissed_version', versionTag.textContent);
        console.log(`Version ${versionTag.textContent} dismissed by user.`);
    }

    // Only set cookie if not already present
    setCookieIfNotExists('reef_last_version_check', 'true', 3);
}
