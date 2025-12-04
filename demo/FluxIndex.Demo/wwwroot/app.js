// FluxIndex Demo App

const API_BASE = '';

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    loadStatus();
    loadDocuments();
    setupUploadArea();
    setupSearchEnter();
});

// Status
async function loadStatus() {
    try {
        const response = await fetch(`${API_BASE}/api/status`);
        const data = await response.json();

        document.getElementById('docCount').textContent = data.totalDocuments || 0;
        document.getElementById('chunkCount').textContent = data.totalChunks || 0;
        document.getElementById('lastIndexed').textContent = data.lastIndexed
            ? new Date(data.lastIndexed).toLocaleString()
            : '-';
    } catch (error) {
        console.error('Failed to load status:', error);
    }
}

// Upload
function setupUploadArea() {
    const uploadArea = document.getElementById('uploadArea');
    const fileInput = document.getElementById('fileInput');

    uploadArea.addEventListener('click', () => fileInput.click());

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
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            uploadFile(files[0]);
        }
    });

    fileInput.addEventListener('change', (e) => {
        if (e.target.files.length > 0) {
            uploadFile(e.target.files[0]);
        }
    });
}

async function uploadFile(file) {
    const progressDiv = document.getElementById('uploadProgress');
    const progressFill = document.getElementById('progressFill');
    const uploadStatus = document.getElementById('uploadStatus');

    progressDiv.style.display = 'block';
    progressFill.style.width = '0%';
    uploadStatus.textContent = `Uploading ${file.name}...`;

    const formData = new FormData();
    formData.append('file', file);

    try {
        // Simulate progress
        progressFill.style.width = '30%';
        uploadStatus.textContent = 'Processing document...';

        const response = await fetch(`${API_BASE}/api/upload`, {
            method: 'POST',
            body: formData
        });

        progressFill.style.width = '80%';
        uploadStatus.textContent = 'Indexing chunks...';

        const result = await response.json();

        progressFill.style.width = '100%';

        if (result.success) {
            uploadStatus.textContent = `Success! Created ${result.chunkCount} chunks in ${result.processingTimeMs}ms`;
            uploadStatus.style.color = '#22c55e';
            loadStatus();
            loadDocuments();
        } else {
            uploadStatus.textContent = `Error: ${result.message}`;
            uploadStatus.style.color = '#ef4444';
        }

        setTimeout(() => {
            progressDiv.style.display = 'none';
            uploadStatus.style.color = '';
        }, 3000);

    } catch (error) {
        progressFill.style.width = '100%';
        progressFill.style.background = '#ef4444';
        uploadStatus.textContent = `Error: ${error.message}`;
        uploadStatus.style.color = '#ef4444';
    }
}

// Search
function setupSearchEnter() {
    document.getElementById('searchInput').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            performSearch();
        }
    });
}

async function performSearch() {
    const query = document.getElementById('searchInput').value.trim();
    if (!query) return;

    const useReranker = document.getElementById('useReranker').checked;
    const topK = parseInt(document.getElementById('topK').value);
    const resultsDiv = document.getElementById('searchResults');

    resultsDiv.innerHTML = '<p class="placeholder">Searching...</p>';

    try {
        const response = await fetch(`${API_BASE}/api/search`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ query, topK, useReranker })
        });

        const data = await response.json();

        if (data.error) {
            resultsDiv.innerHTML = `<p class="placeholder" style="color: #ef4444">Error: ${data.error}</p>`;
            return;
        }

        if (data.results.length === 0) {
            resultsDiv.innerHTML = '<p class="placeholder">No results found</p>';
            return;
        }

        let html = `
            <div class="search-stats">
                <span>Found: ${data.totalResults} results</span>
                <span>Time: ${data.searchTimeMs}ms</span>
                <span>Reranker: ${data.usedReranker ? 'Yes' : 'No'}</span>
            </div>
        `;

        data.results.forEach((result, index) => {
            const content = result.content.length > 300
                ? result.content.substring(0, 300) + '...'
                : result.content;

            html += `
                <div class="result-item">
                    <div class="result-header">
                        <span class="result-source">${result.source || 'Unknown'}</span>
                        <div class="result-score">
                            <span>Vector: ${result.score.toFixed(4)}</span>
                            ${result.wasReranked ? `<span class="reranked">Reranked: ${result.rerankedScore.toFixed(4)}</span>` : ''}
                        </div>
                    </div>
                    <div class="result-content">${escapeHtml(content)}</div>
                    ${result.metadata && result.metadata.summary ? `<div class="result-metadata">Summary: ${escapeHtml(result.metadata.summary)}</div>` : ''}
                </div>
            `;
        });

        resultsDiv.innerHTML = html;

    } catch (error) {
        resultsDiv.innerHTML = `<p class="placeholder" style="color: #ef4444">Error: ${error.message}</p>`;
    }
}

// MCP Function
async function callMcpFunction() {
    const query = document.getElementById('mcpQuery').value.trim();
    if (!query) return;

    const includeMetadata = document.getElementById('mcpIncludeMetadata').checked;
    const topK = parseInt(document.getElementById('topK').value);
    const useReranker = document.getElementById('useReranker').checked;
    const outputDiv = document.getElementById('mcpOutput');

    outputDiv.textContent = '// Calling MCP function...';

    try {
        const response = await fetch(`${API_BASE}/api/mcp/search`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ query, topK, useReranker, includeMetadata })
        });

        const data = await response.json();
        outputDiv.textContent = JSON.stringify(data, null, 2);

    } catch (error) {
        outputDiv.textContent = `// Error: ${error.message}`;
    }
}

// Documents
async function loadDocuments() {
    const listDiv = document.getElementById('documentsList');

    try {
        const response = await fetch(`${API_BASE}/api/documents`);
        const documents = await response.json();

        if (documents.length === 0) {
            listDiv.innerHTML = '<p class="placeholder">No documents indexed yet</p>';
            return;
        }

        let html = '';
        documents.forEach(doc => {
            html += `
                <div class="document-item">
                    <div class="document-info">
                        <div class="document-title">${escapeHtml(doc.title)}</div>
                        <div class="document-meta">
                            ${doc.chunkCount} chunks | ${new Date(doc.createdAt).toLocaleDateString()}
                        </div>
                    </div>
                    <button class="btn-delete" onclick="deleteDocument('${doc.id}')">Delete</button>
                </div>
            `;
        });

        listDiv.innerHTML = html;

    } catch (error) {
        listDiv.innerHTML = `<p class="placeholder" style="color: #ef4444">Error loading documents</p>`;
    }
}

async function deleteDocument(id) {
    if (!confirm('Are you sure you want to delete this document?')) return;

    try {
        await fetch(`${API_BASE}/api/documents/${id}`, { method: 'DELETE' });
        loadStatus();
        loadDocuments();
    } catch (error) {
        alert('Failed to delete document: ' + error.message);
    }
}

// Utilities
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
