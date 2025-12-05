/**
 * React Query hooks for FluxIndex API
 */
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { fluxIndexApi } from '../api/client';
import type { SearchRequest, McpSearchRequest, RememorizeRequest, BatchRememorizeRequest } from '../types/api';

// Query keys
export const queryKeys = {
  status: ['status'] as const,
  documents: ['documents'] as const,
  documentDetail: (id: string) => ['documentDetail', id] as const,
};

// Status hook
export function useStatus() {
  return useQuery({
    queryKey: queryKeys.status,
    queryFn: fluxIndexApi.getStatus,
    refetchInterval: 30000, // Auto-refresh every 30 seconds
  });
}

// Documents hook
export function useDocuments() {
  return useQuery({
    queryKey: queryKeys.documents,
    queryFn: fluxIndexApi.getDocuments,
  });
}

// Delete document mutation
export function useDeleteDocument() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => fluxIndexApi.deleteDocument(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.documents });
      queryClient.invalidateQueries({ queryKey: queryKeys.status });
    },
  });
}

// Upload file mutation
export function useUploadFile() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      file,
      onProgress,
    }: {
      file: File;
      onProgress?: (progress: number) => void;
    }) => fluxIndexApi.uploadFile(file, onProgress),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.documents });
      queryClient.invalidateQueries({ queryKey: queryKeys.status });
    },
  });
}

// Search mutation
export function useSearch() {
  return useMutation({
    mutationFn: (request: SearchRequest) => fluxIndexApi.search(request),
  });
}

// MCP Search mutation
export function useMcpSearch() {
  return useMutation({
    mutationFn: (request: McpSearchRequest) => fluxIndexApi.mcpSearch(request),
  });
}

// Document detail hook
export function useDocumentDetail(id: string | null) {
  return useQuery({
    queryKey: queryKeys.documentDetail(id ?? ''),
    queryFn: () => fluxIndexApi.getDocumentDetail(id!),
    enabled: !!id,
  });
}

// Rememorize chunk mutation
export function useRememorizeChunk() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ chunkId, request }: { chunkId: string; request: RememorizeRequest }) =>
      fluxIndexApi.rememorizeChunk(chunkId, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.documents });
      queryClient.invalidateQueries({ queryKey: queryKeys.status });
    },
  });
}

// Batch rememorize mutation
export function useBatchRememorize() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ documentId, request }: { documentId: string; request: BatchRememorizeRequest }) =>
      fluxIndexApi.batchRememorize(documentId, request),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.documentDetail(variables.documentId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.documents });
      queryClient.invalidateQueries({ queryKey: queryKeys.status });
    },
  });
}

// Generate QA mutation
export function useGenerateDocumentQA() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (documentId: string) => fluxIndexApi.generateDocumentQA(documentId),
    onSuccess: (_, documentId) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.documentDetail(documentId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.documents });
    },
  });
}

// Logs query key
export const logsQueryKey = ['logs'] as const;

// Logs hook
export function useLogs(options?: { limit?: number; category?: string; level?: string; refetchInterval?: number }) {
  return useQuery({
    queryKey: [...logsQueryKey, options?.limit, options?.category, options?.level],
    queryFn: () => fluxIndexApi.getLogs(options?.limit, options?.category, options?.level),
    refetchInterval: options?.refetchInterval ?? 3000, // Auto-refresh every 3 seconds by default
  });
}

// Clear logs mutation
export function useClearLogs() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => fluxIndexApi.clearLogs(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: logsQueryKey });
    },
  });
}
