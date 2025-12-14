// Admin Email Templates Management
// Handles loading, editing, and saving email templates

let currentTemplate = null;
let currentTemplateType = null;

/**
 * Load a template when selected
 */
async function loadTemplate() {
    const templateType = document.getElementById('template-type').value;

    if (!templateType) {
        document.getElementById('template-editor').classList.add('hidden');
        document.getElementById('template-empty').classList.remove('hidden');
        currentTemplate = null;
        currentTemplateType = null;
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/api/admin/notification-templates/${templateType}`, {
            headers: getAuthHeaders()
        });

        if (!response.ok) {
            console.error('API error:', response.status, response.statusText);
            showToast(`Error loading template: ${response.status} ${response.statusText}`, 'danger');
            return;
        }

        let template;
        try {
            template = await response.json();
        } catch (parseError) {
            console.error('JSON parse error:', parseError);
            showToast('Error parsing template data', 'danger');
            return;
        }

        if (!template || typeof template !== 'object') {
            console.error('Invalid template object:', template);
            showToast('Invalid template data received', 'danger');
            return;
        }

        currentTemplate = template;
        currentTemplateType = templateType;

        // Populate form fields
        document.getElementById('template-subject').value = currentTemplate.subject || '';
        document.getElementById('template-body').value = currentTemplate.htmlBody || '';
        document.getElementById('template-cta-button-text').value = currentTemplate.ctaButtonText || '';
        document.getElementById('template-cta-url-override').value = currentTemplate.ctaUrlOverride || '';

        // Show editor, hide empty state
        document.getElementById('template-editor').classList.remove('hidden');
        document.getElementById('template-empty').classList.add('hidden');

    } catch (error) {
        console.error('Unexpected error loading template:', error);
        showToast('Unexpected error loading template: ' + error.message, 'danger');
    }
}

/**
 * Show template preview in a new tab
 */
function showTemplatePreview() {
    const subject = document.getElementById('template-subject').value;
    const htmlBody = document.getElementById('template-body').value;

    if (!htmlBody) {
        showToast('No HTML body to preview', 'warning');
        return;
    }

    // Create a complete HTML document for preview
    const previewHtml = `<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Email Template Preview - ${subject || 'Unnamed Template'}</title>
</head>
<body>
    <div style="padding: 20px; background: #f5f5f5; min-height: 100vh;">
        <div style="max-width: 600px; margin: 0 auto;">
            <h2 style="margin-top: 0; color: #333; font-size: 14px; text-transform: uppercase; letter-spacing: 0.5px;">Email Preview</h2>
            <p style="color: #666; font-size: 12px; margin-bottom: 20px;"><strong>Subject:</strong> ${subject}</p>
            ${htmlBody}
        </div>
    </div>
</body>
</html>`;

    // Open preview in new tab
    const previewTab = window.open('', '_blank');
    previewTab.document.write(previewHtml);
    previewTab.document.close();

    showToast('Preview opened in new tab', 'success');
}

/**
 * Save the current template
 */
async function saveTemplate() {
    if (!currentTemplateType) {
        showToast('Please select a template first', 'warning');
        return;
    }

    const subject = document.getElementById('template-subject').value.trim();
    const htmlBody = document.getElementById('template-body').value.trim();
    const ctaButtonText = document.getElementById('template-cta-button-text').value.trim();
    const ctaUrlOverride = document.getElementById('template-cta-url-override').value.trim();

    if (!subject) {
        showToast('Subject is required', 'warning');
        return;
    }

    if (!htmlBody) {
        showToast('HTML Body is required', 'warning');
        return;
    }

    const templateData = {
        templateType: currentTemplateType,
        subject: subject,
        htmlBody: htmlBody,
        ctaButtonText: ctaButtonText || null,
        ctaUrlOverride: ctaUrlOverride || null,
        isDefault: currentTemplate?.isDefault || false
    };

    try {
        const response = await fetch(`${API_BASE}/api/admin/notification-templates/${currentTemplateType}`, {
            method: 'PUT',
            headers: getAuthHeaders(),
            body: JSON.stringify(templateData)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save template');
        }

        showToast('Template saved successfully', 'success');

        // Update current template
        currentTemplate.subject = subject;
        currentTemplate.htmlBody = htmlBody;
        currentTemplate.updatedAt = new Date().toISOString();
    } catch (error) {
        console.error('Error saving template:', error);
        showToast('Error: ' + error.message, 'danger');
    }
}

/**
 * Reset a template to its default version
 */
async function resetTemplateToDefault() {
    if (!currentTemplateType) {
        showToast('Please select a template first', 'warning');
        return;
    }

    if (!confirm(`Are you sure you want to reset "${currentTemplateType}" to its default template? This action cannot be undone.`)) {
        return;
    }

    try {
        // Call reset endpoint
        const resetResponse = await fetch(`${API_BASE}/api/admin/notification-templates/${currentTemplateType}/reset`, {
            method: 'POST',
            headers: getAuthHeaders()
        });

        if (!resetResponse.ok) {
            const error = await resetResponse.json();
            throw new Error(error.error || 'Failed to reset template');
        }

        // After reset succeeds, fetch the updated template
        const getResponse = await fetch(`${API_BASE}/api/admin/notification-templates/${currentTemplateType}`, {
            headers: getAuthHeaders()
        });

        if (!getResponse.ok) {
            throw new Error('Failed to load reset template');
        }

        const template = await getResponse.json();

        // Update current template and form
        currentTemplate = template;
        document.getElementById('template-subject').value = template.subject || '';
        document.getElementById('template-body').value = template.htmlBody || '';
        document.getElementById('template-cta-button-text').value = template.ctaButtonText || '';
        document.getElementById('template-cta-url-override').value = template.ctaUrlOverride || '';

        showToast('Template reset to default successfully', 'success');
    } catch (error) {
        console.error('Error resetting template:', error);
        showToast('Error resetting template: ' + error.message, 'danger');
    }
}

/**
 * Initialize email templates when tab is shown
 * Hook into the admin tab system
 */
document.addEventListener('DOMContentLoaded', () => {
    // Override the showTab function to load templates when needed
    const originalShowTab = window.showTab;
    window.showTab = function(tabName) {
        originalShowTab(tabName);
        if (tabName === 'email-templates') {
            // Tab is now visible - user can select and edit templates
            console.log('Email Templates tab opened');
        }
    };
});
