import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { collectionsApi, type Collection } from '@/lib/api'
import { formatDate } from '@/lib/utils'
import { Plus, FolderOpen, Trash2, Edit } from 'lucide-react'
import { useToast } from '@/hooks/use-toast'

export default function CollectionsPage() {
  const [showCreate, setShowCreate] = useState(false)
  const [newName, setNewName] = useState('')
  const [newDescription, setNewDescription] = useState('')
  const { toast } = useToast()
  const queryClient = useQueryClient()

  const { data: collectionsResponse, isLoading } = useQuery({
    queryKey: ['collections'],
    queryFn: () => collectionsApi.getAll(),
    select: (response) => response.data.data,
  })

  const createMutation = useMutation({
    mutationFn: (data: { name: string; description?: string }) =>
      collectionsApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['collections'] })
      setShowCreate(false)
      setNewName('')
      setNewDescription('')
      toast({ title: 'Collection created successfully' })
    },
    onError: () => {
      toast({ title: 'Failed to create collection', variant: 'destructive' })
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => collectionsApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['collections'] })
      toast({ title: 'Collection deleted successfully' })
    },
    onError: () => {
      toast({ title: 'Failed to delete collection', variant: 'destructive' })
    },
  })

  const collections = collectionsResponse as Collection[] | undefined

  const handleCreate = () => {
    if (!newName.trim()) return
    createMutation.mutate({ name: newName, description: newDescription || undefined })
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
          <h2 className="text-3xl font-bold tracking-tight">Collections</h2>
          <p className="text-muted-foreground">
            Organize your documents into collections
          </p>
        </div>
        <Button onClick={() => setShowCreate(true)}>
          <Plus className="mr-2 h-4 w-4" />
          New Collection
        </Button>
      </div>

      {showCreate && (
        <Card>
          <CardHeader>
            <CardTitle>Create New Collection</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <label className="text-sm font-medium">Name</label>
              <Input
                placeholder="Collection name"
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
              />
            </div>
            <div>
              <label className="text-sm font-medium">Description (optional)</label>
              <Input
                placeholder="Description"
                value={newDescription}
                onChange={(e) => setNewDescription(e.target.value)}
              />
            </div>
            <div className="flex space-x-2">
              <Button onClick={handleCreate} disabled={createMutation.isPending}>
                {createMutation.isPending ? 'Creating...' : 'Create'}
              </Button>
              <Button variant="outline" onClick={() => setShowCreate(false)}>
                Cancel
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {collections?.map((collection) => (
          <Card key={collection.id}>
            <CardHeader className="flex flex-row items-start justify-between space-y-0">
              <div className="flex items-center space-x-2">
                <FolderOpen className="h-5 w-5 text-muted-foreground" />
                <div>
                  <CardTitle className="text-lg">{collection.name}</CardTitle>
                  {collection.description && (
                    <CardDescription>{collection.description}</CardDescription>
                  )}
                </div>
              </div>
              <div className="flex space-x-1">
                <Button variant="ghost" size="icon">
                  <Edit className="h-4 w-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => deleteMutation.mutate(collection.id)}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              <div className="space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-muted-foreground">Documents</span>
                  <span className="font-medium">{collection.documentCount}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-muted-foreground">Chunk Size</span>
                  <span className="font-medium">{collection.settings.chunkSize}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-muted-foreground">Created</span>
                  <span className="font-medium">{formatDate(collection.createdAt)}</span>
                </div>
              </div>
            </CardContent>
          </Card>
        ))}

        {(!collections || collections.length === 0) && !showCreate && (
          <Card className="col-span-full">
            <CardContent className="flex flex-col items-center justify-center py-12">
              <FolderOpen className="h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-medium">No collections yet</h3>
              <p className="text-sm text-muted-foreground mb-4">
                Create a collection to organize your documents
              </p>
              <Button onClick={() => setShowCreate(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Create Collection
              </Button>
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  )
}
