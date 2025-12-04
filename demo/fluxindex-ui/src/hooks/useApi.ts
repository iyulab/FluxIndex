/**
 * React Query hooks for FluxIndex API
 */
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { fluxIndexApi } from '../api/client';
import type { SearchRequest, McpSearchRequest } from '../types/api';

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
