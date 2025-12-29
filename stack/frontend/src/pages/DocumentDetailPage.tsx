import { useState, useMemo } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import Editor from '@monaco-editor/react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ScrollArea } from '@/components/ui/scroll-area'
import { ResizablePanelGroup, ResizablePanel, ResizableHandle } from '@/components/ui/resizable'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  documentsApi, chunksApi,
  type ChunkDetail, type DocumentChunk
} from '@/lib/api'
import { formatBytes, cn } from '@/lib/utils'
import {
  ArrowLeft, FileText, Clock, CheckCircle, XCircle, RefreshCw,
  Save, Loader2, RotateCcw, FileCode, Database, MessageSquare,
  Info, Layers, ImageIcon, Sparkles, Copy, Hash, Calendar,
  FolderOpen, FileType, HardDrive
} from 'lucide-react'
import { useToast } from '@/hooks/use-toast'

const statusVariants: Record<string, 'default' | 'success' | 'warning' | 'destructive' | 'info'> = {
  Indexed: 'success',
  Pending: 'warning',
  Processing: 'info',
  Failed: 'destructive',
}

type DocumentTab = 'info' | 'extract' | 'images' | 'chunks' | 'qa'
type ChunkView = 'content' | 'metadata'

interface SelectedChunk {
  chunk: DocumentChunk | ChunkDetail
  view: ChunkView
}

// Info row component for displaying document properties
function InfoRow({
  icon: Icon,
  label,
  value,
  copyable = false
}: {
  icon: typeof FileText
  label: string
  value: React.ReactNode
  copyable?: boolean
}) {
  const { toast } = useToast()

  const handleCopy = () => {
    if (typeof value === 'string') {
      navigator.clipboard.writeText(value)
      toast({ title: 'Copied to clipboard' })
    }
  }

  return (
    <div className="flex items-start gap-3 py-3 border-b last:border-b-0">
      <Icon className="h-4 w-4 mt-0.5 text-muted-foreground shrink-0" />
      <div className="flex-1 min-w-0">
        <div className="text-sm text-muted-foreground">{label}</div>
        <div className="font-medium break-all">{value || '-'}</div>
      </div>
      {copyable && typeof value === 'string' && value && (
        <Button variant="ghost" size="icon" className="h-8 w-8 shrink-0" onClick={handleCopy}>
          <Copy className="h-3 w-3" />
        </Button>
      )}
    </div>
  )
}

