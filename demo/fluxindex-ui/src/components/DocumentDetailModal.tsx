import { useState } from 'react';
import { X, FileText, Layers, Info, ChevronDown, ChevronRight } from 'lucide-react';
import { useDocumentDetail } from '../hooks/useApi';
import type { ChunkDetail } from '../types/api';
import './DocumentDetailModal.css';

interface DocumentDetailModalProps {
  documentId: string | null;
  onClose: () => void;
}

type TabType = 'extract' | 'chunks' | 'metadata';

export default function DocumentDetailModal({
  documentId,
  onClose,
}: DocumentDetailModalProps) {
  const [activeTab, setActiveTab] = useState<TabType>('extract');
  const [expandedChunks, setExpandedChunks] = useState<Set<number>>(new Set());
  const { data, isLoading, error } = useDocumentDetail(documentId);

  if (!documentId) return null;

  const toggleChunk = (index: number) => {
    setExpandedChunks((prev) => {
      const newSet = new Set(prev);
      if (newSet.has(index)) {
        newSet.delete(index);
      } else {
        newSet.add(index);
      }
      return newSet;
    });
  };

  const expandAll = () => {
    if (data?.chunks) {
      setExpandedChunks(new Set(data.chunks.map((_, i) => i)));
    }
  };

  const collapseAll = () => {
    setExpandedChunks(new Set());
  };

  const renderChunk = (chunk: ChunkDetail, index: number) => {
    const isExpanded = expandedChunks.has(index);
    const hasMetadata =
      chunk.chunkMetadata &&
      (chunk.chunkMetadata.keywords.length > 0 ||
        chunk.chunkMetadata.entities.length > 0 ||
        chunk.chunkMetadata.topics.length > 0);

    return (
      <div key={chunk.id} className="chunk-item">
        <div className="chunk-header" onClick={() => toggleChunk(index)}>
          <span className="chunk-toggle">
            {isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
          </span>
          <span className="chunk-index">Chunk {chunk.index + 1}</span>
          <span className="chunk-tokens">{chunk.tokenCount} tokens</span>
          {chunk.chunkMetadata?.contentType && (
            <span className="chunk-type">{chunk.chunkMetadata.contentType}</span>
          )}
        </div>
        {isExpanded && (
          <div className="chunk-body">
            <div className="chunk-content">
              <pre>{chunk.content}</pre>
            </div>
            {hasMetadata && (
              <div className="chunk-meta-section">
                {chunk.chunkMetadata!.keywords.length > 0 && (
                  <div className="meta-group">
                    <span className="meta-label">Keywords:</span>
                    <div className="meta-tags">
                      {chunk.chunkMetadata!.keywords.map((kw, i) => (
                        <span key={i} className="tag keyword">
                          {kw}
                        </span>
                      ))}
                    </div>
                  </div>
                )}
                {chunk.chunkMetadata!.entities.length > 0 && (
                  <div className="meta-group">
                    <span className="meta-label">Entities:</span>
                    <div className="meta-tags">
                      {chunk.chunkMetadata!.entities.map((ent, i) => (
                        <span key={i} className="tag entity">
                          {ent}
                        </span>
                      ))}
                    </div>
                  </div>
                )}
                {chunk.chunkMetadata!.topics.length > 0 && (
                  <div className="meta-group">
                    <span className="meta-label">Topics:</span>
                    <div className="meta-tags">
                      {chunk.chunkMetadata!.topics.map((topic, i) => (
                        <span key={i} className="tag topic">
                          {topic}
                        </span>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}
            {chunk.quality && (
              <div className="chunk-quality">
                <span className="quality-item">
                  Completeness: {(chunk.quality.contentCompleteness * 100).toFixed(0)}%
                </span>
                <span className="quality-item">
                  Density: {(chunk.quality.informationDensity * 100).toFixed(0)}%
                </span>
                <span className="quality-item">
                  Coherence: {(chunk.quality.coherence * 100).toFixed(0)}%
                </span>
                <span className="quality-item">
                  Uniqueness: {(chunk.quality.uniqueness * 100).toFixed(0)}%
                </span>
              </div>
            )}
          </div>
        )}
      </div>
    );
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-container" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>{data?.title || 'Document Detail'}</h2>
          <button className="modal-close" onClick={onClose}>
            <X size={20} />
          </button>
        </div>

        <div className="modal-tabs">
          <button
            className={`tab ${activeTab === 'extract' ? 'active' : ''}`}
            onClick={() => setActiveTab('extract')}
          >
            <FileText size={16} />
            Extracted Text
          </button>
          <button
            className={`tab ${activeTab === 'chunks' ? 'active' : ''}`}
            onClick={() => setActiveTab('chunks')}
          >
            <Layers size={16} />
            Chunks ({data?.totalChunks || 0})
          </button>
          <button
            className={`tab ${activeTab === 'metadata' ? 'active' : ''}`}
            onClick={() => setActiveTab('metadata')}
          >
            <Info size={16} />
            Metadata
          </button>
        </div>

        <div className="modal-content">
          {isLoading && (
            <div className="loading-state">
              <div className="spinner"></div>
              <span>Loading document details...</span>
            </div>
          )}

          {error && (
            <div className="error-state">
              <span>Failed to load document details</span>
            </div>
          )}

          {data && activeTab === 'extract' && (
            <div className="extract-content">
              <pre>{data.fullContent}</pre>
            </div>
          )}

          {data && activeTab === 'chunks' && (
            <div className="chunks-content">
              <div className="chunks-toolbar">
                <button onClick={expandAll}>Expand All</button>
                <button onClick={collapseAll}>Collapse All</button>
              </div>
              <div className="chunks-list">
                {data.chunks.map((chunk, index) => renderChunk(chunk, index))}
              </div>
            </div>
          )}

          {data && activeTab === 'metadata' && (
            <div className="metadata-content">
              <div className="metadata-section">
                <h3>Document Information</h3>
                <div className="metadata-grid">
                  <div className="metadata-item">
                    <span className="label">Document ID</span>
                    <span className="value">{data.id}</span>
                  </div>
                  <div className="metadata-item">
                    <span className="label">Title</span>
                    <span className="value">{data.title}</span>
                  </div>
                  <div className="metadata-item">
                    <span className="label">Created At</span>
                    <span className="value">
                      {new Date(data.createdAt).toLocaleString()}
                    </span>
                  </div>
                  <div className="metadata-item">
                    <span className="label">Total Chunks</span>
                    <span className="value">{data.totalChunks}</span>
                  </div>
                </div>
              </div>

              {data.chunks.length > 0 && data.chunks[0].chunkMetadata && (
                <div className="metadata-section">
                  <h3>First Chunk Analysis</h3>
                  <div className="metadata-grid">
                    <div className="metadata-item">
                      <span className="label">Language</span>
                      <span className="value">
                        {data.chunks[0].chunkMetadata.language}
                      </span>
                    </div>
                    <div className="metadata-item">
                      <span className="label">Content Type</span>
                      <span className="value">
                        {data.chunks[0].chunkMetadata.contentType}
                      </span>
                    </div>
                    <div className="metadata-item">
                      <span className="label">Token Count</span>
                      <span className="value">
                        {data.chunks[0].chunkMetadata.tokenCount}
                      </span>
                    </div>
                    <div className="metadata-item">
                      <span className="label">Sentence Count</span>
                      <span className="value">
                        {data.chunks[0].chunkMetadata.sentenceCount}
                      </span>
                    </div>
                    <div className="metadata-item">
                      <span className="label">Importance Score</span>
                      <span className="value">
                        {(data.chunks[0].chunkMetadata.importanceScore * 100).toFixed(0)}%
                      </span>
                    </div>
                    <div className="metadata-item">
                      <span className="label">Readability Score</span>
                      <span className="value">
                        {data.chunks[0].chunkMetadata.readabilityScore.toFixed(2)}
                      </span>
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
