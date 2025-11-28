// Documentation Page - Schema Diagram Generator

// ============ STATE ============

let currentMainTab = 'profiles';
let currentProfileView = 'overview';
let currentJobView = 'overview';

let allProfiles = [];
let allJobs = [];
let allConnections = [];
let allDestinations = [];
let allTemplates = [];

let selectedProfileId = null;
let selectedJobId = null;

// Zoom and pan state
let profileZoomState = {
    scale: 1,
    panX: 0,
    panY: 0,
    isDragging: false,
    dragStartX: 0,
    dragStartY: 0
};

let jobZoomState = {
    scale: 1,
    panX: 0,
    panY: 0,
    isDragging: false,
    dragStartX: 0,
    dragStartY: 0
};

// ============ INIT ============

document.addEventListener('DOMContentLoaded', async () => {
    // Set user info in sidebar
    setUserSidebarInfo();

    // Initialize Mermaid once
    mermaid.initialize({
        startOnLoad: false,
        theme: 'default',
        securityLevel: 'loose',
        flowchart: {
            useMaxWidth: true,
            htmlLabels: true,
            curve: 'linear'
        },
        graph: {
            useMaxWidth: true,
            htmlLabels: true
        }
    });

    try {
        // Load data first
        await loadAllData();

        // Initial view: Profiles → Flowchart
        await showProfileView('overview');
    } catch (error) {
        console.error('Error during documentation page initialization:', error);
        showToast('Error initializing documentation', 'danger');
    }
});

// ============ TAB MANAGEMENT ============

function showMainTab(tab) {
    currentMainTab = tab;

    // Hide all main tab contents
    document.querySelectorAll('[id^="content-"]').forEach(el => {
        if (el.id === 'content-profiles' || el.id === 'content-jobs') {
            el.classList.add('hidden');
        }
    });

    // Reset styling on main tab buttons
    document.querySelectorAll('[id^="tab-"][id*="profiles"], [id^="tab-"][id*="jobs"]').forEach(el => {
        if (el.id === 'tab-profiles' || el.id === 'tab-jobs') {
            el.classList.remove('text-blue-600', 'border-blue-600', 'border-b-2');
            el.classList.add('text-gray-600');
        }
    });

    // Activate selected tab
    const contentDiv = document.getElementById(`content-${tab}`);
    const tabBtn = document.getElementById(`tab-${tab}`);

    if (contentDiv) {
        contentDiv.classList.remove('hidden');
    }
    if (tabBtn) {
        tabBtn.classList.remove('text-gray-600');
        tabBtn.classList.add('text-blue-600', 'border-blue-600', 'border-b-2');
    }

    // Trigger diagram redraw for the active main tab
    if (tab === 'profiles') {
        showProfileDiagrams();
    } else if (tab === 'jobs') {
        showJobDiagrams();
    }
}

async function showProfileView(view) {
    try {
        console.log('showProfileView called with view:', view);
        currentProfileView = view;
        console.log('currentProfileView set to:', currentProfileView);

        updateSubTabStyling('profile', view);
        console.log('updateSubTabStyling completed');

        await showProfileDiagrams();
        console.log('showProfileDiagrams called');
    } catch (error) {
        console.error('Error in showProfileView:', error);
    }
}

async function showJobView(view) {
    try {
        currentJobView = view;
        updateSubTabStyling('job', view);
        await showJobDiagrams();
    } catch (error) {
        console.error('Error in showJobView:', error);
    }
}

function updateSubTabStyling(type, activeView) {
    const buttons = document.querySelectorAll(`[id^="tab-${type}-"]`);
    buttons.forEach(btn => {
        btn.classList.remove('text-blue-600', 'border-blue-600', 'border-b-2');
        btn.classList.add('text-gray-600');
    });

    const activeBtn = document.getElementById(`tab-${type}-${activeView}`);
    if (activeBtn) {
        activeBtn.classList.remove('text-gray-600');
        activeBtn.classList.add('text-blue-600', 'border-blue-600', 'border-b-2');
    }
}

// ============ DATA LOADING ============

async function loadAllData() {
    try {
        // Profiles
        const profilesResponse = await authenticatedFetch('/api/profiles/');
        if (profilesResponse.ok) {
            allProfiles = await profilesResponse.json();
            populateProfileSelect();
        }

        // Jobs
        const jobsResponse = await authenticatedFetch('/api/jobs/');
        if (jobsResponse.ok) {
            allJobs = await jobsResponse.json();
            populateJobSelect();
        }

        // Connections
        const connectionsResponse = await authenticatedFetch('/api/connections/');
        if (connectionsResponse.ok) {
            allConnections = await connectionsResponse.json();
        }

        // Destinations
        const destinationsResponse = await authenticatedFetch('/api/destinations/');
        if (destinationsResponse.ok) {
            allDestinations = await destinationsResponse.json();
        }

        // Templates
        const templatesResponse = await authenticatedFetch('/api/templates/');
        if (templatesResponse.ok) {
            allTemplates = await templatesResponse.json();
        }

        console.log(
            `Loaded: ${allProfiles.length} profiles, ${allJobs.length} jobs, ` +
            `${allConnections.length} connections, ${allDestinations.length} destinations, ` +
            `${allTemplates.length} templates`
        );
    } catch (error) {
        console.error('Error loading data:', error);
        showToast('Error loading data', 'danger');
    }
}

function populateProfileSelect() {
    const select = document.getElementById('profile-select');
    if (!select) return;

    select.innerHTML = '<option value="">All Profiles Overview</option>';
    allProfiles.forEach(profile => {
        const option = document.createElement('option');
        option.value = profile.id;
        option.textContent = profile.name;
        select.appendChild(option);
    });
}

