import { useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Badge } from '@/components/ui/badge'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Separator } from '@/components/ui/separator'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  documentsApi, chunksApi, jobsApi,
  type ChunkDetail, type IndexingJobLog, type DocumentChunk
} from '@/lib/api'
import { formatDate, formatBytes, cn } from '@/lib/utils'
import {
  ArrowLeft, FileText, Clock, CheckCircle, XCircle, RefreshCw,
  Edit2, Save, X, Plus, Trash2, AlertCircle, Info, Bug,
  ChevronDown, ChevronRight, Loader2, RotateCcw
} from 'lucide-react'
import { useToast } from '@/hooks/use-toast'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'

const statusVariants: Record<string, 'default' | 'success' | 'warning' | 'destructive' | 'info'> = {
  Indexed: 'success',
  Pending: 'warning',
  Processing: 'info',
  Failed: 'destructive',
}

const logLevelColors: Record<string, string> = {
  Debug: 'text-gray-500',
  Info: 'text-blue-500',
  Warning: 'text-yellow-500',
  Error: 'text-red-500',
}

const logLevelIcons: Record<string, typeof Info> = {
  Debug: Bug,
  Info: Info,
  Warning: AlertCircle,
  Error: XCircle,
}

interface QAPair {
  question: string
  answer: string
}

