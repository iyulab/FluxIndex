import axios from 'axios'

const api = axios.create({
  baseURL: '/api/v1',
  headers: {
    'Content-Type': 'application/json',
  },
})

// Add API key to requests
api.interceptors.request.use((config) => {
  const apiKey = localStorage.getItem('fluxindex-api-key')
  if (apiKey) {
    config.headers['X-API-Key'] = apiKey
  }
  return config
})

// Handle errors globally
api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      // Handle unauthorized - could redirect to login or show modal
      console.error('Unauthorized access - API key may be invalid')
    }
    return Promise.reject(error)
  }
)

export default api

// Type definitions
export interface ApiResponse<T> {
  success: boolean
  data?: T
  message?: string
  errors?: ApiError[]
  metadata?: ApiMetadata
}

export interface ApiError {
  code: string
  message: string
  field?: string
}

export interface ApiMetadata {
  page?: number
  pageSize?: number
  totalCount?: number
  totalPages?: number
  hasNextPage?: boolean
  hasPreviousPage?: boolean
  executionTimeMs?: number
}

// Collection types
export interface Collection {
  id: string
  name: string
  description?: string
  settings: CollectionSettings
  documentCount: number
  createdAt: string
  updatedAt: string
}

export interface CollectionSettings {
  chunkSize: number
  chunkOverlap: number
  chunkingStrategy: string
  enableQAGeneration: boolean
  enableEnrichment: boolean
  customSettings: Record<string, unknown>
}

// Document types
export interface Document {
  id: string
  collectionId?: string
  title: string
  sourceType?: string
  sourcePath?: string
  contentHash?: string
  fileSize?: number
  status: string
  chunkCount: number
  metadata: Record<string, unknown>
  createdAt: string
  updatedAt: string
  indexedAt?: string
}

// Search types
export interface SearchRequest {
  query: string
  collectionId?: string
  topK?: number
  minScore?: number
  mode?: 'Vector' | 'Keyword' | 'Hybrid'
  includeContent?: boolean
  includeMetadata?: boolean
}

export interface SearchResult {
  chunkId: string
  documentId: string
  documentTitle: string
  chunkIndex: number
  content?: string
  score: number
  vectorScore?: number
  keywordScore?: number
  metadata?: Record<string, unknown>
  highlights?: string[]
}

export interface SearchResponse {
  query: string
  results: SearchResult[]
  totalResults: number
  executionTimeMs: number
  mode: string
}

// Analytics types
export interface SystemStats {
  totalDocuments: number
  totalChunks: number
  totalCollections: number
  totalStorageBytes: number
  indexedDocuments: number
  pendingDocuments: number
  failedDocuments: number
}

// API functions
export const collectionsApi = {
  getAll: () => api.get<ApiResponse<Collection[]>>('/collections'),
  getById: (id: string) => api.get<ApiResponse<Collection>>(`/collections/${id}`),
  create: (data: Partial<Collection>) => api.post<ApiResponse<Collection>>('/collections', data),
  update: (id: string, data: Partial<Collection>) => api.put<ApiResponse<Collection>>(`/collections/${id}`, data),
  delete: (id: string) => api.delete<ApiResponse<void>>(`/collections/${id}`),
}

export const documentsApi = {
  getAll: (params?: { page?: number; pageSize?: number; collectionId?: string; status?: string }) =>
    api.get<ApiResponse<Document[]>>('/documents', { params }),
  getById: (id: string) => api.get<ApiResponse<Document>>(`/documents/${id}`),
  upload: (data: FormData) => api.post<ApiResponse<{ documentId: string; jobId: string }>>('/documents/upload', data, {
    headers: { 'Content-Type': 'multipart/form-data' },
  }),
  delete: (id: string) => api.delete<ApiResponse<void>>(`/documents/${id}`),
}

export const searchApi = {
  search: (data: SearchRequest) => api.post<ApiResponse<SearchResponse>>('/search', data),
}

export const analyticsApi = {
  getSystemStats: () => api.get<ApiResponse<SystemStats>>('/analytics/system'),
}