function populateJobSelect() {
    const select = document.getElementById('job-select');
    if (!select) return;

    select.innerHTML = '<option value="">All Jobs Overview</option>';
    allJobs.forEach(job => {
        const option = document.createElement('option');
        option.value = job.id;
        option.textContent = job.name;
        select.appendChild(option);
    });
}

// ============ PROFILE DIAGRAMS ============

function handleProfileSelection() {
    const select = document.getElementById('profile-select');
    selectedProfileId = select.value ? parseInt(select.value, 10) : null;
    showProfileDiagrams();
}

function refreshProfileDiagrams() {
    loadAllData().then(() => showProfileDiagrams());
}

async function showProfileDiagrams() {
    console.log('showProfileDiagrams called, currentProfileView:', currentProfileView);
    const diagramDiv = document.getElementById('profile-diagram');

    if (!diagramDiv) {
        console.error('profile-diagram container not found in DOM');
        return;
    }

    let diagram = null;

    try {
        if (currentProfileView === 'overview') {
            diagram = buildProfileFlowchartDiagram();
        } else if (currentProfileView === 'relationship') {
            diagram = buildProfileRelationshipDiagram();
        }

        if (!diagram) {
            console.log('Diagram is null');
            diagramDiv.innerHTML = '<div class="text-gray-400 text-center p-4">No profiles available</div>';
            diagramDiv.className = '';
            return;
        }

        const trimmedDiagram = diagram.trim();
        if (!trimmedDiagram) {
            console.log('Diagram is whitespace only');
            diagramDiv.innerHTML = '<div class="text-gray-400 text-center p-4">No profiles available</div>';
            diagramDiv.className = '';
            return;
        }

        // Clear any existing SVG and remove old classes
        diagramDiv.innerHTML = '';
        diagramDiv.className = '';

        // Create a new inner mermaid block
        const mermaidDiv = document.createElement('div');
        mermaidDiv.className = 'mermaid w-full h-full';

        // IMPORTANT: use textContent so browser doesn't treat diagram as HTML
        mermaidDiv.textContent = trimmedDiagram;

        diagramDiv.appendChild(mermaidDiv);

        // Give the DOM a moment to attach
        await new Promise(resolve => setTimeout(resolve, 0));

        // Render only this diagram, not every .mermaid on the page
        if (window.mermaid && window.mermaid.run) {
            await window.mermaid.run({
                querySelector: '#profile-diagram .mermaid'
            });
        }

        // Wait for SVG to be created and rendered
        await waitForSVG('#profile-diagram svg', 2000);

        // Initialize zoom/pan after rendering
        initializeProfileZoomPan();
    } catch (error) {
        console.error('Error rendering Mermaid diagram:', error);
        diagramDiv.innerHTML = '<div class="text-red-400 text-center p-4">Error rendering diagram</div>';
        diagramDiv.className = '';
    }
}

function buildProfileFlowchartDiagram() {
    if (!selectedProfileId && allProfiles.length === 0) {
        return null;
    }

    if (selectedProfileId) {
        const profile = allProfiles.find(p => p.id === selectedProfileId);
        if (!profile) return null;
        return buildSingleProfileFlowchart(profile);
    }

    if (allProfiles.length === 0) {
        return null;
    }

    return buildAllProfilesFlowchart();
}

/**
 * Builds a detailed Mermaid flowchart for a single profile's execution path,
 * using a top-down layout and a vertical subgraph.
 * @param {object} profile - The profile object. Assumed to have properties:
 * name, connectionId, outputDestinationId,
 * preProcessType, templateId, deltaSyncEnabled,
 * postProcessType, outputFormat, and isEnabled.
 * @returns {string|null} The Mermaid definition string or null if profile is invalid.
 */