export default function DocumentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { toast } = useToast()
  const queryClient = useQueryClient()

  const [activeTab, setActiveTab] = useState('overview')
  const [editingChunkId, setEditingChunkId] = useState<string | null>(null)
  const [editedContent, setEditedContent] = useState('')
  const [editedMetadata, setEditedMetadata] = useState('')
  const [expandedChunks, setExpandedChunks] = useState<Set<string>>(new Set())
  const [qaDialogOpen, setQaDialogOpen] = useState(false)
  const [editingQa, setEditingQa] = useState<{ chunkId: string; qa: QAPair[] } | null>(null)

  // Fetch document detail
  const { data: document, isLoading: docLoading } = useQuery({
    queryKey: ['document', id, 'detail'],
    queryFn: async () => {
      const response = await documentsApi.getDetail(id!)
      return response.data.data
    },
    enabled: !!id,
  })

  // Fetch chunks
  const { data: chunksData } = useQuery({
    queryKey: ['chunks', id],
    queryFn: async () => {
      const response = await chunksApi.getByDocumentId(id!, { page: 1, pageSize: 100 })
      return response.data.data
    },
    enabled: !!id,
  })

  // Fetch job logs if document has a recent job
  const { data: jobLogs } = useQuery({
    queryKey: ['jobs', 'document', id, 'logs'],
    queryFn: async () => {
      // Get the most recent job for this document
      const jobsResponse = await jobsApi.getAll({ pageSize: 1 })
      const jobs = jobsResponse.data.data?.items || []
      const documentJob = jobs.find(j => j.documentId === id)
      if (!documentJob) return []

      const logsResponse = await jobsApi.getLogs(documentJob.id)
      return logsResponse.data.data || []
    },
    enabled: !!id,
    refetchInterval: (query) => {
      const logs = query.state.data as IndexingJobLog[] | undefined
      // Keep refreshing if there are recent logs (within last minute)
      if (logs && logs.length > 0) {
        const lastLog = logs[logs.length - 1]
        const isRecent = new Date(lastLog.createdAt).getTime() > Date.now() - 60000
        return isRecent ? 2000 : false
      }
      return false
    },
  })

  // Update chunk mutation
  const updateChunkMutation = useMutation({
    mutationFn: async ({ chunkId, content, metadata }: { chunkId: string; content?: string; metadata?: Record<string, unknown> }) => {
      return chunksApi.update(chunkId, { content, metadata, regenerateEmbedding: !!content })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['chunks', id] })
      queryClient.invalidateQueries({ queryKey: ['document', id] })
      setEditingChunkId(null)
      toast({ title: 'Chunk updated successfully' })
    },
    onError: () => {
      toast({ title: 'Failed to update chunk', variant: 'destructive' })
    },
  })

  // Reindex mutation
  const reindexMutation = useMutation({
    mutationFn: () => documentsApi.reindex(id!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['document', id] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      toast({ title: 'Document queued for reindexing' })
    },
    onError: () => {
      toast({ title: 'Failed to reindex document', variant: 'destructive' })
    },
  })

  // Prefer ChunkDetail from API, fallback to DocumentChunk from document
  const chunks: (ChunkDetail | DocumentChunk)[] = chunksData?.items || document?.chunks || []

  // Helper to check if chunk is ChunkDetail (has hasEmbedding property)
  const isChunkDetail = (chunk: ChunkDetail | DocumentChunk): chunk is ChunkDetail => {
    return 'hasEmbedding' in chunk
  }

  const toggleChunkExpand = (chunkId: string) => {
    setExpandedChunks(prev => {
      const next = new Set(prev)
      if (next.has(chunkId)) {
        next.delete(chunkId)
      } else {
        next.add(chunkId)
      }
      return next
    })
  }

  const startEditingChunk = (chunk: ChunkDetail | DocumentChunk) => {
    setEditingChunkId(chunk.id)
    setEditedContent(chunk.content)
    setEditedMetadata(JSON.stringify(chunk.metadata, null, 2))
  }

  const saveChunkEdit = () => {
    if (!editingChunkId) return
    try {
      const metadata = JSON.parse(editedMetadata)
      updateChunkMutation.mutate({
        chunkId: editingChunkId,
        content: editedContent,
        metadata,
      })
    } catch {
      toast({ title: 'Invalid JSON in metadata', variant: 'destructive' })
    }
  }

  const openQaEditor = (chunk: ChunkDetail | DocumentChunk) => {
    const existingQa = (chunk.metadata?.qa as QAPair[]) || []
    setEditingQa({ chunkId: chunk.id, qa: existingQa.length > 0 ? existingQa : [{ question: '', answer: '' }] })
    setQaDialogOpen(true)
  }

  const addQaPair = () => {
    if (!editingQa) return
    setEditingQa({
      ...editingQa,
      qa: [...editingQa.qa, { question: '', answer: '' }]
    })
  }

  const removeQaPair = (index: number) => {
    if (!editingQa) return
    setEditingQa({
      ...editingQa,
      qa: editingQa.qa.filter((_, i) => i !== index)
    })
  }

  const updateQaPair = (index: number, field: 'question' | 'answer', value: string) => {
    if (!editingQa) return
    const newQa = [...editingQa.qa]
    newQa[index] = { ...newQa[index], [field]: value }
    setEditingQa({ ...editingQa, qa: newQa })
  }

  const saveQa = () => {
    if (!editingQa) return
    const filteredQa = editingQa.qa.filter(qa => qa.question.trim() || qa.answer.trim())
    const chunk = chunks.find(c => c.id === editingQa.chunkId)
    if (!chunk) return

    updateChunkMutation.mutate({
      chunkId: editingQa.chunkId,
      metadata: { ...chunk.metadata, qa: filteredQa }
    })
    setQaDialogOpen(false)
    setEditingQa(null)
  }

  if (docLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <Loader2 className="h-8 w-8 animate-spin" />
      </div>
    )
  }

  if (!document) {
    return (
      <div className="flex flex-col items-center justify-center h-full">
        <FileText className="h-12 w-12 text-muted-foreground mb-4" />
        <h3 className="text-lg font-medium">Document not found</h3>
        <Button variant="link" onClick={() => navigate('/documents')}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to documents
        </Button>
      </div>
    )
  }

  const StatusIcon = document.status === 'Indexed' ? CheckCircle :
    document.status === 'Processing' ? RefreshCw :
    document.status === 'Pending' ? Clock : XCircle

  return (
    <TooltipProvider>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-4">
            <Button variant="ghost" size="icon" onClick={() => navigate('/documents')}>
              <ArrowLeft className="h-5 w-5" />
            </Button>
            <div>
              <h2 className="text-2xl font-bold tracking-tight">{document.title}</h2>
              <div className="flex items-center gap-2 mt-1">
                <Badge variant={statusVariants[document.status] || 'default'}>
                  <StatusIcon className={cn('h-3 w-3 mr-1', document.status === 'Processing' && 'animate-spin')} />
                  {document.status}
                </Badge>
                <span className="text-sm text-muted-foreground">
                  {document.sourceType || 'Unknown'} • {formatBytes(document.fileSize || 0)}
                </span>
              </div>
            </div>
          </div>
          <div className="flex gap-2">
            <Button
              variant="outline"
              onClick={() => reindexMutation.mutate()}
              disabled={reindexMutation.isPending || document.status === 'Processing'}
            >
              <RotateCcw className="h-4 w-4 mr-2" />
              Reindex
            </Button>
          </div>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab}>
          <TabsList>
            <TabsTrigger value="overview">Overview</TabsTrigger>
            <TabsTrigger value="chunks">Chunks ({chunks.length})</TabsTrigger>
            <TabsTrigger value="qa">QA & Metadata</TabsTrigger>
            <TabsTrigger value="logs">Processing Logs</TabsTrigger>
          </TabsList>

          {/* Overview Tab */}
          <TabsContent value="overview" className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <Card>
                <CardHeader>
                  <CardTitle>Document Info</CardTitle>
                </CardHeader>
                <CardContent className="space-y-3">
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">ID</span>
                    <span className="font-mono text-sm">{document.id}</span>
                  </div>
                  <Separator />
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Source Type</span>
                    <span>{document.sourceType || 'Unknown'}</span>
                  </div>
                  <Separator />
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">File Size</span>
                    <span>{formatBytes(document.fileSize || 0)}</span>
                  </div>
                  <Separator />
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Chunk Count</span>
                    <span>{document.chunkCount}</span>
                  </div>
                  <Separator />
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Created</span>
                    <span>{formatDate(document.createdAt)}</span>
                  </div>
                  {document.indexedAt && (
                    <>
                      <Separator />
                      <div className="flex justify-between">
                        <span className="text-muted-foreground">Indexed</span>
                        <span>{formatDate(document.indexedAt)}</span>
                      </div>
                    </>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle>Metadata</CardTitle>
                </CardHeader>
                <CardContent>
                  <ScrollArea className="h-[200px]">
                    <pre className="text-sm font-mono whitespace-pre-wrap">
                      {JSON.stringify(document.metadata, null, 2)}
                    </pre>
                  </ScrollArea>
                </CardContent>
              </Card>
            </div>

            {/* Content Preview */}
            {chunks.length > 0 && (
              <Card>
                <CardHeader>
                  <CardTitle>Content Preview</CardTitle>
                  <CardDescription>First chunk of the document</CardDescription>
                </CardHeader>
                <CardContent>
                  <ScrollArea className="h-[300px]">
                    <p className="text-sm whitespace-pre-wrap">{chunks[0]?.content}</p>
                  </ScrollArea>
                </CardContent>
              </Card>
            )}
          </TabsContent>

          {/* Chunks Tab */}
          <TabsContent value="chunks" className="space-y-4">
            <Card>
              <CardHeader>
                <CardTitle>Document Chunks</CardTitle>
                <CardDescription>
                  {chunks.length} chunks extracted from this document
                </CardDescription>
              </CardHeader>
              <CardContent>
                <ScrollArea className="h-[600px]">
                  <div className="space-y-3">
                    {chunks.map((chunk) => {
                      const isExpanded = expandedChunks.has(chunk.id)
                      const isEditing = editingChunkId === chunk.id

                      return (
                        <div
                          key={chunk.id}
                          className="border rounded-lg p-4"
                        >
                          <div className="flex items-center justify-between mb-2">
                            <div className="flex items-center gap-2">
                              <Button
                                variant="ghost"
                                size="icon"
                                className="h-6 w-6"
                                onClick={() => toggleChunkExpand(chunk.id)}
                              >
                                {isExpanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                              </Button>
                              <span className="font-medium">Chunk #{chunk.chunkIndex + 1}</span>
                              <Badge variant="outline" className="text-xs">
                                {chunk.tokenCount} tokens
                              </Badge>
                              {isChunkDetail(chunk) && chunk.hasEmbedding && (
                                <Badge variant="success" className="text-xs">
                                  Embedded
                                </Badge>
                              )}
                            </div>
                            <div className="flex items-center gap-1">
                              {isEditing ? (
                                <>
                                  <Button
                                    size="sm"
                                    variant="ghost"
                                    onClick={() => setEditingChunkId(null)}
                                  >
                                    <X className="h-4 w-4" />
                                  </Button>
                                  <Button
                                    size="sm"
                                    onClick={saveChunkEdit}
                                    disabled={updateChunkMutation.isPending}
                                  >
                                    <Save className="h-4 w-4 mr-1" />
                                    Save
                                  </Button>
                                </>
                              ) : (
                                <Tooltip>
                                  <TooltipTrigger asChild>
                                    <Button
                                      size="sm"
                                      variant="ghost"
                                      onClick={() => startEditingChunk(chunk)}
                                    >
                                      <Edit2 className="h-4 w-4" />
                                    </Button>
                                  </TooltipTrigger>
                                  <TooltipContent>Edit chunk</TooltipContent>
                                </Tooltip>
                              )}
                            </div>
                          </div>

                          {isExpanded && (
                            <div className="mt-3 space-y-3">
                              {isEditing ? (
                                <>
                                  <div>
                                    <label className="text-sm font-medium">Content</label>
                                    <Textarea
                                      value={editedContent}
                                      onChange={(e) => setEditedContent(e.target.value)}
                                      className="mt-1 min-h-[200px] font-mono text-sm"
                                    />
                                  </div>
                                  <div>
                                    <label className="text-sm font-medium">Metadata (JSON)</label>
                                    <Textarea
                                      value={editedMetadata}
                                      onChange={(e) => setEditedMetadata(e.target.value)}
                                      className="mt-1 min-h-[100px] font-mono text-sm"
                                    />
                                  </div>
                                </>
                              ) : (
                                <>
                                  <div className="bg-muted/50 rounded p-3">
                                    <p className="text-sm whitespace-pre-wrap">{chunk.content}</p>
                                  </div>
                                  {Object.keys(chunk.metadata || {}).length > 0 && (
                                    <div>
                                      <span className="text-sm font-medium text-muted-foreground">Metadata:</span>
                                      <pre className="mt-1 text-xs font-mono bg-muted/50 rounded p-2 overflow-x-auto">
                                        {JSON.stringify(chunk.metadata, null, 2)}
                                      </pre>
                                    </div>
                                  )}
                                </>
                              )}
                            </div>
                          )}
                        </div>
                      )
                    })}
                  </div>
                </ScrollArea>
              </CardContent>
            </Card>
          </TabsContent>

          {/* QA Tab */}
          <TabsContent value="qa" className="space-y-4">
            <Card>
              <CardHeader>
                <CardTitle>QA Pairs & Memorize Data</CardTitle>
                <CardDescription>
                  Manage question-answer pairs and additional metadata for each chunk
                </CardDescription>
              </CardHeader>
              <CardContent>
                <ScrollArea className="h-[600px]">
                  <div className="space-y-4">
                    {chunks.map((chunk) => {
                      const qa = (chunk.metadata?.qa as QAPair[]) || []
                      return (
                        <div key={chunk.id} className="border rounded-lg p-4">
                          <div className="flex items-center justify-between mb-3">
                            <div>
                              <span className="font-medium">Chunk #{chunk.chunkIndex + 1}</span>
                              <p className="text-sm text-muted-foreground line-clamp-1 mt-1">
                                {chunk.content.substring(0, 100)}...
                              </p>
                            </div>
                            <Button
                              size="sm"
                              variant="outline"
                              onClick={() => openQaEditor(chunk)}
                            >
                              <Edit2 className="h-4 w-4 mr-1" />
                              Edit QA ({qa.length})
                            </Button>
                          </div>

                          {qa.length > 0 && (
                            <div className="space-y-2 mt-3">
                              {qa.map((pair, idx) => (
                                <div key={idx} className="bg-muted/50 rounded p-3 space-y-1">
                                  <p className="text-sm">
                                    <span className="font-medium text-blue-600">Q:</span> {pair.question}
                                  </p>
                                  <p className="text-sm">
                                    <span className="font-medium text-green-600">A:</span> {pair.answer}
                                  </p>
                                </div>
                              ))}
                            </div>
                          )}
                        </div>
                      )
                    })}
                  </div>
                </ScrollArea>
              </CardContent>
            </Card>
          </TabsContent>

          {/* Logs Tab */}
          <TabsContent value="logs" className="space-y-4">
            <Card>
              <CardHeader>
                <CardTitle>Processing Logs</CardTitle>
                <CardDescription>
                  View detailed logs from the indexing process
                </CardDescription>
              </CardHeader>
              <CardContent>
                <ScrollArea className="h-[600px]">
                  {jobLogs && jobLogs.length > 0 ? (
                    <div className="space-y-2">
                      {jobLogs.map((log) => {
                        const LogIcon = logLevelIcons[log.level] || Info
                        return (
                          <div
                            key={log.id}
                            className={cn(
                              'flex items-start gap-3 p-3 rounded border',
                              log.level === 'Error' && 'bg-red-50 border-red-200 dark:bg-red-950/20',
                              log.level === 'Warning' && 'bg-yellow-50 border-yellow-200 dark:bg-yellow-950/20'
                            )}
                          >
                            <LogIcon className={cn('h-4 w-4 mt-0.5', logLevelColors[log.level])} />
                            <div className="flex-1 min-w-0">
                              <div className="flex items-center gap-2 mb-1">
                                <span className={cn('text-xs font-medium', logLevelColors[log.level])}>
                                  {log.level}
                                </span>
                                {log.phase && (
                                  <Badge variant="outline" className="text-xs">
                                    {log.phase}
                                  </Badge>
                                )}
                                {log.chunkIndex !== undefined && log.chunkIndex !== null && (
                                  <Badge variant="secondary" className="text-xs">
                                    Chunk #{log.chunkIndex + 1}
                                  </Badge>
                                )}
                                <span className="text-xs text-muted-foreground ml-auto">
                                  {formatDate(log.createdAt)}
                                </span>
                              </div>
                              <p className="text-sm">{log.message}</p>
                              {log.details && (
                                <pre className="mt-1 text-xs font-mono text-muted-foreground whitespace-pre-wrap">
                                  {log.details}
                                </pre>
                              )}
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  ) : (
                    <div className="flex flex-col items-center justify-center py-12">
                      <Info className="h-12 w-12 text-muted-foreground mb-4" />
                      <h3 className="text-lg font-medium">No processing logs</h3>
                      <p className="text-sm text-muted-foreground">
                        Logs will appear here when the document is processed
                      </p>
                    </div>
                  )}
                </ScrollArea>
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>

        {/* QA Editor Dialog */}
        <Dialog open={qaDialogOpen} onOpenChange={setQaDialogOpen}>
          <DialogContent className="max-w-2xl max-h-[80vh] overflow-y-auto">
            <DialogHeader>
              <DialogTitle>Edit QA Pairs</DialogTitle>
              <DialogDescription>
                Add or modify question-answer pairs for this chunk
              </DialogDescription>
            </DialogHeader>

            {editingQa && (
              <div className="space-y-4">
                {editingQa.qa.map((pair, index) => (
                  <div key={index} className="border rounded-lg p-4 space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-medium">QA Pair #{index + 1}</span>
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => removeQaPair(index)}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                    <div>
                      <label className="text-sm text-muted-foreground">Question</label>
                      <Input
                        value={pair.question}
                        onChange={(e) => updateQaPair(index, 'question', e.target.value)}
                        placeholder="Enter question..."
                        className="mt-1"
                      />
                    </div>
                    <div>
                      <label className="text-sm text-muted-foreground">Answer</label>
                      <Textarea
                        value={pair.answer}
                        onChange={(e) => updateQaPair(index, 'answer', e.target.value)}
                        placeholder="Enter answer..."
                        className="mt-1"
                      />
                    </div>
                  </div>
                ))}

                <Button
                  variant="outline"
                  onClick={addQaPair}
                  className="w-full"
                >
                  <Plus className="h-4 w-4 mr-2" />
                  Add QA Pair
                </Button>
              </div>
            )}

            <DialogFooter>
              <Button variant="outline" onClick={() => setQaDialogOpen(false)}>
                Cancel
              </Button>
              <Button onClick={saveQa} disabled={updateChunkMutation.isPending}>
                {updateChunkMutation.isPending && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
                Save Changes
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </TooltipProvider>
  )
}
