/**
 * FluxIndex Demo Application
 * Modern RAG infrastructure demo with semantic search
 */

const API_BASE = '';
let allDocuments = [];

// ============================================
// Initialization
// ============================================

document.addEventListener('DOMContentLoaded', () => {
    initNavigation();
    initUploadArea();
    initSearchInput();
    loadStatus();
    loadDocuments();

    // Auto-refresh status every 30 seconds
    setInterval(loadStatus, 30000);
});

// ============================================
// Navigation
// ============================================

function initNavigation() {
    const navItems = document.querySelectorAll('.nav-item');
    navItems.forEach(item => {
        item.addEventListener('click', () => {
            const tab = item.dataset.tab;
            switchTab(tab);
        });
    });
}

function switchTab(tabName) {
    // Update nav items
    document.querySelectorAll('.nav-item').forEach(item => {
        item.classList.toggle('active', item.dataset.tab === tabName);
    });

    // Update tab content
    document.querySelectorAll('.tab-content').forEach(content => {
        content.classList.toggle('active', content.id === `tab-${tabName}`);
    });
}

// ============================================
// Status Management
// ============================================

async function loadStatus() {
    try {
        const response = await fetch(`${API_BASE}/api/status`);
        const data = await response.json();

        document.getElementById('docCount').textContent = formatNumber(data.totalDocuments || 0);
        document.getElementById('chunkCount').textContent = formatNumber(data.totalChunks || 0);
        document.getElementById('modelName').textContent = data.embeddingModel || '-';
        document.getElementById('lastIndexed').textContent = data.lastIndexed
            ? formatDate(data.lastIndexed)
            : '-';

        // Enable/disable delete all button
        const deleteAllBtn = document.getElementById('deleteAllBtn');
        if (deleteAllBtn) {
            deleteAllBtn.disabled = data.totalDocuments === 0;
        }
    } catch (error) {
        console.error('Failed to load status:', error);
    }
}

// ============================================
// Upload Functionality
// ============================================

function initUploadArea() {
    const uploadArea = document.getElementById('uploadArea');
    const fileInput = document.getElementById('fileInput');

    uploadArea.addEventListener('click', (e) => {
        if (e.target.tagName !== 'BUTTON') {
            fileInput.click();
        }
    });

    uploadArea.addEventListener('dragover', (e) => {
        e.preventDefault();
        uploadArea.classList.add('dragover');
    });

    uploadArea.addEventListener('dragleave', () => {
        uploadArea.classList.remove('dragover');
    });

    uploadArea.addEventListener('drop', (e) => {
        e.preventDefault();
        uploadArea.classList.remove('dragover');
        handleFiles(e.dataTransfer.files);
    });

    fileInput.addEventListener('change', (e) => {
        handleFiles(e.target.files);
        e.target.value = ''; // Reset for same file selection
    });
}

function handleFiles(files) {
    Array.from(files).forEach(file => uploadFile(file));
}

