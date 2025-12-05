import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { documentsApi, type Document } from '@/lib/api'
import { formatDate, formatBytes, truncate } from '@/lib/utils'
import { Upload, FileText, Trash2, RefreshCw, CheckCircle, Clock, XCircle, AlertCircle } from 'lucide-react'
import { useToast } from '@/hooks/use-toast'

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

  const { data: docsResponse, isLoading } = useQuery({
    queryKey: ['documents', page],
    queryFn: () => documentsApi.getAll({ page, pageSize: 20 }),
    select: (response) => response.data.data,
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => documentsApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['documents'] })
      toast({ title: 'Document deleted successfully' })
    },
    onError: () => {
      toast({ title: 'Failed to delete document', variant: 'destructive' })
    },
  })

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files
    if (!files || files.length === 0) return

    const formData = new FormData()
    formData.append('file', files[0])
    formData.append('title', files[0].name)

    try {
      await documentsApi.upload(formData)
      queryClient.invalidateQueries({ queryKey: ['documents'] })
      toast({ title: 'Document uploaded successfully' })
    } catch {
      toast({ title: 'Failed to upload document', variant: 'destructive' })
    }
  }

  const documents = docsResponse as Document[] | undefined

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
            Manage and index your documents
          </p>
        </div>
        <div>
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

      <Card>
        <CardHeader>
          <CardTitle>All Documents</CardTitle>
          <CardDescription>
            {documents?.length ?? 0} documents in your system
          </CardDescription>
        </CardHeader>
        <CardContent>
          {documents && documents.length > 0 ? (
            <div className="space-y-4">
              {documents.map((doc) => {
                const StatusIcon = statusIcons[doc.status] || AlertCircle
                const statusColor = statusColors[doc.status] || 'text-gray-500'

                return (
                  <div
                    key={doc.id}
                    className="flex items-center justify-between p-4 border rounded-lg"
                  >
                    <div className="flex items-center space-x-4">
                      <FileText className="h-8 w-8 text-muted-foreground" />
                      <div>
                        <h4 className="font-medium">{truncate(doc.title, 50)}</h4>
                        <div className="flex items-center space-x-4 text-sm text-muted-foreground">
                          <span>{doc.sourceType || 'Unknown'}</span>
                          {doc.fileSize && <span>{formatBytes(doc.fileSize)}</span>}
                          <span>{doc.chunkCount} chunks</span>
                          <span>{formatDate(doc.createdAt)}</span>
                        </div>
                      </div>
                    </div>
                    <div className="flex items-center space-x-4">
                      <div className={`flex items-center space-x-1 ${statusColor}`}>
                        <StatusIcon className="h-4 w-4" />
                        <span className="text-sm">{doc.status}</span>
                      </div>
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() => deleteMutation.mutate(doc.id)}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
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
        </CardContent>
      </Card>
    </div>
  )
}
