/**
 * FluxIndex API TypeScript Types
 */

// Status API
export interface StatusResponse {
  totalDocuments: number;
  totalChunks: number;
  lastIndexed: string | null;
  databasePath: string;
  embeddingModel: string;
}

// Document API
export interface DocumentInfo {
  id: string;
  title: string;
  chunkCount: number;
  createdAt: string;
}

// Upload API
export interface UploadResponse {
  success: boolean;
  documentId?: string;
  fileName: string;
  chunkCount: number;
  processingTimeMs: number;
  message: string;
}

// Search API
export interface SearchRequest {
  query: string;
  topK: number;
  useReranker: boolean;
}

export interface SearchResult {
  chunkId: string;
  content: string;
  score: number;
  rerankedScore?: number;
  wasReranked: boolean;
  metadata: Record<string, unknown>;
  source?: string;
}

export interface SearchResponse {
  query: string;
  results: SearchResult[];
  totalResults: number;
  searchTimeMs: number;
  usedReranker: boolean;
  error?: string;
}

// MCP API
export interface McpSearchRequest {
  query: string;
  topK: number;
  useReranker: boolean;
  includeMetadata: boolean;
  maxTokens: number;
}

export interface McpSearchResult {
  id: string;
  content: string;
  score: number;
  vectorScore: number;
  rerankedScore?: number;
  wasReranked: boolean;
  metadata?: Record<string, unknown>;
  source?: string;
}

export interface McpParameters {
  topK: number;
  useReranker: boolean;
  includeMetadata: boolean;
  maxTokens: number;
}

export interface McpResultMetadata {
  totalResults: number;
  resultsReturned: number;
  estimatedTokens: number;
  searchTimeMs: number;
  usedReranker: boolean;
  error?: string;
}

export interface McpSearchResponse {
  toolName: string;
  query: string;
  parameters: McpParameters;
  results: McpSearchResult[];
  metadata: McpResultMetadata;
}

// Document Detail API
export interface ChunkMetadataDto {
  language: string;
  contentType: string;
  keywords: string[];
  entities: string[];
  topics: string[];
  sectionTitle: string;
  importanceScore: number;
  tokenCount: number;
  characterCount: number;
  sentenceCount: number;
  readabilityScore: number;
}

export interface ChunkQualityDto {
  contentCompleteness: number;
  informationDensity: number;
  coherence: number;
  uniqueness: number;
}

export interface ChunkDetail {
  id: string;
  index: number;
  content: string;
  tokenCount: number;
  metadata: Record<string, unknown>;
  chunkMetadata: ChunkMetadataDto | null;
  quality: ChunkQualityDto | null;
}

export interface DocumentDetailResponse {
  id: string;
  title: string;
  createdAt: string;
  totalChunks: number;
  fullContent: string;
  chunks: ChunkDetail[];
}
