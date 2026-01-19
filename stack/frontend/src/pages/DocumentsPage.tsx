import { useState, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Progress } from '@/components/ui/progress'
import { documentsApi, collectionsApi, jobsApi, type Document } from '@/lib/api'
import { formatDate, formatBytes, truncate } from '@/lib/utils'
import {
  Upload, FileText, Trash2, RefreshCw, CheckCircle, Clock, XCircle,
  AlertCircle, FolderOpen, RotateCcw, Loader2, Info, Eye, StopCircle
} from 'lucide-react'
import { useToast } from '@/hooks/use-toast'
import { useStore } from '@/store/useStore'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'

const statusIcons: Record<string, typeof CheckCircle> = {
  Indexed: CheckCircle,
  Pending: Clock,
  Processing: RefreshCw,
  Failed: XCircle,
}

const statusColors: Record<string, string> = {
  Indexed: 'text-green-500',
  Pending: 'text-yellow-500',
  Processing: 'text-blue-500',
  Failed: 'text-red-500',
}

export default function DocumentsPage() {
  const [page] = useState(1)
  const { toast } = useToast()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { selectedCollectionId } = useStore()

  // Fetch collections for display
  const { data: collections } = useQuery({
    queryKey: ['collections'],
    queryFn: async () => {
      const response = await collectionsApi.getAll()
      return response.data.data || []
    },
  })

  const selectedCollection = collections?.find(c => c.id === selectedCollectionId)

  // Fetch documents with auto-refresh when there are pending/processing docs
  const { data: documents, isLoading } = useQuery({
    queryKey: ['documents', page, selectedCollectionId],
    queryFn: async () => {
      const response = await documentsApi.getAll({
        page,
        pageSize: 50,
        collectionId: selectedCollectionId || undefined
      })
      return response.data.data || []
    },
    refetchInterval: (query) => {
      // Auto-refresh every 3 seconds if there are pending/processing documents
      const docs = query.state.data as Document[] | undefined
      const hasActiveJobs = docs?.some(d => d.status === 'Pending' || d.status === 'Processing')
      return hasActiveJobs ? 3000 : false
    },
  })

  // Fetch job summary for status overview
  const { data: jobSummary } = useQuery({
    queryKey: ['jobs', 'summary'],
    queryFn: async () => {
      const response = await jobsApi.getSummary()
      return response.data.data
    },
    refetchInterval: 5000, // Refresh every 5 seconds
  })

  // Calculate document stats
  const docStats = useMemo(() => {
    if (!documents) return { pending: 0, processing: 0, indexed: 0, failed: 0 }
    return {
      pending: documents.filter(d => d.status === 'Pending').length,
      processing: documents.filter(d => d.status === 'Processing').length,
      indexed: documents.filter(d => d.status === 'Indexed').length,
      failed: documents.filter(d => d.status === 'Failed').length,
    }
  }, [documents])

  const deleteMutation = useMutation({
    mutationFn: (id: string) => documentsApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['documents'] })
      queryClient.invalidateQueries({ queryKey: ['collections'] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      toast({ title: 'Document deleted successfully' })
    },
    onError: (error: unknown) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const status = (error as any)?.response?.status
      if (status === 403) {
        toast({
          title: 'Permission denied',
          description: 'Admin role required to delete documents. Check your API key settings.',
          variant: 'destructive'
        })
      } else {
        toast({ title: 'Failed to delete document', variant: 'destructive' })
      }
    },
  })

  const reindexMutation = useMutation({
    mutationFn: (id: string) => documentsApi.reindex(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['documents'] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      toast({ title: 'Document queued for reindexing' })
    },
    onError: () => {
      toast({ title: 'Failed to reindex document', variant: 'destructive' })
    },
  })

  const cancelJobMutation = useMutation({
    mutationFn: (jobId: string) => jobsApi.cancel(jobId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['documents'] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      toast({ title: 'Indexing job cancelled' })
    },
    onError: () => {
      toast({ title: 'Failed to cancel job', variant: 'destructive' })
    },
  })

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files
    if (!files || files.length === 0) return

    const formData = new FormData()
    formData.append('file', files[0])
    formData.append('title', files[0].name)
    if (selectedCollectionId) {
      formData.append('collectionId', selectedCollectionId)
    }

    try {
      const response = await documentsApi.upload(formData)
      queryClient.invalidateQueries({ queryKey: ['documents'] })
      queryClient.invalidateQueries({ queryKey: ['collections'] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })

      const jobId = response.data.data?.jobId
      toast({
        title: 'Document uploaded',
        description: jobId
          ? 'Indexing job queued. Status will update automatically.'
          : 'Document uploaded successfully.',
      })
    } catch {
      toast({ title: 'Failed to upload document', variant: 'destructive' })
    }

    e.target.value = ''
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary" />
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-3xl font-bold tracking-tight">Documents</h2>
          <p className="text-muted-foreground">
            {selectedCollectionId
              ? `Viewing documents in "${selectedCollection?.name || 'selected collection'}"`
              : 'Viewing all documents'}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Input
            type="file"
            className="hidden"
            id="file-upload"
            onChange={handleFileUpload}
            accept=".pdf,.txt,.md,.docx,.html"
          />
          <Button asChild>
            <label htmlFor="file-upload" className="cursor-pointer">
              <Upload className="mr-2 h-4 w-4" />
              Upload Document
            </label>
          </Button>
        </div>
      </div>

      {/* Job Status Summary */}
      {(jobSummary && (jobSummary.queuedCount > 0 || jobSummary.processingCount > 0)) && (
        <Card className="border-blue-200 bg-blue-50/50 dark:border-blue-900 dark:bg-blue-950/20">
          <CardContent className="py-4">
            <div className="flex items-center gap-4">
              <Loader2 className="h-5 w-5 animate-spin text-blue-500" />
              <div className="flex-1">
                <p className="text-sm font-medium">
                  Processing documents...
                </p>
                <p className="text-xs text-muted-foreground">
                  {jobSummary.queuedCount} queued, {jobSummary.processingCount} processing
                </p>
              </div>
              <div className="text-right text-xs text-muted-foreground">
                Auto-refreshing every 3s
              </div>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Document Stats */}
      <div className="grid gap-4 md:grid-cols-4">
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Indexed</p>
                <p className="text-2xl font-bold text-green-600">{docStats.indexed}</p>
              </div>
              <CheckCircle className="h-8 w-8 text-green-500/30" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Pending</p>
                <p className="text-2xl font-bold text-yellow-600">{docStats.pending}</p>
              </div>
              <Clock className="h-8 w-8 text-yellow-500/30" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Processing</p>
                <p className="text-2xl font-bold text-blue-600">{docStats.processing}</p>
              </div>
              <RefreshCw className="h-8 w-8 text-blue-500/30" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Failed</p>
                <p className="text-2xl font-bold text-red-600">{docStats.failed}</p>
              </div>
              <XCircle className="h-8 w-8 text-red-500/30" />
            </div>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>
            {selectedCollectionId ? 'Filtered Documents' : 'All Documents'}
          </CardTitle>
          <CardDescription>
            {documents?.length ?? 0} documents {selectedCollectionId ? 'in current scope' : 'total'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <TooltipProvider>
            {documents && documents.length > 0 ? (
              <div className="space-y-3">
                {documents.map((doc) => {
                  const StatusIcon = statusIcons[doc.status] || AlertCircle
                  const statusColor = statusColors[doc.status] || 'text-gray-500'
                  const isFailed = doc.status === 'Failed'
                  const isProcessing = doc.status === 'Processing'
                  const isPending = doc.status === 'Pending'
                  const errorMessage: string | undefined = typeof doc.metadata?.error === 'string' ? doc.metadata.error : undefined

                  return (
                    <div
                      key={doc.id}
                      className={`flex items-center justify-between p-4 border rounded-lg transition-colors ${
                        isFailed ? 'border-red-200 bg-red-50/50 dark:border-red-900 dark:bg-red-950/20' : 'hover:bg-accent/50'
                      }`}
                    >
                      <div className="flex items-center space-x-4 flex-1 min-w-0">
                        <FileText className={`h-8 w-8 flex-shrink-0 ${isFailed ? 'text-red-400' : 'text-muted-foreground'}`} />
                        <div className="min-w-0 flex-1">
                          <h4 className="font-medium truncate">{truncate(doc.title, 60)}</h4>
                          <div className="flex items-center flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
                            <span>{doc.sourceType || 'Unknown'}</span>
                            {doc.fileSize && <span>{formatBytes(doc.fileSize)}</span>}
                            {doc.status === 'Indexed' && <span>{doc.chunkCount} chunks</span>}
                            <span>{formatDate(doc.createdAt)}</span>
                            {doc.collectionId && (
                              <span className="flex items-center gap-1">
                                <FolderOpen className="h-3 w-3" />
                                {collections?.find(c => c.id === doc.collectionId)?.name || 'Collection'}
                              </span>
                            )}
                          </div>
                          {/* Error message display */}
                          {isFailed && errorMessage && (
                            <p className="text-xs text-red-600 mt-1 truncate">
                              Error: {errorMessage}
                            </p>
                          )}
                        </div>
                      </div>

                      <div className="flex items-center space-x-3 flex-shrink-0">
                        {/* Status indicator */}
                        <div className={`flex items-center space-x-1 ${statusColor}`}>
                          <StatusIcon className={`h-4 w-4 ${isProcessing ? 'animate-spin' : ''}`} />
                          <span className="text-sm">{doc.status}</span>
                        </div>

                        {/* Processing progress indicator */}
                        {isProcessing && (
                          <div className="w-20">
                            <Progress value={50} className="h-1" />
                          </div>
                        )}

                        {/* View detail button */}
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <Button
                              variant="ghost"
                              size="icon"
                              onClick={() => navigate(`/documents/${doc.id}`)}
                            >
                              <Eye className="h-4 w-4" />
                            </Button>
                          </TooltipTrigger>
                          <TooltipContent>
                            <p>View details</p>
                          </TooltipContent>
                        </Tooltip>

                        {/* Error info tooltip */}
                        {isFailed && errorMessage && (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <Button variant="ghost" size="icon" className="text-red-500">
                                <Info className="h-4 w-4" />
                              </Button>
                            </TooltipTrigger>
                            <TooltipContent side="left" className="max-w-sm">
                              <p className="text-sm">{errorMessage}</p>
                            </TooltipContent>
                          </Tooltip>
                        )}

                        {/* Cancel button for processing/pending documents */}
                        {(isProcessing || isPending) && !!doc.metadata?.jobId && (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <Button
                                variant="ghost"
                                size="icon"
                                className="text-orange-500 hover:text-orange-600"
                                onClick={() => cancelJobMutation.mutate(doc.metadata?.jobId as string)}
                                disabled={cancelJobMutation.isPending}
                              >
                                <StopCircle className="h-4 w-4" />
                              </Button>
                            </TooltipTrigger>
                            <TooltipContent>
                              <p>Cancel indexing</p>
                            </TooltipContent>
                          </Tooltip>
                        )}

                        {/* Reindex button for failed/indexed documents */}
                        {(isFailed || doc.status === 'Indexed') && (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <Button
                                variant="ghost"
                                size="icon"
                                onClick={() => reindexMutation.mutate(doc.id)}
                                disabled={reindexMutation.isPending || isPending || isProcessing}
                              >
                                <RotateCcw className="h-4 w-4" />
                              </Button>
                            </TooltipTrigger>
                            <TooltipContent>
                              <p>{isFailed ? 'Retry indexing' : 'Reindex document'}</p>
                            </TooltipContent>
                          </Tooltip>
                        )}

                        {/* Delete button - always available */}
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <Button
                              variant="ghost"
                              size="icon"
                              className={isProcessing || isPending ? 'text-red-500 hover:text-red-600' : ''}
                              onClick={() => deleteMutation.mutate(doc.id)}
                              disabled={deleteMutation.isPending}
                            >
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          </TooltipTrigger>
                          <TooltipContent>
                            <p>{isProcessing || isPending ? 'Force delete (cancels job)' : 'Delete document'}</p>
                          </TooltipContent>
                        </Tooltip>
                      </div>
                    </div>
                  )
                })}
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center py-12">
                <FileText className="h-12 w-12 text-muted-foreground mb-4" />
                <h3 className="text-lg font-medium">No documents yet</h3>
                <p className="text-sm text-muted-foreground mb-4">
                  Upload your first document to get started
                </p>
                <Button asChild>
                  <label htmlFor="file-upload" className="cursor-pointer">
                    <Upload className="mr-2 h-4 w-4" />
                    Upload Document
                  </label>
                </Button>
              </div>
            )}
          </TooltipProvider>
        </CardContent>
      </Card>
    </div>
  )
}