async function uploadFile(file) {
    const queue = document.getElementById('uploadQueue');
    const itemId = `upload-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

    // Create upload item
    const item = document.createElement('div');
    item.id = itemId;
    item.className = 'upload-item';
    item.innerHTML = `
        <div class="upload-item-icon">${getFileIcon(file.name)}</div>
        <div class="upload-item-info">
            <div class="upload-item-name">${escapeHtml(file.name)}</div>
            <div class="upload-item-status">Uploading...</div>
            <div class="upload-item-progress">
                <div class="upload-item-progress-fill" style="width: 10%"></div>
            </div>
        </div>
        <button class="upload-item-action" onclick="removeUploadItem('${itemId}')" title="Remove">✕</button>
    `;
    queue.appendChild(item);

    const progressFill = item.querySelector('.upload-item-progress-fill');
    const status = item.querySelector('.upload-item-status');

    try {
        // Simulate progress
        progressFill.style.width = '30%';
        status.textContent = 'Processing document...';

        const formData = new FormData();
        formData.append('file', file);

        const response = await fetch(`${API_BASE}/api/upload`, {
            method: 'POST',
            body: formData
        });

        progressFill.style.width = '80%';
        status.textContent = 'Generating embeddings...';

        const result = await response.json();

        progressFill.style.width = '100%';

        if (result.success) {
            item.classList.add('success');
            status.textContent = `✓ ${result.chunkCount} chunks indexed (${formatDuration(result.processingTimeMs)})`;
            showToast('success', `Successfully indexed "${file.name}"`);
            loadStatus();
            loadDocuments();
        } else {
            item.classList.add('error');
            status.textContent = `✗ ${result.message}`;
            showToast('error', `Failed to index "${file.name}": ${result.message}`);
        }
    } catch (error) {
        item.classList.add('error');
        progressFill.style.width = '100%';
        status.textContent = `✗ Upload failed: ${error.message}`;
        showToast('error', `Upload failed: ${error.message}`);
    }
}

function removeUploadItem(itemId) {
    const item = document.getElementById(itemId);
    if (item) {
        item.style.animation = 'fadeOut 0.2s ease forwards';
        setTimeout(() => item.remove(), 200);
    }
}

function getFileIcon(filename) {
    const ext = filename.split('.').pop().toLowerCase();
    const icons = {
        'pdf': '📕',
        'docx': '📘',
        'doc': '📘',
        'txt': '📄',
        'md': '📝',
        'html': '🌐',
        'htm': '🌐',
        'json': '📋',
        'csv': '📊',
        'xml': '📰'
    };
    return icons[ext] || '📄';
}

// ============================================
// Search Functionality
// ============================================

function initSearchInput() {
    const searchInput = document.getElementById('searchInput');
    searchInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            performSearch();
        }
    });
}

async function performSearch() {
    const query = document.getElementById('searchInput').value.trim();
    if (!query) {
        showToast('info', 'Please enter a search query');
        return;
    }

    const useReranker = document.getElementById('useReranker').checked;
    const topK = parseInt(document.getElementById('topK').value);
    const resultsDiv = document.getElementById('searchResults');

    resultsDiv.innerHTML = `
        <div class="loading-overlay">
            <div class="spinner"></div>
        </div>
    `;

    try {
        const response = await fetch(`${API_BASE}/api/search`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ query, topK, useReranker })
        });

        const data = await response.json();

        if (data.error) {
            resultsDiv.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon">❌</div>
                    <h3>Search Error</h3>
                    <p>${escapeHtml(data.error)}</p>
                </div>
            `;
            return;
        }

        if (!data.results || data.results.length === 0) {
            resultsDiv.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon">🔍</div>
                    <h3>No Results Found</h3>
                    <p>Try adjusting your search query or index more documents</p>
                </div>
            `;
            return;
        }

        let html = `
            <div class="search-stats">
                <span>Found <strong>${data.totalResults}</strong> results</span>
                <span>Search time: <strong>${data.searchTimeMs}ms</strong></span>
                <span>Reranker: <strong>${data.usedReranker ? 'Yes' : 'No'}</strong></span>
            </div>
        `;

        data.results.forEach((result, index) => {
            const content = truncateText(result.content, 400);
            const highlightedContent = highlightQuery(content, query);

            html += `
                <div class="result-item">
                    <div class="result-header">
                        <span class="result-rank">${index + 1}</span>
                        <span class="result-source">${escapeHtml(result.source || 'Unknown source')}</span>
                        <div class="result-scores">
                            <span class="score-badge vector">Vector: ${result.score.toFixed(4)}</span>
                            ${result.wasReranked ? `<span class="score-badge reranked">Reranked: ${result.rerankedScore.toFixed(4)}</span>` : ''}
                        </div>
                    </div>
                    <div class="result-content">${highlightedContent}</div>
                    <div class="result-meta">
                        ${result.metadata?.quality ? `<span>📊 Quality: ${result.metadata.quality}</span>` : ''}
                        ${result.metadata?.importance ? `<span>⭐ Importance: ${result.metadata.importance}</span>` : ''}
                    </div>
                </div>
            `;
        });

        resultsDiv.innerHTML = html;

    } catch (error) {
        resultsDiv.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">❌</div>
                <h3>Search Failed</h3>
                <p>${escapeHtml(error.message)}</p>
            </div>
        `;
    }
}

// ============================================
// Documents Management
// ============================================

async function loadDocuments() {
    const listDiv = document.getElementById('documentsList');

    try {
        const response = await fetch(`${API_BASE}/api/documents`);
        const documents = await response.json();

        allDocuments = documents;
        renderDocuments(documents);

    } catch (error) {
        listDiv.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">❌</div>
                <h3>Failed to Load Documents</h3>
                <p>${escapeHtml(error.message)}</p>
            </div>
        `;
    }
}

function renderDocuments(documents) {
    const listDiv = document.getElementById('documentsList');

    if (!documents || documents.length === 0) {
        listDiv.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">📚</div>
                <h3>No Documents</h3>
                <p>Upload documents to get started with semantic search</p>
            </div>
        `;
        return;
    }

    let html = '';
    documents.forEach(doc => {
        html += `
            <div class="document-item" data-id="${doc.id}">
                <div class="document-icon">${getFileIcon(doc.title)}</div>
                <div class="document-info">
                    <div class="document-title">${escapeHtml(doc.title)}</div>
                    <div class="document-meta">
                        <span>📦 ${doc.chunkCount} chunks</span>
                        <span>📅 ${formatDate(doc.createdAt)}</span>
                    </div>
                </div>
                <div class="document-actions">
                    <button class="btn danger btn-sm" onclick="deleteDocument('${doc.id}', '${escapeHtml(doc.title).replace(/'/g, "\\'")}')">
                        Delete
                    </button>
                </div>
            </div>
        `;
    });

    listDiv.innerHTML = html;
}

