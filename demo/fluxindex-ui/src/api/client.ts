/**
 * FluxIndex API Client
 */
import axios from 'axios';
import type {
  StatusResponse,
  DocumentInfo,
  DocumentDetailResponse,
  UploadResponse,
  SearchRequest,
  SearchResponse,
  McpSearchRequest,
  McpSearchResponse,
  RememorizeRequest,
  RememorizeResponse,
  BatchRememorizeRequest,
  BatchRememorizeResponse,
  GenerateQAResponse,
  LogEntry,
  DocumentEvaluationResponse,
} from '../types/api';

const API_BASE = import.meta.env.VITE_API_BASE || '';

const api = axios.create({
  baseURL: API_BASE,
  headers: {
    'Content-Type': 'application/json',
  },
});

export const fluxIndexApi = {
  // Status
  getStatus: async (): Promise<StatusResponse> => {
    const { data } = await api.get<StatusResponse>('/api/status');
    return data;
  },

  // Documents
  getDocuments: async (): Promise<DocumentInfo[]> => {
    const { data } = await api.get<DocumentInfo[]>('/api/documents');
    return data;
  },

  deleteDocument: async (id: string): Promise<void> => {
    await api.delete(`/api/documents/${id}`);
  },

  getDocumentDetail: async (id: string): Promise<DocumentDetailResponse> => {
    const { data } = await api.get<DocumentDetailResponse>(`/api/documents/${id}`);
    return data;
  },

  // Upload
  uploadFile: async (
    file: File,
    onProgress?: (progress: number) => void
  ): Promise<UploadResponse> => {
    const formData = new FormData();
    formData.append('file', file);

    const { data } = await api.post<UploadResponse>('/api/upload', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
      onUploadProgress: (progressEvent) => {
        if (onProgress && progressEvent.total) {
          const progress = Math.round(
            (progressEvent.loaded * 100) / progressEvent.total
          );
          onProgress(progress);
        }
      },
    });
    return data;
  },

  // Search
  search: async (request: SearchRequest): Promise<SearchResponse> => {
    const { data } = await api.post<SearchResponse>('/api/search', request);
    return data;
  },

  // MCP
  mcpSearch: async (request: McpSearchRequest): Promise<McpSearchResponse> => {
    const { data } = await api.post<McpSearchResponse>(
      '/api/mcp/search',
      request
    );
    return data;
  },

  // Rememorize - Update chunk content and regenerate embedding
  rememorizeChunk: async (chunkId: string, request: RememorizeRequest): Promise<RememorizeResponse> => {
    const { data } = await api.put<RememorizeResponse>(
      `/api/chunks/${chunkId}/rememorize`,
      request
    );
    return data;
  },

  // Batch rememorize - Update multiple chunks
  batchRememorize: async (documentId: string, request: BatchRememorizeRequest): Promise<BatchRememorizeResponse> => {
    const { data } = await api.put<BatchRememorizeResponse>(
      `/api/documents/${documentId}/rememorize`,
      request
    );
    return data;
  },

  // Generate QA - Auto-generate QA pairs for all chunks in a document
  generateDocumentQA: async (documentId: string): Promise<GenerateQAResponse> => {
    const { data } = await api.post<GenerateQAResponse>(
      `/api/documents/${documentId}/generate-qa`
    );
    return data;
  },

  // Logs - Get process logs
  getLogs: async (limit?: number, category?: string, level?: string): Promise<LogEntry[]> => {
    const params = new URLSearchParams();
    if (limit) params.append('limit', limit.toString());
    if (category) params.append('category', category);
    if (level) params.append('level', level);
    const { data } = await api.get<LogEntry[]>(`/api/logs?${params.toString()}`);
    return data;
  },

  // Logs - Clear all logs
  clearLogs: async (): Promise<void> => {
    await api.delete('/api/logs');
  },

  // Evaluate QA - Evaluate all QA pairs in a document
  evaluateDocumentQA: async (documentId: string): Promise<DocumentEvaluationResponse> => {
    const { data } = await api.post<DocumentEvaluationResponse>(
      `/api/documents/${documentId}/evaluate-qa`
    );
    return data;
  },
};

export default fluxIndexApi;
