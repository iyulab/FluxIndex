import { useState, useMemo } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import Editor from '@monaco-editor/react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ScrollArea } from '@/components/ui/scroll-area'
import { ResizablePanelGroup, ResizablePanel, ResizableHandle } from '@/components/ui/resizable'
import {
  documentsApi, chunksApi,
  type ChunkDetail, type DocumentChunk
} from '@/lib/api'
import { formatBytes, cn } from '@/lib/utils'
import {
  ArrowLeft, FileText, Clock, CheckCircle, XCircle, RefreshCw,
  Save, Loader2, RotateCcw, FileCode, Database, MessageSquare, Info
} from 'lucide-react'
import { useToast } from '@/hooks/use-toast'

const statusVariants: Record<string, 'default' | 'success' | 'warning' | 'destructive' | 'info'> = {
  Indexed: 'success',
  Pending: 'warning',
  Processing: 'info',
  Failed: 'destructive',
}

interface QAPair {
  question: string
  answer: string
}

type ContentType = 'chunk' | 'metadata' | 'qa' | 'extract'

interface SelectedItem {
  type: ContentType
  id: string
  title: string
  content: string
  language: string
  editable: boolean
  chunkIndex?: number
}

export default function DocumentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { toast } = useToast()
  const queryClient = useQueryClient()

  const [activeTab, setActiveTab] = useState<ContentType>('chunk')
  const [selectedItem, setSelectedItem] = useState<SelectedItem | null>(null)
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

  const chunks: (ChunkDetail | DocumentChunk)[] = chunksData?.items || document?.chunks || []

  // Build list items based on active tab
  const listItems = useMemo(() => {
    switch (activeTab) {
      case 'chunk':
        return chunks.map((chunk) => ({
          id: chunk.id,
          title: `Chunk #${chunk.chunkIndex + 1}`,
          subtitle: `${chunk.tokenCount} tokens`,
          preview: chunk.content.substring(0, 80) + (chunk.content.length > 80 ? '...' : ''),
          chunkIndex: chunk.chunkIndex,
        }))
      case 'metadata':
        return chunks.map((chunk) => ({
          id: `meta-${chunk.id}`,
          title: `Chunk #${chunk.chunkIndex + 1} Metadata`,
          subtitle: `${Object.keys(chunk.metadata || {}).length} fields`,
          preview: Object.keys(chunk.metadata || {}).slice(0, 3).join(', '),
          chunkIndex: chunk.chunkIndex,
          chunkId: chunk.id,
        }))
      case 'qa':
        return chunks.map((chunk) => {
          const qa = (chunk.metadata?.qa as QAPair[]) || []
          return {
            id: `qa-${chunk.id}`,
            title: `Chunk #${chunk.chunkIndex + 1} QA`,
            subtitle: `${qa.length} pairs`,
            preview: qa.length > 0 ? qa[0].question.substring(0, 50) : 'No QA pairs',
            chunkIndex: chunk.chunkIndex,
            chunkId: chunk.id,
          }
        })
      case 'extract':
        return chunks.map((chunk) => {
          const extracts = chunk.metadata?.extracts as string[] || []
          return {
            id: `extract-${chunk.id}`,
            title: `Chunk #${chunk.chunkIndex + 1} Extracts`,
            subtitle: `${extracts.length} items`,
            preview: extracts.length > 0 ? extracts[0].substring(0, 50) : 'No extracts',
            chunkIndex: chunk.chunkIndex,
            chunkId: chunk.id,
          }
        })
      default:
        return []
    }
  }, [activeTab, chunks])

  // Handle item selection
  const handleSelectItem = (item: typeof listItems[0]) => {
    let content = ''
    let language = 'plaintext'
    let editable = true
    let chunkIndex = item.chunkIndex

    const chunk = chunks.find(c =>
      c.id === item.id ||
      item.id === `meta-${c.id}` ||
      item.id === `qa-${c.id}` ||
      item.id === `extract-${c.id}`
    )

    switch (activeTab) {
      case 'chunk':
        content = chunk?.content || ''
        language = 'plaintext'
        editable = true
        break
      case 'metadata':
        content = JSON.stringify(chunk?.metadata || {}, null, 2)
        language = 'json'
        editable = true
        break
      case 'qa':
        const qa = (chunk?.metadata?.qa as QAPair[]) || []
        content = JSON.stringify(qa, null, 2)
        language = 'json'
        editable = true
        break
      case 'extract':
        const extracts = (chunk?.metadata?.extracts as string[]) || []
        content = JSON.stringify(extracts, null, 2)
        language = 'json'
        editable = true
        break
    }

    setSelectedItem({
      type: activeTab,
      id: chunk?.id || item.id,
      title: item.title,
      content,
      language,
      editable,
      chunkIndex,
    })
    setEditedContent(content)
    setHasChanges(false)
  }

  // Handle save
  const handleSave = () => {
    if (!selectedItem || !hasChanges) return

    const chunk = chunks.find(c => c.id === selectedItem.id)
    if (!chunk) return

    try {
      switch (selectedItem.type) {
        case 'chunk':
          updateChunkMutation.mutate({
            chunkId: selectedItem.id,
            content: editedContent,
          })
          break
        case 'metadata':
          const metadata = JSON.parse(editedContent)
          updateChunkMutation.mutate({
            chunkId: selectedItem.id,
            metadata,
          })
          break
        case 'qa':
          const qa = JSON.parse(editedContent)
          updateChunkMutation.mutate({
            chunkId: selectedItem.id,
            metadata: { ...chunk.metadata, qa },
          })
          break
        case 'extract':
          const extracts = JSON.parse(editedContent)
          updateChunkMutation.mutate({
            chunkId: selectedItem.id,
            metadata: { ...chunk.metadata, extracts },
          })
          break
      }
    } catch {
      toast({ title: 'Invalid JSON format', variant: 'destructive' })
    }
  }

  // Handle editor change
  const handleEditorChange = (value: string | undefined) => {
    if (value !== undefined) {
      setEditedContent(value)
      setHasChanges(value !== selectedItem?.content)
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

  const tabIcons: Record<ContentType, typeof FileCode> = {
    chunk: FileCode,
    metadata: Database,
    qa: MessageSquare,
    extract: Info,
  }

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
          {hasChanges && (
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

      {/* Main Content - Split Panel */}
      <ResizablePanelGroup direction="horizontal" className="flex-1">
        {/* Left Panel - List */}
        <ResizablePanel defaultSize={30} minSize={20} maxSize={50}>
          <div className="flex flex-col h-full border-r">
            {/* Tabs */}
            <Tabs value={activeTab} onValueChange={(v) => {
              setActiveTab(v as ContentType)
              setSelectedItem(null)
              setHasChanges(false)
            }} className="flex flex-col h-full">
              <TabsList className="w-full justify-start rounded-none border-b bg-transparent h-auto p-0">
                {(['chunk', 'metadata', 'qa', 'extract'] as ContentType[]).map((tab) => {
                  const Icon = tabIcons[tab]
                  return (
                    <TabsTrigger
                      key={tab}
                      value={tab}
                      className="rounded-none border-b-2 border-transparent data-[state=active]:border-primary data-[state=active]:bg-transparent px-4 py-2"
                    >
                      <Icon className="h-4 w-4 mr-1" />
                      {tab.charAt(0).toUpperCase() + tab.slice(1)}
                    </TabsTrigger>
                  )
                })}
              </TabsList>

              <TabsContent value={activeTab} className="flex-1 m-0 overflow-hidden">
                <ScrollArea className="h-full">
                  <div className="p-2 space-y-1">
                    {listItems.map((item) => (
                      <button
                        key={item.id}
                        onClick={() => handleSelectItem(item)}
                        className={cn(
                          'w-full text-left p-3 rounded-lg border transition-colors',
                          selectedItem?.id === item.id || selectedItem?.id === item.id.replace(/^(meta|qa|extract)-/, '')
                            ? 'border-primary bg-primary/5'
                            : 'border-transparent hover:bg-muted/50'
                        )}
                      >
                        <div className="flex items-center justify-between mb-1">
                          <span className="font-medium text-sm">{item.title}</span>
                          <Badge variant="outline" className="text-xs">
                            {item.subtitle}
                          </Badge>
                        </div>
                        <p className="text-xs text-muted-foreground line-clamp-2">
                          {item.preview}
                        </p>
                      </button>
                    ))}

                    {listItems.length === 0 && (
                      <div className="text-center py-8 text-muted-foreground">
                        <Info className="h-8 w-8 mx-auto mb-2" />
                        <p className="text-sm">No items available</p>
                      </div>
                    )}
                  </div>
                </ScrollArea>
              </TabsContent>
            </Tabs>
          </div>
        </ResizablePanel>

        <ResizableHandle withHandle />

        {/* Right Panel - Monaco Editor */}
        <ResizablePanel defaultSize={70}>
          <div className="flex flex-col h-full">
            {selectedItem ? (
              <>
                {/* Editor Header */}
                <div className="flex items-center justify-between px-4 py-2 border-b bg-muted/30">
                  <div className="flex items-center gap-2">
                    <span className="font-medium">{selectedItem.title}</span>
                    {hasChanges && (
                      <Badge variant="warning" className="text-xs">
                        Modified
                      </Badge>
                    )}
                  </div>
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <span>{selectedItem.language.toUpperCase()}</span>
                    {selectedItem.editable && (
                      <Badge variant="outline" className="text-xs">
                        Editable
                      </Badge>
                    )}
                  </div>
                </div>

                {/* Monaco Editor */}
                <div className="flex-1">
                  <Editor
                    height="100%"
                    language={selectedItem.language}
                    value={editedContent}
                    onChange={handleEditorChange}
                    theme="vs-dark"
                    options={{
                      readOnly: !selectedItem.editable,
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
                <FileCode className="h-16 w-16 mb-4 opacity-50" />
                <h3 className="text-lg font-medium">Select an item to view</h3>
                <p className="text-sm mt-1">
                  Choose a chunk, metadata, QA, or extract from the list
                </p>
              </div>
            )}
          </div>
        </ResizablePanel>
      </ResizablePanelGroup>
    </div>
  )
}