function filterDocuments() {
    const filterText = document.getElementById('docFilterInput').value.toLowerCase();
    const filtered = allDocuments.filter(doc =>
        doc.title.toLowerCase().includes(filterText)
    );
    renderDocuments(filtered);
}

async function deleteDocument(id, title) {
    if (!confirm(`Are you sure you want to delete "${title}"?`)) return;

    try {
        await fetch(`${API_BASE}/api/documents/${id}`, { method: 'DELETE' });
        showToast('success', `Deleted "${title}"`);
        loadStatus();
        loadDocuments();
    } catch (error) {
        showToast('error', `Failed to delete: ${error.message}`);
    }
}

async function deleteAllDocuments() {
    if (!confirm('Are you sure you want to delete ALL documents? This cannot be undone.')) return;

    try {
        for (const doc of allDocuments) {
            await fetch(`${API_BASE}/api/documents/${doc.id}`, { method: 'DELETE' });
        }
        showToast('success', 'All documents deleted');
        loadStatus();
        loadDocuments();
    } catch (error) {
        showToast('error', `Failed to delete all: ${error.message}`);
    }
}

// ============================================
// MCP Function Test
// ============================================

async function callMcpFunction() {
    const query = document.getElementById('mcpQuery').value.trim();
    if (!query) {
        showToast('info', 'Please enter a query');
        return;
    }

    const useReranker = document.getElementById('mcpUseReranker').checked;
    const includeMetadata = document.getElementById('mcpIncludeMetadata').checked;
    const topK = parseInt(document.getElementById('mcpTopK').value);
    const maxTokens = parseInt(document.getElementById('mcpMaxTokens').value);
    const outputDiv = document.getElementById('mcpOutput');

    outputDiv.textContent = '// Calling MCP function...';

    try {
        const response = await fetch(`${API_BASE}/api/mcp/search`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ query, topK, useReranker, includeMetadata, maxTokens })
        });

        const data = await response.json();
        outputDiv.textContent = JSON.stringify(data, null, 2);
        showToast('success', 'MCP function executed successfully');

    } catch (error) {
        outputDiv.textContent = `// Error: ${error.message}`;
        showToast('error', `MCP call failed: ${error.message}`);
    }
}

function copyMcpOutput() {
    const output = document.getElementById('mcpOutput').textContent;
    navigator.clipboard.writeText(output).then(() => {
        showToast('success', 'Copied to clipboard');
    }).catch(err => {
        showToast('error', 'Failed to copy');
    });
}

// ============================================
// Toast Notifications
// ============================================

function showToast(type, message) {
    const container = document.getElementById('toastContainer');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;

    const icons = {
        success: '✓',
        error: '✗',
        info: 'ℹ'
    };

    toast.innerHTML = `
        <span class="toast-icon">${icons[type] || 'ℹ'}</span>
        <span class="toast-message">${escapeHtml(message)}</span>
        <button class="toast-close" onclick="this.parentElement.remove()">✕</button>
    `;

    container.appendChild(toast);

    // Auto-remove after 5 seconds
    setTimeout(() => {
        if (toast.parentElement) {
            toast.style.animation = 'fadeOut 0.3s ease forwards';
            setTimeout(() => toast.remove(), 300);
        }
    }, 5000);
}

// ============================================
// Utility Functions
// ============================================

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatNumber(num) {
    if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
    if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
    return num.toString();
}

function formatDate(dateStr) {
    try {
        const date = new Date(dateStr);
        return date.toLocaleDateString('ko-KR', {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    } catch {
        return dateStr;
    }
}

function formatDuration(ms) {
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
    return `${Math.floor(ms / 60000)}m ${Math.floor((ms % 60000) / 1000)}s`;
}

function truncateText(text, maxLength) {
    if (!text) return '';
    if (text.length <= maxLength) return text;
    return text.substring(0, maxLength) + '...';
}

function highlightQuery(text, query) {
    if (!query || !text) return escapeHtml(text);

    const escaped = escapeHtml(text);
    const queryWords = query.toLowerCase().split(/\s+/).filter(w => w.length > 2);

    let result = escaped;
    queryWords.forEach(word => {
        const regex = new RegExp(`(${escapeRegex(word)})`, 'gi');
        result = result.replace(regex, '<mark>$1</mark>');
    });

    return result;
}

function escapeRegex(string) {
    return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// Add fadeOut animation
const style = document.createElement('style');
style.textContent = `
    @keyframes fadeOut {
        from { opacity: 1; transform: translateX(0); }
        to { opacity: 0; transform: translateX(100px); }
    }
`;
document.head.appendChild(style);