function buildSingleProfileFlowchart(profile) {
    if (!profile) {
        return null;
    }

    // Helper to safely escape strings for Mermaid node labels
    function escapeString(str) {
        return (str || "").toString().replace(/"/g, "#quot;");
    }

    // 1. Data fetching and setup (assuming these global arrays exist: allConnections, allDestinations, allTemplates)
    const isEnabled = profile.isEnabled !== false; // Treat undefined/true as enabled
    const connection = allConnections.find(c => c.id === profile.connectionId);
    const destination = allDestinations.find(d => d.id === profile.outputDestinationId);
    const template = allTemplates.find(t => t.id === profile.templateId);

    const connectionName = connection ? connection.name : "Unknown Connection";
    const destinationName = destination ? destination.name : "Unknown Destination";
    const templateName = template ? template.name : null;

    // Top-down layout
    let mermaidDef = "flowchart TB\n";
    mermaidDef += '    subgraph Flow["Profile Execution Flow"]\n';
    mermaidDef += "        direction TB\n";

    let prevNode = "Conn";

    // 2. Connection node
    mermaidDef += `        Conn["Connection ${escapeString(connectionName)} (${connection?.type || "Unknown"})"]:::conn\n`;

    // 3. Pre-processing
    if (profile.preProcessType) {
        mermaidDef += `        Pre["Pre-Process ${escapeString(profile.preProcessType)}"]:::process\n`;
        mermaidDef += `        ${prevNode} --> Pre\n`;
        prevNode = "Pre";
    }

    // 4. Main profile
    mermaidDef += `        Main["Profile ${escapeString(profile.name)}"]:::profile\n`;
    mermaidDef += `        ${prevNode} --> Main\n`;
    prevNode = "Main";

    // 5. Enabled/Disabled Check (The Play/Stop node)
    const checkNode = "Check";
    if (isEnabled) {
        mermaidDef += `        ${checkNode}{Execute Flow}:::enabled\n`;
    } else {
        mermaidDef += `        ${checkNode}{Disabled: Stopped}:::disabled\n`;
    }
    mermaidDef += `        ${prevNode} --> ${checkNode}\n`;
    prevNode = checkNode;

    // --- Execution Path ---
    if (isEnabled) {
        // 6. Template / Transform
        if (profile.templateId) {
            const displayName = templateName ? escapeString(templateName) : `Template ${profile.templateId}`;
            mermaidDef += `        Transform["Transform: ${displayName}"]:::transforms\n`;
            mermaidDef += `        ${prevNode} --> Transform\n`;
            prevNode = "Transform";
        }

        // 7. Delta Sync
        if (profile.deltaSyncEnabled) {
            mermaidDef += `        Delta["Delta Sync Check"]:::delta\n`;
            mermaidDef += `        ${prevNode} --> Delta\n`;
            prevNode = "Delta";
        }

        // 8. Post-processing
        if (profile.postProcessType) {
            mermaidDef += `        Post["Post-Process ${escapeString(profile.postProcessType)}"]:::process\n`;
            mermaidDef += `        ${prevNode} --> Post\n`;
            prevNode = "Post";
        }

        // 9. Destination (template's outputFormat overrides profile's outputFormat if template exists)
        const destName = destination ? destinationName : `Destination ${profile.outputDestinationId}`;
        const outputFormat = template?.outputFormat || profile.outputFormat || "Unknown";
        mermaidDef += `        Dest["${escapeString(destName)} (${escapeString(outputFormat)})"]:::dest\n`;
        mermaidDef += `        ${prevNode} --> Dest\n`;
    } else {
        // 10. Termination for disabled flow
        mermaidDef += `        Term[Stopped]:::stop\n`;
        mermaidDef += `        ${prevNode} --> Term\n`;
    }

    mermaidDef += "    end\n";

    // 11. Styling
    mermaidDef += `\n    classDef conn fill:#4F46E5,stroke:#333,color:#fff;\n`;
    mermaidDef += `    classDef process fill:#F59E0B,stroke:#333,color:#fff;\n`;
    mermaidDef += `    classDef profile fill:#10B981,stroke:#333,color:#fff;\n`;
    mermaidDef += `    classDef transforms fill:#8B5CF6,stroke:#333,color:#fff;\n`;
    mermaidDef += `    classDef delta fill:#EC4899,stroke:#333,color:#fff;\n`;
    mermaidDef += `    classDef dest fill:#06B6D4,stroke:#333,color:#fff;\n`;
    mermaidDef += `    classDef enabled fill:#D1FAE5,stroke:#10B981,color:#10B981;\n`;
    mermaidDef += `    classDef disabled fill:#FEE2E2,stroke:#EF4444,color:#EF4444;\n`;
    mermaidDef += `    classDef stop fill:#4B5563,stroke:#1F2937,color:#fff;\n`;

    return mermaidDef;
}


function escapeString(str) {
    if (!str) return '';
    return String(str)
        .replace(/"/g, '\\"')
        .replace(/\n/g, ' ');
}

function buildAllProfilesFlowchart() {
    if (!allProfiles || allProfiles.length === 0) {
        return null;
    }

    // 1. Group profiles by connectionId
    const groups = {};
    allProfiles.forEach(p => {
        const key = String(p.connectionId);
        if (!groups[key]) groups[key] = [];
        groups[key].push(p);
    });

    // Top-down layout
    let mermaid = "flowchart TB\n";
    let g = 0;

    for (const [connId, profiles] of Object.entries(groups)) {
        const conn = allConnections.find(c => c.id === Number(connId));
        const connName = conn ? conn.name : `Connection ${connId}`;
        const connNodeId = `C${g}`;

        // We'll gather lines separately so we can nest subgraphs:
        // - CG{g}_P: Profiles
        // - CG{g}_D: Destinations (deduped)
        const profileLines = [];
        const destLines = [];
        const edgeLines = [];

        // Map to dedupe destinations per connection
        const destKeyToNodeId = {};

        profiles.forEach((profile, i) => {
            const profileNodeId = `P${g}_${i}`;
            const isEnabled = profile.isEnabled !== false;
            const profileClass = isEnabled ? "profile" : "disabled_profile";

            // Shorter, cleaner label; type is implied by Profiles subgraph
            profileLines.push(
                `        ${profileNodeId}["${escapeString(profile.name)}"]:::${profileClass}\n`
            );

            // Destination dedupe key: destination + format
            const destId = profile.outputDestinationId;
            const format = profile.outputFormat || "Unknown";
            const destKey = `${String(destId)}|${format}`;

            let destNodeId = destKeyToNodeId[destKey];
            if (!destNodeId) {
                destNodeId = `D${g}_${Object.keys(destKeyToNodeId).length}`;
                destKeyToNodeId[destKey] = destNodeId;

                const dest = allDestinations.find(d => d.id === destId);
                const destName = dest ? dest.name : `Destination ${destId}`;
                const destLabel = `${destName} (${format})`;

                destLines.push(
                    `        ${destNodeId}["${escapeString(destLabel)}"]:::dest\n`
                );
            }

            // Connection -> Profile
            edgeLines.push(
                `        ${connNodeId} --> ${profileNodeId}\n`
            );

            // Profile -> Destination (solid if enabled, dotted "Disabled" if disabled)
            if (isEnabled) {
                edgeLines.push(
                    `        ${profileNodeId} --> ${destNodeId}\n`
                );
            } else {
                edgeLines.push(
                    `        ${profileNodeId} -.-|>|Disabled| ${destNodeId}\n`
                );
            }
        });

        // Build the subgraph for this connection
        mermaid += `\n    subgraph CG${g}["${escapeString(connName)}"]\n`;
        mermaid += "        direction TB\n";
        mermaid += `        ${connNodeId}["${escapeString(connName)}"]:::conn\n\n`;

        // Profiles cluster
        mermaid += `        subgraph CG${g}_P["Profiles"]\n`;
        profileLines.forEach(line => {
            mermaid += line;
        });
        mermaid += "        end\n\n";

        // Destinations cluster (deduped)
        mermaid += `        subgraph CG${g}_D["Destinations"]\n`;
        destLines.forEach(line => {
            mermaid += line;
        });
        mermaid += "        end\n\n";

        // Edges (after nodes so Mermaid is happy)
        edgeLines.forEach(line => {
            mermaid += line;
        });

        mermaid += "    end\n";

        g++;
    }

    // Styling
    mermaid += `\n    classDef conn fill:#4F46E5,stroke:#333,color:#fff;\n`;
    mermaid += `    classDef profile fill:#10B981,stroke:#333,color:#fff;\n`;
    mermaid += `    classDef disabled_profile fill:#FEE2E2,stroke:#EF4444,color:#000;\n`;
    mermaid += `    classDef process fill:#F59E0B,stroke:#333,color:#fff;\n`;
    mermaid += `    classDef transform fill:#8B5CF6,stroke:#333,color:#fff;\n`;
    mermaid += `    classDef delta fill:#EC4899,stroke:#333,color:#fff;\n`;
    mermaid += `    classDef dest fill:#06B6D4,stroke:#333,color:#fff;\n`;
    mermaid += `    classDef enabled fill:#D1FAE5,stroke:#10B981,color:#10B981;\n`;
    mermaid += `    classDef disabled fill:#FEE2E2,stroke:#EF4444,color:#EF4444;\n`;
    mermaid += `    classDef stop fill:#4B5563,stroke:#1F2937,color:#fff;\n`;

    return mermaid;
}


function buildProfileRelationshipDiagram() {
    console.log('buildProfileRelationshipDiagram called - selectedProfileId:', selectedProfileId, 'allProfiles:', allProfiles.length);

    if (!selectedProfileId && allProfiles.length === 0) {
        return null;
    }

    let mermaidDef = 'flowchart TB\n';

    if (selectedProfileId) {
        const profile = allProfiles.find(p => p.id === selectedProfileId);
        if (!profile) {
            console.log('Profile not found for id:', selectedProfileId);
            return null;
        }

        mermaidDef += '    subgraph Connections["Connections"]\n';
        const connection = allConnections.find(c => c.id === profile.connectionId);
        const connName = connection ? connection.name : `Connection ${profile.connectionId}`;
        mermaidDef += `        C["${escapeString(connName)} (${connection?.type || 'Unknown'})"]\n`;
        mermaidDef += '    end\n';

        mermaidDef += '    subgraph Profiles["Profiles"]\n';
        mermaidDef += `        P["${escapeString(profile.name)}"]\n`;
        mermaidDef += '    end\n';

        mermaidDef += '    subgraph Destinations["Destinations"]\n';
        const destination = allDestinations.find(d => d.id === profile.outputDestinationId);
        const destName = destination ? destination.name : 'Unknown Destination';
        mermaidDef += `        D["${escapeString(destName)}"]\n`;
        mermaidDef += '    end\n';

        mermaidDef += '    C -->|Source| P\n';
        mermaidDef += '    P -->|Output| D\n';
    } else {
        // All profiles relationship view

        if (allConnections.length > 0) {
            mermaidDef += '    subgraph Connections["Connections"]\n';
            allConnections.slice(0, 5).forEach((conn, idx) => {
                mermaidDef += `        C${idx}["${escapeString(conn.name)}"]\n`;
            });
            if (allConnections.length > 5) {
                mermaidDef += `        C_more["... and ${allConnections.length - 5} more"]\n`;
            }
            mermaidDef += '    end\n';
        }

        if (allProfiles.length > 0) {
            mermaidDef += '    subgraph Profiles["Profiles"]\n';
            allProfiles.slice(0, 5).forEach((profile, idx) => {
                mermaidDef += `        P${idx}["${escapeString(profile.name)}"]\n`;
            });
            if (allProfiles.length > 5) {
                mermaidDef += `        P_more["... and ${allProfiles.length - 5} more"]\n`;
            }
            mermaidDef += '    end\n';
        }

        if (allDestinations.length > 0) {
            mermaidDef += '    subgraph Destinations["Destinations"]\n';
            allDestinations.slice(0, 5).forEach((dest, idx) => {
                mermaidDef += `        D${idx}["${escapeString(dest.name)}"]\n`;
            });
            if (allDestinations.length > 5) {
                mermaidDef += `        D_more["... and ${allDestinations.length - 5} more"]\n`;
            }
            mermaidDef += '    end\n';
        }

        if (allProfiles.length > 0 && (allConnections.length > 0 || allDestinations.length > 0)) {
            allProfiles.slice(0, 3).forEach((profile, idx) => {
                const connIdx = allConnections.findIndex(c => c.id === profile.connectionId);
                if (connIdx >= 0) {
                    mermaidDef += `    C${connIdx} -->|Source| P${idx}\n`;
                }
                const destIdx = allDestinations.findIndex(d => d.id === profile.outputDestinationId);
                if (destIdx >= 0) {
                    mermaidDef += `    P${idx} -->|Output| D${destIdx}\n`;
                }
            });
        }
    }

    // If somehow nothing useful was added
    if (mermaidDef === 'flowchart TB\n') {
        console.log('Relationship diagram is empty, returning null');
        return null;
    }

    console.log('Returning relationship diagram with length:', mermaidDef.length);
    return mermaidDef;
}

// ============ JOB DIAGRAMS ============

function handleJobSelection() {
    const select = document.getElementById('job-select');
    selectedJobId = select.value ? parseInt(select.value, 10) : null;
    showJobDiagrams();
}

function refreshJobDiagrams() {
    loadAllData().then(() => showJobDiagrams());
}

async function showJobDiagrams() {
    const diagramDiv = document.getElementById('job-diagram');
    if (!diagramDiv) {
        console.error('job-diagram container not found in DOM');
        return;
    }

    let diagram = null;

    try {
        if (currentJobView === 'overview') {
            diagram = buildJobFlowchartDiagram();
        } else if (currentJobView === 'relationship') {
            diagram = buildJobRelationshipDiagram();
        }

        if (!diagram || !diagram.trim()) {
            diagramDiv.innerHTML = '<div class="text-gray-400 text-center p-4">No jobs available</div>';
            diagramDiv.className = '';
            return;
        }

        // Clear any existing content and remove mermaid class
        diagramDiv.innerHTML = '';
        diagramDiv.className = '';

        // Create a new div for the mermaid diagram
        const mermaidDiv = document.createElement('div');
        mermaidDiv.className = 'mermaid w-full h-full';
        mermaidDiv.textContent = diagram.trim();
        diagramDiv.appendChild(mermaidDiv);

        // Allow DOM to update
        await new Promise(resolve => setTimeout(resolve, 0));

        // Render only this diagram
        if (window.mermaid && window.mermaid.run) {
            await window.mermaid.run({
                querySelector: '#job-diagram .mermaid'
            });
        }

        // Wait for SVG to be created and rendered
        await waitForSVG('#job-diagram svg', 2000);

        // Initialize zoom/pan after rendering
        initializeJobZoomPan();
    } catch (error) {
        console.error('Error rendering Mermaid diagram:', error);
        diagramDiv.innerHTML = '<div class="text-red-400 text-center p-4">Error rendering diagram</div>';
        diagramDiv.className = '';
    }
}

function buildJobFlowchartDiagram() {
    if (!selectedJobId && allJobs.length === 0) {
        return null;
    }

    if (selectedJobId) {
        const job = allJobs.find(j => j.id === selectedJobId);
        if (!job) return null;
        return buildSingleJobFlowchart(job);
    }

    return buildAllJobsFlowchart();
}

function buildSingleJobFlowchart(job) {
    let mermaidDef = 'flowchart LR\n';

    // Job node
    mermaidDef += `    Job["Job ${escapeString(job.name)} (${job.scheduleType || 'Manual'})"]:::job\n`;

    // Dependencies
    if (job.dependsOnJobIds) {
        const deps = job.dependsOnJobIds
            .split(',')
            .map(d => parseInt(d.trim(), 10))
            .filter(d => !isNaN(d));

        deps.forEach((depId, idx) => {
            const depJob = allJobs.find(j => j.id === depId);
            const depName = depJob ? depJob.name : `Job ${depId}`;
            mermaidDef += `    Dep${idx}["${escapeString(depName)}"]:::job\n`;
            mermaidDef += `    Dep${idx} -->|Depends On| Job\n`;
        });
    }

    // Profile execution
    if (job.profileId) {
        const profile = allProfiles.find(p => p.id === job.profileId);
        const profileName = profile ? profile.name : `Profile ${job.profileId}`;
        mermaidDef += `    Prof["Profile ${escapeString(profileName)}"]:::profile\n`;
        mermaidDef += '    Job -->|Executes| Prof\n';

        if (profile) {
            const destination = allDestinations.find(d => d.id === profile.outputDestinationId);
            const destName = destination ? destination.name : 'Unknown';
            mermaidDef += `    Dest["${escapeString(destName)}"]:::dest\n`;
            mermaidDef += '    Prof -->|Output| Dest\n';
        }
    }

    mermaidDef += '    classDef job fill:#10B981,stroke:#333,color:#fff\n';
    mermaidDef += '    classDef profile fill:#8B5CF6,stroke:#333,color:#fff\n';
    mermaidDef += '    classDef dest fill:#06B6D4,stroke:#333,color:#fff\n';

    return mermaidDef;
}

function buildAllJobsFlowchart() {
    if (allJobs.length === 0) {
        return null;
    }

    let mermaidDef = 'flowchart TB\n';

    // Group jobs by schedule type
    const bySchedule = {};
    allJobs.forEach(job => {
        const schedType = job.scheduleType || 'Manual';
        if (!bySchedule[schedType]) {
            bySchedule[schedType] = [];
        }
        bySchedule[schedType].push(job);
    });

    const scheduleKeys = Object.keys(bySchedule);

    // Create subgraphs for each schedule type
    scheduleKeys.forEach((schedType, schedIdx) => {
        const jobs = bySchedule[schedType];
        mermaidDef += `\n    subgraph SG${schedIdx}["${schedType} Jobs"]\n`;
        mermaidDef += '        direction TB\n';

        // Job nodes
        mermaidDef += `        subgraph JOBS${schedIdx}["Jobs"]\n`;
        jobs.forEach((job, idx) => {
            mermaidDef += `            J${schedIdx}_${idx}["${escapeString(job.name)}"]\n`;
        });
        mermaidDef += '        end\n';

        // Jobs with profiles
        const jobsWithProfiles = jobs.filter(j => j.profileId);
        if (jobsWithProfiles.length > 0) {
            mermaidDef += `\n        subgraph PROFS${schedIdx}["Profiles Executed"]\n`;
            jobsWithProfiles.forEach((job, idx) => {
                const profile = allProfiles.find(p => p.id === job.profileId);
                const profName = profile ? escapeString(profile.name) : `Profile ${job.profileId}`;
                mermaidDef += `            PROF${schedIdx}_${idx}["${profName}"]\n`;
            });
            mermaidDef += '        end\n';
        }

        // Destination group
        const destSet = new Set();
        jobsWithProfiles.forEach(job => {
            const profile = allProfiles.find(p => p.id === job.profileId);
            if (profile) {
                const dest = allDestinations.find(d => d.id === profile.outputDestinationId);
                if (dest) destSet.add(dest.id);
            }
        });

        if (destSet.size > 0) {
            mermaidDef += `\n        subgraph DESTS${schedIdx}["Destinations"]\n`;
            Array.from(destSet).forEach((destId, idx) => {
                const dest = allDestinations.find(d => d.id === destId);
                const destName = dest ? escapeString(dest.name) : `Dest ${destId}`;
                mermaidDef += `            DEST${schedIdx}_${idx}["${destName}"]\n`;
            });
            mermaidDef += '        end\n';
        }

        mermaidDef += '    end\n';

        // Create connections
        const destArray = Array.from(destSet);

        jobsWithProfiles.forEach((job, idx) => {
            const jobIdx = jobs.findIndex(j => j.id === job.id);
            mermaidDef += `    J${schedIdx}_${jobIdx} --> PROF${schedIdx}_${idx}\n`;

            const profile = allProfiles.find(p => p.id === job.profileId);
            if (profile) {
                const dest = allDestinations.find(d => d.id === profile.outputDestinationId);
                if (dest) {
                    const destIdx = destArray.indexOf(dest.id);
                    mermaidDef += `    PROF${schedIdx}_${idx} --> DEST${schedIdx}_${destIdx}\n`;
                }
            }
        });

        // Job dependencies
        const jobsWithDeps = jobs.filter(j => j.dependsOnJobIds && j.dependsOnJobIds.trim());
        jobsWithDeps.forEach(job => {
            const jobIdx = jobs.findIndex(j => j.id === job.id);
            const deps = job.dependsOnJobIds
                .split(',')
                .map(d => parseInt(d.trim(), 10))
                .filter(d => !isNaN(d));

            deps.forEach(depId => {
                const depJob = allJobs.find(j => j.id === depId);
                if (depJob) {
                    const depSchedule = scheduleKeys.find(
                        key => bySchedule[key].find(j => j.id === depId)
                    );
                    if (depSchedule) {
                        const depScheduleIdx = scheduleKeys.indexOf(depSchedule);
                        const depJobIdx = bySchedule[depSchedule].findIndex(j => j.id === depId);
                        mermaidDef += `    J${depScheduleIdx}_${depJobIdx} -->|depends| J${schedIdx}_${jobIdx}\n`;
                    }
                }
            });
        });
    });

    // Styling
    mermaidDef += `\n    classDef job fill:#10B981,stroke:#333,color:#fff\n`;
    mermaidDef += `    classDef profile fill:#8B5CF6,stroke:#333,color:#fff\n`;
    mermaidDef += `    classDef dest fill:#06B6D4,stroke:#333,color:#fff\n`;

    return mermaidDef;
}

function buildJobRelationshipDiagram() {
    if (!selectedJobId && allJobs.length === 0) {
        return null;
    }

    let mermaidDef = 'flowchart TB\n';

    if (selectedJobId) {
        const job = allJobs.find(j => j.id === selectedJobId);
        if (!job) return null;

        mermaidDef += '    subgraph Jobs["Jobs"]\n';
        mermaidDef += `        J["${escapeString(job.name)} (${job.scheduleType || 'Manual'})"]\n`;
        mermaidDef += '    end\n';

        if (job.profileId) {
            mermaidDef += '    subgraph Profiles["Profiles"]\n';
            const profile = allProfiles.find(p => p.id === job.profileId);
            const profileName = profile ? profile.name : `Profile ${job.profileId}`;
            mermaidDef += `        P["${escapeString(profileName)}"]\n`;
            mermaidDef += '    end\n';

            if (profile) {
                const destination = allDestinations.find(d => d.id === profile.outputDestinationId);
                if (destination) {
                    mermaidDef += '    subgraph Destinations["Destinations"]\n';
                    const destName = destination.name || 'Unknown';
                    mermaidDef += `        D["${escapeString(destName)}"]\n`;
                    mermaidDef += '    end\n';
                    mermaidDef += '    P -->|Output| D\n';
                }
            }

            mermaidDef += '    J -->|Executes| P\n';
        }
    } else {
        // All jobs relationship
        mermaidDef += '    subgraph Jobs["Jobs"]\n';
        allJobs.slice(0, 5).forEach((job, idx) => {
            mermaidDef += `        J${idx}["${escapeString(job.name)}"]\n`;
        });
        if (allJobs.length > 5) {
            mermaidDef += `        J_more["... and ${allJobs.length - 5} more"]\n`;
        }
        mermaidDef += '    end\n';

        const jobProfiles = new Set();
        allJobs.forEach(job => {
            if (job.profileId) jobProfiles.add(job.profileId);
        });

        const profileArray = Array.from(jobProfiles)
            .map(id => allProfiles.find(p => p.id === id))
            .filter(p => p);

        if (profileArray.length > 0) {
            mermaidDef += '    subgraph Profiles["Profiles"]\n';
            profileArray.slice(0, 5).forEach((profile, idx) => {
                mermaidDef += `        P${idx}["${escapeString(profile.name)}"]\n`;
            });
            if (profileArray.length > 5) {
                mermaidDef += `        P_more["... and ${profileArray.length - 5} more"]\n`;
            }
            mermaidDef += '    end\n';
        }

        if (allDestinations.length > 0) {
            mermaidDef += '    subgraph Destinations["Destinations"]\n';
            allDestinations.slice(0, 5).forEach((dest, idx) => {
                mermaidDef += `        D${idx}["${escapeString(dest.name)}"]\n`;
            });
            if (allDestinations.length > 5) {
                mermaidDef += `        D_more["... and ${allDestinations.length - 5} more"]\n`;
            }
            mermaidDef += '    end\n';
        }

        if (profileArray.length > 0) {
            allJobs.slice(0, 3).forEach((job, idx) => {
                if (job.profileId) {
                    const profIdx = profileArray.findIndex(p => p.id === job.profileId);
                    if (profIdx >= 0) {
                        mermaidDef += `    J${idx} -->|Executes| P${profIdx}\n`;
                    }
                }
            });
        }
    }

    if (mermaidDef === 'flowchart TB\n') {
        return null;
    }

    return mermaidDef;
}

// ============ UTILITY FUNCTIONS ============

function escapeString(str) {
    if (!str) return '';
    return String(str)
        .replace(/"/g, '\\"')
        .replace(/\n/g, ' ')
        .substring(0, 30); // Limit length for readability
}

// ============ HELPER FUNCTIONS ============

async function waitForSVG(selector, timeout = 2000) {
    const startTime = Date.now();
    while (Date.now() - startTime < timeout) {
        const svg = document.querySelector(selector);
        if (svg && svg.getBBox) {
            try {
                svg.getBBox(); // Throws if not fully rendered
                return svg;
            } catch {
                await new Promise(resolve => setTimeout(resolve, 50));
                continue;
            }
        }
        await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw new Error(`SVG element not found or not rendered: ${selector}`);
}

// ============ ZOOM AND PAN FUNCTIONS ============

function resetProfileZoom() {
    profileZoomState = {
        scale: 1,
        panX: 0,
        panY: 0,
        isDragging: false,
        dragStartX: 0,
        dragStartY: 0
    };
    applyProfileTransform();
}

function resetJobZoom() {
    jobZoomState = {
        scale: 1,
        panX: 0,
        panY: 0,
        isDragging: false,
        dragStartX: 0,
        dragStartY: 0
    };
    applyJobTransform();
}

function applyProfileTransform() {
    const svg = document.querySelector('#profile-diagram svg');
    if (svg) {
        svg.style.transformOrigin = '0 0';
        svg.style.transform = `translate(${profileZoomState.panX}px, ${profileZoomState.panY}px) scale(${profileZoomState.scale})`;

        const container = document.getElementById('profile-diagram-container');
        if (container) {
            container.style.overflow = 'auto';
        }
    }
}

function applyJobTransform() {
    const svg = document.querySelector('#job-diagram svg');
    if (svg) {
        svg.style.transformOrigin = '0 0';
        svg.style.transform = `translate(${jobZoomState.panX}px, ${jobZoomState.panY}px) scale(${jobZoomState.scale})`;

        const container = document.getElementById('job-diagram-container');
        if (container) {
            container.style.overflow = 'auto';
        }
    }
}

function initializeProfileZoomPan() {
    const container = document.getElementById('profile-diagram-container');
    if (!container) return;

    // Reset zoom state
    resetProfileZoom();

    // Only initialize once
    if (container._profileZoomInitialized) {
        applyProfileTransform();
        return;
    }
    container._profileZoomInitialized = true;

    // Mouse wheel zoom
    container.addEventListener(
        'wheel',
        (e) => {
            e.preventDefault();
            const delta = e.deltaY > 0 ? -0.1 : 0.1;
            const newScale = Math.max(0.5, Math.min(3, profileZoomState.scale + delta));
            profileZoomState.scale = newScale;
            applyProfileTransform();
        },
        { passive: false }
    );

    // Mouse drag pan
    container.addEventListener('mousedown', (e) => {
        profileZoomState.isDragging = true;
        profileZoomState.dragStartX = e.clientX - profileZoomState.panX;
        profileZoomState.dragStartY = e.clientY - profileZoomState.panY;
        container.style.cursor = 'grabbing';
    });

    document.addEventListener('mousemove', (e) => {
        if (profileZoomState.isDragging) {
            profileZoomState.panX = e.clientX - profileZoomState.dragStartX;
            profileZoomState.panY = e.clientY - profileZoomState.dragStartY;
            applyProfileTransform();
        }
    });

    document.addEventListener('mouseup', () => {
        if (profileZoomState.isDragging) {
            profileZoomState.isDragging = false;
            container.style.cursor = 'grab';
        }
    });

    // Cursor feedback
    container.style.cursor = 'grab';
    container.addEventListener('mouseenter', () => {
        container.style.cursor = profileZoomState.isDragging ? 'grabbing' : 'grab';
    });
    container.addEventListener('mouseleave', () => {
        container.style.cursor = 'grab';
    });
}

function initializeJobZoomPan() {
    const container = document.getElementById('job-diagram-container');
    if (!container) return;

    // Reset zoom state
    resetJobZoom();

    // Only initialize once
    if (container._jobZoomInitialized) {
        applyJobTransform();
        return;
    }
    container._jobZoomInitialized = true;

    // Mouse wheel zoom
    container.addEventListener(
        'wheel',
        (e) => {
            e.preventDefault();
            const delta = e.deltaY > 0 ? -0.1 : 0.1;
            const newScale = Math.max(0.5, Math.min(3, jobZoomState.scale + delta));
            jobZoomState.scale = newScale;
            applyJobTransform();
        },
        { passive: false }
    );

    // Mouse drag pan
    container.addEventListener('mousedown', (e) => {
        jobZoomState.isDragging = true;
        jobZoomState.dragStartX = e.clientX - jobZoomState.panX;
        jobZoomState.dragStartY = e.clientY - jobZoomState.panY;
        container.style.cursor = 'grabbing';
    });

    document.addEventListener('mousemove', (e) => {
        if (jobZoomState.isDragging) {
            jobZoomState.panX = e.clientX - jobZoomState.dragStartX;
            jobZoomState.panY = e.clientY - jobZoomState.dragStartY;
            applyJobTransform();
        }
    });

    document.addEventListener('mouseup', () => {
        if (jobZoomState.isDragging) {
            jobZoomState.isDragging = false;
            container.style.cursor = 'grab';
        }
    });

    // Cursor feedback
    container.style.cursor = 'grab';
    container.addEventListener('mouseenter', () => {
        container.style.cursor = jobZoomState.isDragging ? 'grabbing' : 'grab';
    });
    container.addEventListener('mouseleave', () => {
        container.style.cursor = 'grab';
    });
}

// ============ DOWNLOAD FUNCTIONS ============

async function downloadProfileDiagramAsWebP() {
    try {
        const svg = document.querySelector('#profile-diagram svg');
        if (!svg) {
            showToast('No diagram to download', 'warning');
            return;
        }

        await downloadSVGAsWebP(svg, 'profile-schema.webp');
    } catch (error) {
        console.error('Error downloading diagram:', error);
        showToast('Error downloading diagram', 'danger');
    }
}

async function downloadProfileDiagramAsMermaid() {
    try {
        let diagram = null;
        if (currentProfileView === 'overview') {
            diagram = buildProfileFlowchartDiagram();
        } else if (currentProfileView === 'relationship') {
            diagram = buildProfileRelationshipDiagram();
        }

        if (!diagram) {
            showToast('No diagram to download', 'warning');
            return;
        }

        downloadMermaidFile(diagram, 'profile-schema.txt');
    } catch (error) {
        console.error('Error downloading diagram:', error);
        showToast('Error downloading diagram', 'danger');
    }
}

async function downloadJobDiagramAsWebP() {
    try {
        const svg = document.querySelector('#job-diagram svg');
        if (!svg) {
            showToast('No diagram to download', 'warning');
            return;
        }

        await downloadSVGAsWebP(svg, 'job-schema.webp');
    } catch (error) {
        console.error('Error downloading diagram:', error);
        showToast('Error downloading diagram', 'danger');
    }
}

async function downloadJobDiagramAsMermaid() {
    try {
        let diagram = null;
        if (currentJobView === 'overview') {
            diagram = buildJobFlowchartDiagram();
        } else if (currentJobView === 'relationship') {
            diagram = buildJobRelationshipDiagram();
        }

        if (!diagram) {
            showToast('No diagram to download', 'warning');
            return;
        }

        downloadMermaidFile(diagram, 'job-schema.txt');
    } catch (error) {
        console.error('Error downloading diagram:', error);
        showToast('Error downloading diagram', 'danger');
    }
}

async function downloadSVGAsWebP(svg, filename) {
    return new Promise((resolve, reject) => {
        const bbox = svg.getBBox();
        const padding = 20;
        const svgWidth = bbox.width + padding * 2;
        const svgHeight = bbox.height + padding * 2;

        const scale = 2;
        const canvasWidth = svgWidth * scale;
        const canvasHeight = svgHeight * scale;

        const canvas = document.createElement('canvas');
        canvas.width = canvasWidth;
        canvas.height = canvasHeight;

        const ctx = canvas.getContext('2d');
        if (!ctx) {
            reject(new Error('Could not get canvas context'));
            return;
        }

        // White background
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, canvasWidth, canvasHeight);

        // Clone and prepare SVG
        const clonedSvg = svg.cloneNode(true);
        clonedSvg.setAttribute('width', svgWidth);
        clonedSvg.setAttribute('height', svgHeight);
        clonedSvg.setAttribute('viewBox', `${bbox.x - padding} ${bbox.y - padding} ${svgWidth} ${svgHeight}`);

        // Serialize SVG to string
        const serializer = new XMLSerializer();
        const svgString = serializer.serializeToString(clonedSvg);

        // Create blob from SVG
        const blob = new Blob([svgString], { type: 'image/svg+xml;charset=utf-8' });
        const url = URL.createObjectURL(blob);

        const img = new Image();
        img.onload = () => {
            ctx.drawImage(img, 0, 0, canvasWidth, canvasHeight);

            canvas.toBlob(
                (webpBlob) => {
                    if (!webpBlob) {
                        reject(new Error('Could not convert to WebP'));
                        return;
                    }

                    const webpUrl = URL.createObjectURL(webpBlob);
                    const link = document.createElement('a');
                    link.href = webpUrl;
                    link.download = filename;
                    document.body.appendChild(link);
                    link.click();
                    document.body.removeChild(link);

                    URL.revokeObjectURL(url);
                    URL.revokeObjectURL(webpUrl);

                    showToast('Diagram downloaded successfully', 'success');
                    resolve();
                },
                'image/webp',
                0.95
            );
        };

        img.onerror = () => {
            URL.revokeObjectURL(url);
            reject(new Error('Could not load SVG as image'));
        };

        img.src = url;
    });
}

function downloadMermaidFile(diagramContent, filename) {
    try {
        const blob = new Blob([diagramContent], { type: 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);

        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        URL.revokeObjectURL(url);

        showToast('Diagram downloaded successfully', 'success');
    } catch (error) {
        console.error('Error downloading mermaid file:', error);
        showToast('Error downloading diagram', 'danger');
    }
}
