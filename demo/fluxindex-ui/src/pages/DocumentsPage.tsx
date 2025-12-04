import { useState, useMemo } from 'react';
import { RefreshCw, Trash2, Eye } from 'lucide-react';
import { useDocuments, useDeleteDocument, useStatus } from '../hooks/useApi';
import { useToast } from '../components/Toast';
import DocumentDetailModal from '../components/DocumentDetailModal';
import './DocumentsPage.css';

function formatDate(dateStr: string): string {
  try {
    const date = new Date(dateStr);
    return date.toLocaleDateString('ko-KR', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return dateStr;
  }
}

const fileIcons: Record<string, string> = {
  pdf: 'PDF',
  docx: 'DOCX',
  doc: 'DOC',
  txt: 'TXT',
  md: 'MD',
  html: 'HTML',
  htm: 'HTML',
  json: 'JSON',
  csv: 'CSV',
  xml: 'XML',
};

function getFileIcon(filename: string): string {
  const ext = filename.split('.').pop()?.toLowerCase() || '';
  return fileIcons[ext] || 'FILE';
}

export default function DocumentsPage() {
  const [filter, setFilter] = useState('');
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null);
  const { data: documents, isLoading, refetch } = useDocuments();
  const { data: status } = useStatus();
  const deleteMutation = useDeleteDocument();
  const { showToast } = useToast();

  const filteredDocuments = useMemo(() => {
    if (!documents) return [];
    if (!filter) return documents;
    return documents.filter((doc) =>
      doc.title.toLowerCase().includes(filter.toLowerCase())
    );
  }, [documents, filter]);

  const handleDelete = async (id: string, title: string, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!confirm(`Are you sure you want to delete "${title}"?`)) return;

    try {
      await deleteMutation.mutateAsync(id);
      showToast('success', `Deleted "${title}"`);
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Delete failed';
      showToast('error', `Failed to delete: ${errorMessage}`);
    }
  };

  const handleDeleteAll = async () => {
    if (!documents || documents.length === 0) return;
    if (!confirm('Are you sure you want to delete ALL documents? This cannot be undone.'))
      return;

    try {
      for (const doc of documents) {
        await deleteMutation.mutateAsync(doc.id);
      }
      showToast('success', 'All documents deleted');
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Delete failed';
      showToast('error', `Failed to delete all: ${errorMessage}`);
    }
  };

  const handleDocumentClick = (id: string) => {
    setSelectedDocumentId(id);
  };

  const handleCloseModal = () => {
    setSelectedDocumentId(null);
  };

  return (
    <div className="page">
      <div className="page-header">
        <div className="page-header-text">
          <h1>Indexed Documents</h1>
          <p>Manage your indexed document collection</p>
        </div>
        <button className="btn btn-secondary" onClick={() => refetch()}>
          <RefreshCw size={16} />
          Refresh
        </button>
      </div>

      <div className="documents-container">
        <div className="documents-toolbar">
          <div className="search-filter">
            <input
              type="text"
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              placeholder="Filter documents..."
            />
          </div>
          <div className="documents-actions">
            <button
              className="btn btn-danger"
              onClick={handleDeleteAll}
              disabled={!status?.totalDocuments}
            >
              Delete All
            </button>
          </div>
        </div>

        <div className="documents-list">
          {isLoading && (
            <div className="loading-overlay">
              <div className="spinner"></div>
            </div>
          )}

          {!isLoading && filteredDocuments.length === 0 && (
            <div className="empty-state">
              <div className="empty-icon">Docs</div>
              <h3>No Documents</h3>
              <p>Upload documents to get started with semantic search</p>
            </div>
          )}

          {filteredDocuments.map((doc) => (
            <div
              key={doc.id}
              className="document-item clickable"
              onClick={() => handleDocumentClick(doc.id)}
            >
              <div className="document-icon">{getFileIcon(doc.title)}</div>
              <div className="document-info">
                <div className="document-title">{doc.title}</div>
                <div className="document-meta">
                  <span>{doc.chunkCount} chunks</span>
                  <span>{formatDate(doc.createdAt)}</span>
                </div>
              </div>
              <div className="document-actions">
                <button
                  className="btn btn-primary btn-sm"
                  onClick={(e) => {
                    e.stopPropagation();
                    handleDocumentClick(doc.id);
                  }}
                  title="View Details"
                >
                  <Eye size={14} />
                  View
                </button>
                <button
                  className="btn btn-danger btn-sm"
                  onClick={(e) => handleDelete(doc.id, doc.title, e)}
                  disabled={deleteMutation.isPending}
                >
                  <Trash2 size={14} />
                  Delete
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>

      <DocumentDetailModal
        documentId={selectedDocumentId}
        onClose={handleCloseModal}
      />
    </div>
  );
}