export default function DocumentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { toast } = useToast()
  const queryClient = useQueryClient()

  const [activeTab, setActiveTab] = useState<DocumentTab>('info')
  const [selectedChunk, setSelectedChunk] = useState<SelectedChunk | null>(null)
  const [chunkView, setChunkView] = useState<ChunkView>('content')
  const [editedContent, setEditedContent] = useState<string>('')
  const [hasChanges, setHasChanges] = useState(false)

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

  // Update chunk mutation
  const updateChunkMutation = useMutation({
    mutationFn: async ({ chunkId, content, metadata }: { chunkId: string; content?: string; metadata?: Record<string, unknown> }) => {
      return chunksApi.update(chunkId, { content, metadata, regenerateEmbedding: !!content })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['chunks', id] })
      queryClient.invalidateQueries({ queryKey: ['document', id] })
      setHasChanges(false)
      toast({ title: 'Saved successfully' })
    },
    onError: () => {
      toast({ title: 'Failed to save', variant: 'destructive' })
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

  // Generate Q&A mutation
  const generateQAMutation = useMutation({
    mutationFn: async () => {
      const response = await documentsApi.generateQA(id!)
      return response.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['document', id] })
      toast({ title: 'Q&A pairs generated successfully' })
    },
    onError: () => {
      toast({ title: 'Failed to generate Q&A pairs', variant: 'destructive' })
    },
  })

  const chunks: (ChunkDetail | DocumentChunk)[] = chunksData?.items || document?.chunks || []

  // Get document-level metadata (FileFlux processing metadata)
  // Prefer document.metadata (new documents), fallback to first chunk (legacy documents)
  const documentMetadata = useMemo(() => {
    const docLevelKeys = ['language', 'ff_strategy', 'ff_density', 'ff_quality', 'ff_importance', 'total_chunks', 'word_count']

    // First try document.metadata (populated during indexing for new documents)
    if (document?.metadata && Object.keys(document.metadata).length > 0) {
      const docMeta: Record<string, unknown> = {}
      for (const key of docLevelKeys) {
        if (key in document.metadata) {
          docMeta[key] = document.metadata[key]
        }
      }
      if (Object.keys(docMeta).length > 0) return docMeta
    }

    // Fallback: extract from first chunk (legacy documents not yet re-indexed)
    const firstChunk = chunks[0]
    if (!firstChunk?.metadata) return null

    const docMeta: Record<string, unknown> = {}
    for (const key of docLevelKeys) {
      if (key in firstChunk.metadata) {
        docMeta[key] = firstChunk.metadata[key]
      }
    }

    return Object.keys(docMeta).length > 0 ? docMeta : null
  }, [document?.metadata, chunks])

  // Handle chunk selection
  const handleSelectChunk = (chunk: DocumentChunk | ChunkDetail) => {
    const content = chunkView === 'content' ? chunk.content : JSON.stringify(chunk.metadata || {}, null, 2)
    setSelectedChunk({ chunk, view: chunkView })
    setEditedContent(content)
    setHasChanges(false)
  }

  // Handle chunk view toggle
  const handleChunkViewChange = (view: ChunkView) => {
    setChunkView(view)
    if (selectedChunk) {
      const content = view === 'content'
        ? selectedChunk.chunk.content
        : JSON.stringify(selectedChunk.chunk.metadata || {}, null, 2)
      setSelectedChunk({ ...selectedChunk, view })
      setEditedContent(content)
      setHasChanges(false)
    }
  }

  // Handle save
  const handleSave = () => {
    if (!selectedChunk || !hasChanges) return

    try {
      if (chunkView === 'content') {
        updateChunkMutation.mutate({
          chunkId: selectedChunk.chunk.id,
          content: editedContent,
        })
      } else {
        const metadata = JSON.parse(editedContent)
        updateChunkMutation.mutate({
          chunkId: selectedChunk.chunk.id,
          metadata,
        })
      }
    } catch {
      toast({ title: 'Invalid JSON format', variant: 'destructive' })
    }
  }

  // Handle editor change
  const handleEditorChange = (value: string | undefined) => {
    if (value !== undefined && activeTab === 'chunks' && selectedChunk) {
      setEditedContent(value)
      const originalContent = chunkView === 'content'
        ? selectedChunk.chunk.content
        : JSON.stringify(selectedChunk.chunk.metadata || {}, null, 2)
      setHasChanges(value !== originalContent)
    }
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

  const tabConfig: { key: DocumentTab; label: string; icon: typeof FileCode }[] = [
    { key: 'info', label: 'Info', icon: Info },
    { key: 'extract', label: 'Extract', icon: FileText },
    { key: 'images', label: 'Images', icon: ImageIcon },
    { key: 'chunks', label: 'Chunks', icon: Layers },
    { key: 'qa', label: 'Q&A', icon: MessageSquare },
  ]

  return (
    <div className="flex flex-col h-[calc(100vh-4rem)]">
      {/* Header */}
      <div className="flex items-center justify-between p-4 border-b shrink-0">
        <div className="flex items-center gap-4">
          <Button variant="ghost" size="icon" onClick={() => navigate('/documents')}>
            <ArrowLeft className="h-5 w-5" />
          </Button>
          <div>
            <h2 className="text-xl font-bold tracking-tight">{document.title}</h2>
            <div className="flex items-center gap-2 mt-1">
              <Badge variant={statusVariants[document.status] || 'default'}>
                <StatusIcon className={cn('h-3 w-3 mr-1', document.status === 'Processing' && 'animate-spin')} />
                {document.status}
              </Badge>
              <span className="text-sm text-muted-foreground">
                {document.sourceType} • {formatBytes(document.fileSize || 0)} • {chunks.length} chunks
              </span>
            </div>
          </div>
        </div>
        <div className="flex gap-2">
          {hasChanges && activeTab === 'chunks' && (
            <Button
              onClick={handleSave}
              disabled={updateChunkMutation.isPending}
            >
              {updateChunkMutation.isPending ? (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              ) : (
                <Save className="h-4 w-4 mr-2" />
              )}
              Save Changes
            </Button>
          )}
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

      {/* Main Content */}
      <Tabs
        value={activeTab}
        onValueChange={(v) => {
          setActiveTab(v as DocumentTab)
          setSelectedChunk(null)
          setHasChanges(false)
        }}
        className="flex flex-col flex-1 overflow-hidden"
      >
        {/* Tab Bar */}
        <div className="border-b px-4">
          <TabsList className="h-auto bg-transparent p-0">
            {tabConfig.map(({ key, label, icon: Icon }) => (
              <TabsTrigger
                key={key}
                value={key}
                className="rounded-none border-b-2 border-transparent data-[state=active]:border-primary data-[state=active]:bg-transparent px-4 py-2"
              >
                <Icon className="h-4 w-4 mr-2" />
                {label}
                {key === 'chunks' && (
                  <Badge variant="outline" className="ml-2 text-xs">
                    {chunks.length}
                  </Badge>
                )}
                {key === 'qa' && document.qaPairs && (
                  <Badge variant="outline" className="ml-2 text-xs">
                    {document.qaPairs.length}
                  </Badge>
                )}
              </TabsTrigger>
            ))}
          </TabsList>
        </div>

        {/* Info Tab - Document Properties */}
        <TabsContent value="info" className="flex-1 m-0 overflow-auto">
          <div className="p-6 max-w-4xl mx-auto space-y-6">
            {/* Basic Info Card */}
            <Card>
              <CardHeader>
                <CardTitle className="text-lg">Document Information</CardTitle>
                <CardDescription>Basic properties and metadata</CardDescription>
              </CardHeader>
              <CardContent className="space-y-0">
                <InfoRow icon={Hash} label="Document ID" value={document.id} copyable />
                <InfoRow icon={FileText} label="Title" value={document.title} />
                <InfoRow icon={FileType} label="Source Type" value={document.sourceType} />
                <InfoRow icon={FolderOpen} label="Source Path" value={document.sourcePath} copyable />
                <InfoRow icon={HardDrive} label="File Size" value={formatBytes(document.fileSize || 0)} />
                <InfoRow icon={Layers} label="Chunk Count" value={String(document.chunkCount || chunks.length)} />
                <InfoRow
                  icon={Hash}
                  label="Content Hash"
                  value={document.contentHash ? `${document.contentHash.substring(0, 16)}...` : null}
                  copyable
                />
              </CardContent>
            </Card>

            {/* Timestamps Card */}
            <Card>
              <CardHeader>
                <CardTitle className="text-lg">Timestamps</CardTitle>
              </CardHeader>
              <CardContent className="space-y-0">
                <InfoRow
                  icon={Calendar}
                  label="Created"
                  value={new Date(document.createdAt).toLocaleString()}
                />
                <InfoRow
                  icon={Calendar}
                  label="Updated"
                  value={new Date(document.updatedAt).toLocaleString()}
                />
                <InfoRow
                  icon={Calendar}
                  label="Indexed"
                  value={document.indexedAt ? new Date(document.indexedAt).toLocaleString() : 'Not indexed'}
                />
              </CardContent>
            </Card>

            {/* Processing Metadata Card (from FileFlux) */}
            {documentMetadata && (
              <Card>
                <CardHeader>
                  <CardTitle className="text-lg">Processing Metadata</CardTitle>
                  <CardDescription>FileFlux extraction properties</CardDescription>
                </CardHeader>
                <CardContent>
                  <Editor
                    height="150px"
                    language="json"
                    value={JSON.stringify(documentMetadata, null, 2)}
                    theme="vs-dark"
                    options={{
                      readOnly: true,
                      minimap: { enabled: false },
                      fontSize: 13,
                      lineNumbers: 'off',
                      scrollBeyondLastLine: false,
                      automaticLayout: true,
                      padding: { top: 8, bottom: 8 },
                    }}
                  />
                </CardContent>
              </Card>
            )}

            {/* Custom Metadata Card */}
            {document.metadata && Object.keys(document.metadata).length > 0 && (
              <Card>
                <CardHeader>
                  <CardTitle className="text-lg">Custom Metadata</CardTitle>
                  <CardDescription>User-defined metadata</CardDescription>
                </CardHeader>
                <CardContent>
                  <Editor
                    height="200px"
                    language="json"
                    value={JSON.stringify(document.metadata, null, 2)}
                    theme="vs-dark"
                    options={{
                      readOnly: true,
                      minimap: { enabled: false },
                      fontSize: 13,
                      lineNumbers: 'off',
                      scrollBeyondLastLine: false,
                      automaticLayout: true,
                      padding: { top: 8, bottom: 8 },
                    }}
                  />
                </CardContent>
              </Card>
            )}
          </div>
        </TabsContent>

        {/* Extract Tab */}
        <TabsContent value="extract" className="flex-1 m-0 overflow-hidden">
          <div className="h-full p-0">
            <Editor
              height="100%"
              language="markdown"
              value={document?.extractedContent || '(No extracted content available)'}
              theme="vs-dark"
              options={{
                readOnly: true,
                minimap: { enabled: false },
                fontSize: 13,
                lineNumbers: 'off',
                wordWrap: 'on',
                scrollBeyondLastLine: false,
                automaticLayout: true,
                padding: { top: 16, bottom: 16 },
              }}
            />
          </div>
        </TabsContent>

        {/* Images Tab */}
        <TabsContent value="images" className="flex-1 m-0 overflow-auto">
          <div className="flex flex-col items-center justify-center h-full text-muted-foreground">
            <ImageIcon className="h-16 w-16 mb-4 opacity-50" />
            <h3 className="text-lg font-medium">No images extracted</h3>
            <p className="text-sm mt-1 max-w-md text-center">
              Image extraction from documents will be available in a future update.
              Currently, images embedded in PDFs, DOCX, and HTML files can be processed.
            </p>
          </div>
        </TabsContent>

        {/* Q&A Tab */}
        <TabsContent value="qa" className="flex-1 m-0 overflow-hidden">
          <div className="h-full flex flex-col">
            {/* Q&A Header with Generate Button */}
            <div className="flex items-center justify-between px-4 py-3 border-b bg-muted/30">
              <div className="flex items-center gap-2">
                <MessageSquare className="h-4 w-4" />
                <span className="font-medium">
                  Q&A Pairs ({document.qaPairs?.length || 0})
                </span>
              </div>
              <Button
                onClick={() => generateQAMutation.mutate()}
                disabled={generateQAMutation.isPending || !document.extractedContent}
                size="sm"
              >
                {generateQAMutation.isPending ? (
                  <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                ) : (
                  <Sparkles className="h-4 w-4 mr-2" />
                )}
                Generate Q&A
              </Button>
            </div>

            {/* Q&A Content */}
            <div className="flex-1">
              {document.qaPairs && document.qaPairs.length > 0 ? (
                <ScrollArea className="h-full">
                  <div className="p-4 space-y-4">
                    {document.qaPairs.map((qa, index) => (
                      <Card key={index}>
                        <CardHeader className="pb-2">
                          <CardTitle className="text-sm font-medium flex items-center gap-2">
                            <Badge variant="outline">Q{index + 1}</Badge>
                            {qa.question}
                          </CardTitle>
                        </CardHeader>
                        <CardContent>
                          <p className="text-sm text-muted-foreground">{qa.answer}</p>
                        </CardContent>
                      </Card>
                    ))}
                  </div>
                </ScrollArea>
              ) : (
                <div className="flex flex-col items-center justify-center h-full text-muted-foreground">
                  <MessageSquare className="h-16 w-16 mb-4 opacity-50" />
                  <h3 className="text-lg font-medium">No Q&A pairs generated</h3>
                  <p className="text-sm mt-1 mb-4 max-w-md text-center">
                    Generate question-answer pairs from the document content using AI.
                  </p>
                  <Button
                    onClick={() => generateQAMutation.mutate()}
                    disabled={generateQAMutation.isPending || !document.extractedContent}
                  >
                    {generateQAMutation.isPending ? (
                      <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                    ) : (
                      <Sparkles className="h-4 w-4 mr-2" />
                    )}
                    Generate Q&A Pairs
                  </Button>
                </div>
              )}
            </div>
          </div>
        </TabsContent>

        {/* Chunks Tab */}
        <TabsContent value="chunks" className="flex-1 m-0 overflow-hidden">
          <ResizablePanelGroup direction="horizontal" className="h-full">
            {/* Left Panel - Chunk List */}
            <ResizablePanel defaultSize={30} minSize={20} maxSize={50}>
              <div className="flex flex-col h-full border-r">
                <ScrollArea className="flex-1">
                  <div className="p-2 space-y-1">
                    {chunks.map((chunk) => (
                      <button
                        key={chunk.id}
                        onClick={() => handleSelectChunk(chunk)}
                        className={cn(
                          'w-full text-left p-3 rounded-lg border transition-colors',
                          selectedChunk?.chunk.id === chunk.id
                            ? 'border-primary bg-primary/5'
                            : 'border-transparent hover:bg-muted/50'
                        )}
                      >
                        <div className="flex items-center justify-between mb-1">
                          <span className="font-medium text-sm">Chunk #{chunk.chunkIndex + 1}</span>
                          <Badge variant="outline" className="text-xs">
                            {chunk.tokenCount} tokens
                          </Badge>
                        </div>
                        <p className="text-xs text-muted-foreground line-clamp-2">
                          {chunk.content.substring(0, 100)}
                          {chunk.content.length > 100 ? '...' : ''}
                        </p>
                      </button>
                    ))}

                    {chunks.length === 0 && (
                      <div className="text-center py-8 text-muted-foreground">
                        <Layers className="h-8 w-8 mx-auto mb-2" />
                        <p className="text-sm">No chunks available</p>
                      </div>
                    )}
                  </div>
                </ScrollArea>
              </div>
            </ResizablePanel>

            <ResizableHandle withHandle />

            {/* Right Panel - Editor */}
            <ResizablePanel defaultSize={70}>
              <div className="flex flex-col h-full">
                {selectedChunk ? (
                  <>
                    {/* Chunk View Toggle */}
                    <div className="flex items-center justify-between px-4 py-2 border-b bg-muted/30">
                      <div className="flex items-center gap-2">
                        <span className="font-medium">Chunk #{selectedChunk.chunk.chunkIndex + 1}</span>
                        {hasChanges && (
                          <Badge variant="warning" className="text-xs">
                            Modified
                          </Badge>
                        )}
                      </div>
                      <div className="flex items-center gap-2">
                        <Button
                          variant={chunkView === 'content' ? 'default' : 'ghost'}
                          size="sm"
                          onClick={() => handleChunkViewChange('content')}
                        >
                          <FileCode className="h-4 w-4 mr-1" />
                          Content
                        </Button>
                        <Button
                          variant={chunkView === 'metadata' ? 'default' : 'ghost'}
                          size="sm"
                          onClick={() => handleChunkViewChange('metadata')}
                        >
                          <Database className="h-4 w-4 mr-1" />
                          Metadata
                        </Button>
                      </div>
                    </div>

                    {/* Monaco Editor */}
                    <div className="flex-1">
                      <Editor
                        height="100%"
                        language={chunkView === 'content' ? 'markdown' : 'json'}
                        value={editedContent}
                        onChange={handleEditorChange}
                        theme="vs-dark"
                        options={{
                          readOnly: false,
                          minimap: { enabled: false },
                          fontSize: 13,
                          lineNumbers: 'on',
                          wordWrap: 'on',
                          scrollBeyondLastLine: false,
                          automaticLayout: true,
                          padding: { top: 8, bottom: 8 },
                        }}
                      />
                    </div>
                  </>
                ) : (
                  <div className="flex flex-col items-center justify-center h-full text-muted-foreground">
                    <Layers className="h-16 w-16 mb-4 opacity-50" />
                    <h3 className="text-lg font-medium">Select a chunk to view</h3>
                    <p className="text-sm mt-1">
                      Choose a chunk from the list to view or edit its content
                    </p>
                  </div>
                )}
              </div>
            </ResizablePanel>
          </ResizablePanelGroup>
        </TabsContent>
      </Tabs>
    </div>
  )
}
