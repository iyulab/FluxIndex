import axios from 'axios'

const api = axios.create({
  baseURL: '/api/v1',
  headers: {
    'Content-Type': 'application/json',
  },
})

// Add API key to requests (only if valid key exists)
api.interceptors.request.use((config) => {
  const apiKey = localStorage.getItem('fluxindex-api-key')
  // Only send API key if it's a non-empty string
  if (apiKey && apiKey.trim().length > 0) {
    config.headers['X-API-Key'] = apiKey
  }
  // In development, if no API key is set, the backend will auto-grant Admin access
  return config
})

// Handle errors globally
api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      // API key is invalid - clear it so next requests use dev bypass
      const currentKey = localStorage.getItem('fluxindex-api-key')
      if (currentKey) {
        console.warn('API key invalid, clearing stored key. You may need to generate a new one.')
        localStorage.removeItem('fluxindex-api-key')
      }
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

export interface DocumentChunk {
  id: string
  chunkIndex: number
  content: string
  tokenCount: number
  startPosition: number
  endPosition: number
  metadata: Record<string, unknown>
}

export interface DocumentDetail extends Document {
  chunks: DocumentChunk[]
}

export interface UploadDocumentResponse {
  documentId: string
  jobId?: string
  status: string
  message: string
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

export interface TopQuery {
  query: string
  count: number
  averageExecutionTimeMs: number
}

export interface SearchTrend {
  date: string
  searchCount: number
  averageExecutionTimeMs: number
}

export interface SearchAnalytics {
  totalSearches: number
  averageExecutionTimeMs: number
  averageResultCount: number
  topQueries: TopQuery[]
  dailyTrends: SearchTrend[]
}

export interface DocumentTypeStats {
  sourceType: string
  count: number
  totalSizeBytes: number
}

export interface DocumentStatusStats {
  status: string
  count: number
}

export interface DocumentTrend {
  date: string
  uploadCount: number
  indexedCount: number
}

export interface DocumentAnalytics {
  bySourceType: DocumentTypeStats[]
  byStatus: DocumentStatusStats[]
  dailyUploads: DocumentTrend[]
}

export interface SemanticCacheEntry {
  query: string
  response: string
  similarity: number
  cachedAt: string
}

// API Key types
export interface ApiKey {
  id: string
  name: string
  keyPrefix: string
  role: string
  isActive: boolean
  lastUsedAt?: string
  expiresAt?: string
  rateLimitPerMinute?: number
  rateLimitPerDay?: number
  createdAt: string
}

export interface CreateApiKeyRequest {
  name: string
  role?: string
  expiresAt?: string
  rateLimitPerMinute?: number
  rateLimitPerDay?: number
}

export interface CreateApiKeyResponse {
  id: string
  name: string
  rawKey: string
  keyPrefix: string
  role: string
  expiresAt?: string
  message: string
}

// API functions
export const collectionsApi = {
  getAll: (params?: { page?: number; pageSize?: number }) =>
    api.get<ApiResponse<Collection[]>>('/collections', { params }),
  getById: (id: string) => api.get<ApiResponse<Collection>>(`/collections/${id}`),
  getByName: (name: string) => api.get<ApiResponse<Collection>>(`/collections/by-name/${encodeURIComponent(name)}`),
  create: (data: { name: string; description?: string; settings?: Partial<CollectionSettings> }) =>
    api.post<ApiResponse<Collection>>('/collections', data),
  update: (id: string, data: { name: string; description?: string; settings?: Partial<CollectionSettings> }) =>
    api.put<ApiResponse<Collection>>(`/collections/${id}`, data),
  delete: (id: string) => api.delete<ApiResponse<void>>(`/collections/${id}`),
}

export const documentsApi = {
  getAll: (params?: { page?: number; pageSize?: number; collectionId?: string; status?: string }) =>
    api.get<ApiResponse<Document[]>>('/documents', { params }),
  getById: (id: string) => api.get<ApiResponse<Document>>(`/documents/${id}`),
  getDetail: (id: string) => api.get<ApiResponse<DocumentDetail>>(`/documents/${id}/detail`),
  upload: (data: FormData) =>
    api.post<ApiResponse<UploadDocumentResponse>>('/documents/upload', data, {
      headers: { 'Content-Type': 'multipart/form-data' },
    }),
  uploadContent: (data: { title: string; content: string; collectionId?: string; sourceType?: string }) =>
    api.post<ApiResponse<UploadDocumentResponse>>('/documents/upload/content', data),
  update: (id: string, data: { title: string; metadata?: Record<string, unknown> }) =>
    api.put<ApiResponse<Document>>(`/documents/${id}`, data),
  delete: (id: string) => api.delete<ApiResponse<void>>(`/documents/${id}`),
  reindex: (id: string) => api.post<ApiResponse<void>>(`/documents/${id}/reindex`),
}

export const searchApi = {
  search: (data: SearchRequest) => api.post<ApiResponse<SearchResponse>>('/search', data),
  searchGet: (params: { query: string; collectionId?: string; topK?: number; mode?: 'Vector' | 'Keyword' | 'Hybrid' }) =>
    api.get<ApiResponse<SearchResponse>>('/search', { params }),
  getCachedResponse: (query: string, similarityThreshold?: number) =>
    api.get<ApiResponse<SemanticCacheEntry>>('/search/cache', {
      params: { query, similarityThreshold },
    }),
  cacheResponse: (data: { query: string; response: string }) =>
    api.post<ApiResponse<void>>('/search/cache', data),
  clearCache: (collectionId?: string) =>
    api.delete<ApiResponse<void>>('/search/cache', { params: { collectionId } }),
}

export const analyticsApi = {
  getSystemStats: () => api.get<ApiResponse<SystemStats>>('/analytics/system'),
  getSearchAnalytics: (params?: { days?: number; collectionId?: string }) =>
    api.get<ApiResponse<SearchAnalytics>>('/analytics/search', { params }),
  getDocumentAnalytics: (params?: { days?: number; collectionId?: string }) =>
    api.get<ApiResponse<DocumentAnalytics>>('/analytics/documents', { params }),
}

export const apiKeysApi = {
  getAll: (params?: { page?: number; pageSize?: number }) =>
    api.get<ApiResponse<ApiKey[]>>('/apikeys', { params }),
  getById: (id: string) => api.get<ApiResponse<ApiKey>>(`/apikeys/${id}`),
  create: (data: CreateApiKeyRequest) =>
    api.post<ApiResponse<CreateApiKeyResponse>>('/apikeys', data),
  update: (id: string, data: { name?: string; rateLimitPerMinute?: number; rateLimitPerDay?: number }) =>
    api.put<ApiResponse<ApiKey>>(`/apikeys/${id}`, data),
  delete: (id: string) => api.delete<ApiResponse<void>>(`/apikeys/${id}`),
  activate: (id: string) => api.post<ApiResponse<void>>(`/apikeys/${id}/activate`),
  deactivate: (id: string) => api.post<ApiResponse<void>>(`/apikeys/${id}/deactivate`),
}

// Indexing Job types
export interface IndexingJob {
  id: string
  documentId: string
  documentTitle: string
  status: string
  totalChunks: number
  processedChunks: number
  progressPercentage: number
  errorMessage?: string
  createdAt: string
  startedAt?: string
  completedAt?: string
  durationMs?: number
}

export interface JobStatusSummary {
  queuedCount: number
  processingCount: number
  completedCount: number
  failedCount: number
  totalCount: number
  averageProcessingTimeMs: number
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
}

export const jobsApi = {
  getAll: (params?: { page?: number; pageSize?: number; status?: string }) =>
    api.get<ApiResponse<PagedResult<IndexingJob>>>('/jobs', { params }),
  getById: (id: string) => api.get<ApiResponse<IndexingJob>>(`/jobs/${id}`),
  getSummary: () => api.get<ApiResponse<JobStatusSummary>>('/jobs/summary'),
  cancel: (id: string) => api.post<ApiResponse<string>>(`/jobs/${id}/cancel`),
}
