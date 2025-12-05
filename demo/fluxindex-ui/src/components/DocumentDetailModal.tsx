import { useState, useCallback, useMemo } from 'react';
import { X, FileText, Layers, MessageSquare, Info, ChevronDown, ChevronRight, Save, RotateCcw, Loader2, Sparkles } from 'lucide-react';
import Editor from '@monaco-editor/react';
import { useDocumentDetail, useBatchRememorize, useGenerateDocumentQA } from '../hooks/useApi';
import { useToast } from '../components/Toast';
import type { ChunkDetail, QAItem, ChunkUpdateRequest } from '../types/api';
import './DocumentDetailModal.css';

interface DocumentDetailModalProps {
  documentId: string | null;
  onClose: () => void;
}

type TabType = 'extract' | 'chunks' | 'qa' | 'metadata';

interface EditedChunk {
  content: string;
  qa: QAItem[];
}

export default function DocumentDetailModal({
  documentId,
  onClose,
}: DocumentDetailModalProps) {
  const [activeTab, setActiveTab] = useState<TabType>('extract');
  const [expandedChunks, setExpandedChunks] = useState<Set<number>>(new Set());
  const [selectedChunkIndex, setSelectedChunkIndex] = useState<number>(0);
  const [editedChunks, setEditedChunks] = useState<Map<string, EditedChunk>>(new Map());
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);

  const { data, isLoading, error, refetch } = useDocumentDetail(documentId);
  const batchRememorizeMutation = useBatchRememorize();
  const generateQAMutation = useGenerateDocumentQA();
  const { showToast } = useToast();

  const currentChunk = useMemo(() => {
    if (!data?.chunks || selectedChunkIndex >= data.chunks.length) return null;
    return data.chunks[selectedChunkIndex];
  }, [data?.chunks, selectedChunkIndex]);

  const getEditedContent = useCallback((chunk: ChunkDetail): string => {
    const edited = editedChunks.get(chunk.id);
    return edited?.content ?? chunk.content;
  }, [editedChunks]);

  const getEditedQA = useCallback((chunk: ChunkDetail): QAItem[] => {
    const edited = editedChunks.get(chunk.id);
    return edited?.qa ?? chunk.qa ?? [];
  }, [editedChunks]);

  const handleContentChange = useCallback((chunkId: string, newContent: string | undefined) => {
    if (!newContent) return;
    const chunk = data?.chunks.find(c => c.id === chunkId);
    if (!chunk) return;

    setEditedChunks(prev => {
      const newMap = new Map(prev);
      const existing = newMap.get(chunkId);
      newMap.set(chunkId, {
        content: newContent,
        qa: existing?.qa ?? chunk.qa ?? []
      });
      return newMap;
    });
    setHasUnsavedChanges(true);
  }, [data?.chunks]);

  const handleQAChange = useCallback((chunkId: string, newQAJson: string | undefined) => {
    if (!newQAJson) return;
    const chunk = data?.chunks.find(c => c.id === chunkId);
    if (!chunk) return;

    try {
      const parsed = JSON.parse(newQAJson) as QAItem[];
      setEditedChunks(prev => {
        const newMap = new Map(prev);
        const existing = newMap.get(chunkId);
        newMap.set(chunkId, {
          content: existing?.content ?? chunk.content,
          qa: parsed
        });
        return newMap;
      });
      setHasUnsavedChanges(true);
    } catch {
      // Invalid JSON, ignore
    }
  }, [data?.chunks]);

  const handleRememorize = useCallback(async () => {
    if (!documentId || editedChunks.size === 0) return;

    const updates: ChunkUpdateRequest[] = Array.from(editedChunks.entries()).map(([chunkId, edited]) => ({
      chunkId,
      content: edited.content,
      qa: edited.qa.length > 0 ? edited.qa : undefined
    }));

    try {
      const result = await batchRememorizeMutation.mutateAsync({
        documentId,
        request: { updates }
      });

      showToast('success', `Rememorized ${result.updatedCount} chunks`);
      setEditedChunks(new Map());
      setHasUnsavedChanges(false);
      refetch();
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Rememorize failed';
      showToast('error', errorMessage);
    }
  }, [documentId, editedChunks, batchRememorizeMutation, showToast, refetch]);

  const handleResetChanges = useCallback(() => {
    setEditedChunks(new Map());
    setHasUnsavedChanges(false);
  }, []);

  const handleGenerateQA = useCallback(async () => {
    if (!documentId) return;

    try {
      const result = await generateQAMutation.mutateAsync(documentId);

      if (result.errors.length > 0) {
        showToast('warning', `Generated ${result.totalQAPairs} Q&A pairs with ${result.errors.length} errors`);
      } else {
        showToast('success', `Generated ${result.totalQAPairs} Q&A pairs for ${result.processedChunks} chunks`);
      }
      refetch();
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'QA generation failed';
      showToast('error', errorMessage);
    }
  }, [documentId, generateQAMutation, showToast, refetch]);

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

  if (!documentId) return null;

  const renderChunkListItem = (chunk: ChunkDetail, index: number) => {
    const isExpanded = expandedChunks.has(index);
    const isEdited = editedChunks.has(chunk.id);

    return (
      <div key={chunk.id} className={`chunk-item ${isEdited ? 'edited' : ''}`}>
        <div className="chunk-header" onClick={() => toggleChunk(index)}>
          <span className="chunk-toggle">
            {isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
          </span>
          <span className="chunk-index">Chunk {chunk.index + 1}</span>
          <span className="chunk-tokens">{chunk.tokenCount} tokens</span>
          {isEdited && <span className="chunk-edited-badge">Modified</span>}
          {chunk.chunkMetadata?.contentType && (
            <span className="chunk-type">{chunk.chunkMetadata.contentType}</span>
          )}
        </div>
        {isExpanded && (
          <div className="chunk-body">
            <div className="chunk-content">
              <pre>{getEditedContent(chunk)}</pre>
            </div>
            {chunk.qa && chunk.qa.length > 0 && (
              <div className="chunk-qa-preview">
                <span className="qa-badge">{chunk.qa.length} Q&A pairs</span>
              </div>
            )}
          </div>
        )}
      </div>
    );
  };

  const renderChunkEditor = () => {
    if (!currentChunk) return <div className="empty-state">Select a chunk to edit</div>;

    const editedContent = getEditedContent(currentChunk);
    const isEdited = editedChunks.has(currentChunk.id);

    return (
      <div className="chunk-editor-container">
        <div className="chunk-selector">
          <select
            value={selectedChunkIndex}
            onChange={(e) => setSelectedChunkIndex(Number(e.target.value))}
          >
            {data?.chunks.map((chunk, index) => (
              <option key={chunk.id} value={index}>
                Chunk {chunk.index + 1} - {chunk.tokenCount} tokens
                {editedChunks.has(chunk.id) ? ' (modified)' : ''}
              </option>
            ))}
          </select>
          {isEdited && <span className="edited-indicator">● Modified</span>}
        </div>

        <div className="editor-wrapper">
          <Editor
            height="400px"
            defaultLanguage="markdown"
            value={editedContent}
            onChange={(value) => handleContentChange(currentChunk.id, value)}
            theme="vs-dark"
            options={{
              minimap: { enabled: false },
              wordWrap: 'on',
              lineNumbers: 'on',
              fontSize: 13,
              scrollBeyondLastLine: false,
              automaticLayout: true,
            }}
          />
        </div>

        <div className="chunk-meta-info">
          {currentChunk.chunkMetadata && (
            <>
              {currentChunk.chunkMetadata.keywords.length > 0 && (
                <div className="meta-tags">
                  <span className="meta-label">Keywords:</span>
                  {currentChunk.chunkMetadata.keywords.map((kw, i) => (
                    <span key={i} className="tag keyword">{kw}</span>
                  ))}
                </div>
              )}
              {currentChunk.chunkMetadata.entities.length > 0 && (
                <div className="meta-tags">
                  <span className="meta-label">Entities:</span>
                  {currentChunk.chunkMetadata.entities.map((ent, i) => (
                    <span key={i} className="tag entity">{ent}</span>
                  ))}
                </div>
              )}
            </>
          )}
        </div>
      </div>
    );
  };

  const renderQAEditor = () => {
    if (!currentChunk) return <div className="empty-state">Select a chunk to edit Q&A</div>;

    const editedQA = getEditedQA(currentChunk);
    const qaJson = JSON.stringify(editedQA, null, 2);

    return (
      <div className="qa-editor-container">
        <div className="chunk-selector">
          <select
            value={selectedChunkIndex}
            onChange={(e) => setSelectedChunkIndex(Number(e.target.value))}
          >
            {data?.chunks.map((chunk, index) => (
              <option key={chunk.id} value={index}>
                Chunk {chunk.index + 1}
                {(chunk.qa?.length ?? 0) > 0 ? ` - ${chunk.qa?.length} Q&A` : ' - No Q&A'}
                {editedChunks.has(chunk.id) ? ' (modified)' : ''}
              </option>
            ))}
          </select>
        </div>

        <div className="qa-help-text">
          Edit Q&A pairs in JSON format. Each item should have "question" and "answer" fields.
        </div>

        <div className="editor-wrapper">
          <Editor
            height="400px"
            defaultLanguage="json"
            value={qaJson}
            onChange={(value) => handleQAChange(currentChunk.id, value)}
            theme="vs-dark"
            options={{
              minimap: { enabled: false },
              wordWrap: 'on',
              lineNumbers: 'on',
              fontSize: 13,
              scrollBeyondLastLine: false,
              automaticLayout: true,
              formatOnPaste: true,
              formatOnType: true,
            }}
          />
        </div>

        <div className="qa-preview">
          <h4>Preview</h4>
          {editedQA.length === 0 ? (
            <p className="no-qa">No Q&A pairs defined</p>
          ) : (
            <div className="qa-list">
              {editedQA.map((qa, index) => (
                <div key={index} className="qa-item">
                  <div className="qa-question">
                    <span className="qa-label">Q:</span> {qa.question}
                  </div>
                  <div className="qa-answer">
                    <span className="qa-label">A:</span> {qa.answer}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-container large" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>{data?.title || 'Document Detail'}</h2>
          <div className="modal-header-actions">
            {hasUnsavedChanges && (
              <>
                <button
                  className="btn btn-secondary"
                  onClick={handleResetChanges}
                  disabled={batchRememorizeMutation.isPending}
                >
                  <RotateCcw size={16} />
                  Reset
                </button>
                <button
                  className="btn btn-primary"
                  onClick={handleRememorize}
                  disabled={batchRememorizeMutation.isPending}
                >
                  {batchRememorizeMutation.isPending ? (
                    <Loader2 size={16} className="animate-spin" />
                  ) : (
                    <Save size={16} />
                  )}
                  Rememorize
                </button>
              </>
            )}
            <button className="modal-close" onClick={onClose}>
              <X size={20} />
            </button>
          </div>
        </div>

        <div className="modal-tabs">
          <button
            className={`tab ${activeTab === 'extract' ? 'active' : ''}`}
            onClick={() => setActiveTab('extract')}
          >
            <FileText size={16} />
            Extract
          </button>
          <button
            className={`tab ${activeTab === 'chunks' ? 'active' : ''}`}
            onClick={() => setActiveTab('chunks')}
          >
            <Layers size={16} />
            Chunks ({data?.totalChunks || 0})
          </button>
          <button
            className={`tab ${activeTab === 'qa' ? 'active' : ''}`}
            onClick={() => setActiveTab('qa')}
          >
            <MessageSquare size={16} />
            Q&A
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
            <div className="chunks-content split-view">
              <div className="chunks-list-panel">
                <div className="chunks-toolbar">
                  <button onClick={expandAll}>Expand All</button>
                  <button onClick={collapseAll}>Collapse All</button>
                </div>
                <div className="chunks-list">
                  {data.chunks.map((chunk, index) => renderChunkListItem(chunk, index))}
                </div>
              </div>
              <div className="chunk-editor-panel">
                <h3>Edit Chunk Content</h3>
                {renderChunkEditor()}
              </div>
            </div>
          )}

          {data && activeTab === 'qa' && (
            <div className="qa-content">
              <div className="qa-toolbar">
                <button
                  className="btn btn-accent"
                  onClick={handleGenerateQA}
                  disabled={generateQAMutation.isPending}
                >
                  {generateQAMutation.isPending ? (
                    <Loader2 size={16} className="animate-spin" />
                  ) : (
                    <Sparkles size={16} />
                  )}
                  Auto-Generate QA
                </button>
                {generateQAMutation.isPending && (
                  <span className="qa-status">Generating Q&A pairs with LLM...</span>
                )}
              </div>
              {renderQAEditor()}
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
