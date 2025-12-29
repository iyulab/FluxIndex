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
    } else if (error.response?.status === 403) {
      // Forbidden - user doesn't have required permissions
      console.warn('Permission denied. Admin role required for this operation.')
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

export interface QAPair {
  question: string
  answer: string
}

export interface DocumentDetail extends Document {
  extractedContent?: string
  qaPairs: QAPair[]
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

export interface GenerateQAResponse {
  documentId: string
  qaPairsGenerated: number
  qaPairs: QAPair[]
  message: string
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
  generateQA: (id: string, params?: { maxPairs?: number }) =>
    api.post<ApiResponse<GenerateQAResponse>>(`/documents/${id}/generate-qa`, null, { params }),
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
  getLogs: (id: string, minLevel?: string) =>
    api.get<ApiResponse<IndexingJobLog[]>>(`/jobs/${id}/logs`, { params: { minLevel } }),
  getDetail: (id: string) => api.get<ApiResponse<IndexingJobDetail>>(`/jobs/${id}/detail`),
}

// Indexing Job Log types
export interface IndexingJobLog {
  id: string
  jobId: string
  level: string
  message: string
  details?: string
  phase?: string
  chunkIndex?: number
  createdAt: string
}

export interface IndexingJobDetail extends IndexingJob {
  logs: IndexingJobLog[]
}

// Chunk types
export interface ChunkDetail {
  id: string
  documentId: string
  documentTitle: string
  chunkIndex: number
  content: string
  tokenCount: number
  startPosition: number
  endPosition: number
  metadata: Record<string, unknown>
  hasEmbedding: boolean
  createdAt: string
  updatedAt?: string
}

export interface UpdateChunkRequest {
  content?: string
  metadata?: Record<string, unknown>
  regenerateEmbedding?: boolean
}

export const chunksApi = {
  getAll: (params?: { page?: number; pageSize?: number; documentId?: string }) =>
    api.get<ApiResponse<PagedResult<ChunkDetail>>>('/chunks', { params }),
  getById: (id: string) => api.get<ApiResponse<ChunkDetail>>(`/chunks/${id}`),
  getByDocumentId: (documentId: string, params?: { page?: number; pageSize?: number }) =>
    api.get<ApiResponse<PagedResult<ChunkDetail>>>(`/chunks/document/${documentId}`, { params }),
  update: (id: string, data: UpdateChunkRequest) =>
    api.put<ApiResponse<ChunkDetail>>(`/chunks/${id}`, data),
  delete: (id: string) => api.delete<ApiResponse<void>>(`/chunks/${id}`),
  enrich: (id: string, data?: { metadataSchema?: string; context?: string; overwriteExisting?: boolean }) =>
    api.post<ApiResponse<{ chunkId: string; success: boolean; enrichedMetadata: Record<string, unknown>; message?: string }>>(`/chunks/${id}/enrich`, data),
  regenerateEmbedding: (id: string) =>
    api.post<ApiResponse<void>>(`/chunks/${id}/regenerate-embedding`),
}

// AI Provider Settings types
export interface AiProviderSettings {
  id: string
  providerName: string
  displayName: string
  hasApiKey: boolean
  isEnabled: boolean
  isDefaultEmbedding: boolean
  isDefaultLlm: boolean
  embeddingModel?: string
  llmModel?: string
  endpointUrl?: string
  /** Whether this is a local provider that doesn't require an API key (e.g., LMSupply) */
  isLocalProvider: boolean
  /** Whether this provider requires a custom endpoint URL (e.g., Azure, GPUStack) */
  requiresEndpoint: boolean
  availableEmbeddingModels: string[]
  availableLlmModels: string[]
  createdAt: string
  updatedAt: string
}

export interface UpdateAiProviderRequest {
  apiKey?: string
  embeddingModel?: string
  llmModel?: string
  endpointUrl?: string
  isEnabled?: boolean
  isDefaultEmbedding?: boolean
  isDefaultLlm?: boolean
}

export interface AiConfigurationStatus {
  hasEmbeddingProvider: boolean
  hasLlmProvider: boolean
  defaultEmbeddingProvider?: string
  defaultEmbeddingModel?: string
  defaultLlmProvider?: string
  defaultLlmModel?: string
  providers: AiProviderSettings[]
}

export interface ModelInfo {
  id: string
  name: string
  description?: string
  maxTokens?: number
  dimensions?: number
}

export interface AvailableModels {
  providerName: string
  embeddingModels: ModelInfo[]
  llmModels: ModelInfo[]
}

export const settingsApi = {
  getAiConfiguration: () =>
    api.get<ApiResponse<AiConfigurationStatus>>('/settings/ai'),
  getAllProviders: () =>
    api.get<ApiResponse<AiProviderSettings[]>>('/settings/ai/providers'),
  getProvider: (providerName: string) =>
    api.get<ApiResponse<AiProviderSettings>>(`/settings/ai/providers/${providerName}`),
  updateProvider: (providerName: string, data: UpdateAiProviderRequest) =>
    api.put<ApiResponse<AiProviderSettings>>(`/settings/ai/providers/${providerName}`, data),
  getAvailableModels: (providerName: string) =>
    api.get<ApiResponse<AvailableModels>>(`/settings/ai/providers/${providerName}/models`),
  testProviderConnection: (providerName: string) =>
    api.post<ApiResponse<boolean>>(`/settings/ai/providers/${providerName}/test`),
  initializeProviders: () =>
    api.post<ApiResponse<string>>('/settings/ai/providers/initialize'),
}

// Evaluation types
export interface EvaluationQuery {
  query: string
  expectedAnswer: string
  relevantDocumentIds?: string[]
}

export interface RunEvaluationRequest {
  jobName: string
  collectionId?: string
  queries: EvaluationQuery[]
  topK?: number
  generateAnswers?: boolean
  version?: string
}

export interface EvaluationJobResponse {
  jobId: string
  jobName: string
  status: string
  totalQueries: number
  createdAt: string
  estimatedCompletionAt?: string
}

export interface EvaluationMetrics {
  mrr: number
  precisionAtK: number
  recallAtK: number
  ndcg: number
  averageFaithfulness?: number
  averageRelevancy?: number
  averageContextPrecision?: number
  overallScore: number
  qualityTier: string
}

export interface QueryEvaluationResult {
  query: string
  expectedAnswer: string
  generatedAnswer?: string
  retrievedChunks: number
  relevantChunksFound?: number
  metrics?: {
    reciprocalRank: number
    precision: number
    recall: number
    faithfulness?: number
    relevancy?: number
    contextPrecision?: number
  }
  retrievalLatencyMs: number
  generationLatencyMs?: number
  success: boolean
  errorMessage?: string
}

export interface EvaluationResult {
  jobId: string
  jobName: string
  status: string
  totalQueries: number
  successfulQueries: number
  failedQueries: number
  metrics?: EvaluationMetrics
  queryResults?: QueryEvaluationResult[]
  startedAt?: string
  completedAt?: string
  durationMs?: number
  errorMessage?: string
}

export interface QualityThresholds {
  minPrecision: number
  minRecall: number
  minF1Score: number
  minMRR: number
  minNDCG: number
  minFaithfulness?: number
  minAnswerRelevancy?: number
}

export interface QualityGateRequest {
  systemVersion: string
  datasetId: string
  thresholds: QualityThresholds
}

export interface QualityGateResult {
  passed: boolean
  systemVersion: string
  datasetId: string
  metrics: EvaluationMetrics
  appliedThresholds: QualityThresholds
  failedCriteria: string[]
  summary: Record<string, unknown>
  executedAt: string
  durationMs?: number
}

export const evaluationApi = {
  runEvaluation: (data: RunEvaluationRequest) =>
    api.post<ApiResponse<EvaluationJobResponse>>('/evaluation/run', data),
  getJobStatus: (jobId: string) =>
    api.get<ApiResponse<EvaluationJobResponse>>(`/evaluation/${jobId}`),
  getResults: (jobId: string, includeQueryResults = false) =>
    api.get<ApiResponse<EvaluationResult>>(`/evaluation/results/${jobId}`, { params: { includeQueryResults } }),
  listJobs: (params?: { status?: string; page?: number; pageSize?: number }) =>
    api.get<ApiResponse<PagedResult<EvaluationJobResponse>>>('/evaluation/jobs', { params }),
  cancelJob: (jobId: string) =>
    api.post<ApiResponse<string>>(`/evaluation/${jobId}/cancel`),
}

export const qualityGateApi = {
  execute: (data: QualityGateRequest) =>
    api.post<ApiResponse<QualityGateResult>>('/qualitygate/execute', data),
  quickCheck: (version: string, datasetId: string, minScore = 0.7) =>
    api.get<ApiResponse<{ status: string; version: string; score: number }>>('/qualitygate/check', {
      params: { version, datasetId, minScore }
    }),
}

// Graph (Knowledge Graph) types
export interface GraphStatistics {
  isAvailable: boolean
  totalNodes: number
  totalRelationships: number
  totalCommunities: number
  nodesByType: Record<string, number>
  relationshipsByType: Record<string, number>
}

export interface GraphHealth {
  isAvailable: boolean
  service: string
  status: string
}

export interface EntityRelationship {
  sourceEntityId: string
  targetEntityId: string
  relationshipType: string
  properties?: Record<string, unknown>
}

export interface GetRelatedEntitiesRequest {
  entityIds: string[]
  maxHops?: number
}

export interface GetRelatedEntitiesResponse {
  relationships: EntityRelationship[]
  totalCount: number
}

export interface QueryExpansionRequest {
  query: string
  maxEntities?: number
}

export interface QueryExpansionResponse {
  originalQuery: string
  relatedTerms: string[]
  expandedQuery: string
}

export interface GraphPath {
  entityIds: string[]
  relationshipTypes: string[]
  pathWeight: number
  length: number
}

export interface FindPathsRequest {
  sourceEntityId: string
  targetEntityId: string
  maxPathLength?: number
}

export interface FindPathsResponse {
  paths: GraphPath[]
  sourceEntityId: string
  targetEntityId: string
}

export interface GraphCommunity {
  communityId: string
  name: string
  memberEntityIds: string[]
  summary?: string
  level: number
}

export interface RunCommunityDetectionRequest {
  collectionId?: string
}

export interface RunCommunityDetectionResponse {
  communitiesDetected: number
  executionTimeMs: number
}

export const graphApi = {
  getStatistics: () =>
    api.get<ApiResponse<GraphStatistics>>('/graph/statistics'),
  getHealth: () =>
    api.get<ApiResponse<GraphHealth>>('/graph/health'),
  getRelatedEntities: (data: GetRelatedEntitiesRequest) =>
    api.post<ApiResponse<GetRelatedEntitiesResponse>>('/graph/entities/related', data),
  expandQuery: (data: QueryExpansionRequest) =>
    api.post<ApiResponse<QueryExpansionResponse>>('/graph/query/expand', data),
  findPaths: (data: FindPathsRequest) =>
    api.post<ApiResponse<FindPathsResponse>>('/graph/paths/find', data),
  getEntityCommunity: (entityId: string) =>
    api.get<ApiResponse<GraphCommunity>>(`/graph/entities/${encodeURIComponent(entityId)}/community`),
  runCommunityDetection: (data?: RunCommunityDetectionRequest) =>
    api.post<ApiResponse<RunCommunityDetectionResponse>>('/graph/communities/detect', data || {}),
}
