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
};

export default fluxIndexApi;
