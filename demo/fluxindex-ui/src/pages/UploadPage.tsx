import { useState, useRef, useCallback } from 'react';
import { FileText, X } from 'lucide-react';
import { useUploadFile } from '../hooks/useApi';
import { useToast } from '../components/Toast';
import './UploadPage.css';

interface UploadItem {
  id: string;
  file: File;
  status: 'uploading' | 'processing' | 'success' | 'error';
  progress: number;
  message: string;
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

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  return `${Math.floor(ms / 60000)}m ${Math.floor((ms % 60000) / 1000)}s`;
}

export default function UploadPage() {
  const [uploadItems, setUploadItems] = useState<UploadItem[]>([]);
  const [isDragOver, setIsDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const uploadMutation = useUploadFile();
  const { showToast } = useToast();

  const processFile = useCallback(
    async (file: File) => {
      const id = `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

      setUploadItems((prev) => [
        ...prev,
        {
          id,
          file,
          status: 'uploading',
          progress: 10,
          message: 'Uploading...',
        },
      ]);

      try {
        setUploadItems((prev) =>
          prev.map((item) =>
            item.id === id
              ? { ...item, progress: 30, message: 'Processing document...' }
              : item
          )
        );

        const result = await uploadMutation.mutateAsync({
          file,
          onProgress: (progress) => {
            setUploadItems((prev) =>
              prev.map((item) =>
                item.id === id ? { ...item, progress: Math.min(progress, 80) } : item
              )
            );
          },
        });

        setUploadItems((prev) =>
          prev.map((item) =>
            item.id === id
              ? {
                  ...item,
                  status: result.success ? 'success' : 'error',
                  progress: 100,
                  message: result.success
                    ? `${result.chunkCount} chunks indexed (${formatDuration(result.processingTimeMs)})`
                    : result.message,
                }
              : item
          )
        );

        if (result.success) {
          showToast('success', `Successfully indexed "${file.name}"`);
        } else {
          showToast('error', `Failed to index "${file.name}": ${result.message}`);
        }
      } catch (error) {
        const errorMessage = error instanceof Error ? error.message : 'Upload failed';
        setUploadItems((prev) =>
          prev.map((item) =>
            item.id === id
              ? { ...item, status: 'error', progress: 100, message: errorMessage }
              : item
          )
        );
        showToast('error', `Upload failed: ${errorMessage}`);
      }
    },
    [uploadMutation, showToast]
  );

  const handleFiles = useCallback(
    (files: FileList | null) => {
      if (!files) return;
      Array.from(files).forEach(processFile);
    },
    [processFile]
  );

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setIsDragOver(false);
      handleFiles(e.dataTransfer.files);
    },
    [handleFiles]
  );

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback(() => {
    setIsDragOver(false);
  }, []);

  const removeItem = useCallback((id: string) => {
    setUploadItems((prev) => prev.filter((item) => item.id !== id));
  }, []);

  return (
    <div className="page">
      <div className="page-header">
        <h1>Upload Documents</h1>
        <p>Upload and index documents for semantic search</p>
      </div>

      <div className="upload-container">
        <div
          className={`upload-area ${isDragOver ? 'dragover' : ''}`}
          onClick={() => fileInputRef.current?.click()}
          onDrop={handleDrop}
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
        >
          <input
            ref={fileInputRef}
            type="file"
            accept=".pdf,.docx,.doc,.txt,.md,.html,.htm,.json,.csv,.xml"
            multiple
            hidden
            onChange={(e) => {
              handleFiles(e.target.files);
              e.target.value = '';
            }}
          />
          <div className="upload-content">
            <div className="upload-icon">
              <FileText size={48} />
            </div>
            <h3>Drop files here</h3>
            <p>
              or <button className="link-btn">browse files</button>
            </p>
            <div className="supported-formats">
              <span className="format-tag">PDF</span>
              <span className="format-tag">DOCX</span>
              <span className="format-tag">TXT</span>
              <span className="format-tag">MD</span>
              <span className="format-tag">HTML</span>
              <span className="format-tag">JSON</span>
              <span className="format-tag">CSV</span>
            </div>
          </div>
        </div>

        <div className="upload-queue">
          {uploadItems.map((item) => (
            <div key={item.id} className={`upload-item ${item.status}`}>
              <div className="upload-item-icon">{getFileIcon(item.file.name)}</div>
              <div className="upload-item-info">
                <div className="upload-item-name">{item.file.name}</div>
                <div className="upload-item-status">
                  {item.status === 'success' && '✓ '}
                  {item.status === 'error' && '✗ '}
                  {item.message}
                </div>
                <div className="upload-item-progress">
                  <div
                    className="upload-item-progress-fill"
                    style={{ width: `${item.progress}%` }}
                  />
                </div>
              </div>
              <button className="upload-item-action" onClick={() => removeItem(item.id)}>
                <X size={16} />
              </button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
