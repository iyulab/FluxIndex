import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useStore } from '@/store/useStore'
import { useToast } from '@/hooks/use-toast'
import { apiKeysApi, collectionsApi } from '@/lib/api'
import { formatDate } from '@/lib/utils'
import {
  Key,
  Moon,
  Sun,
  Trash2,
  Plus,
  Copy,
  Check,
  Eye,
  EyeOff,
  RefreshCw,
  Loader2,
  Shield,
  ShieldOff,
  FolderOpen,
  AlertTriangle,
} from 'lucide-react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'

export default function SettingsPage() {
  const { theme, setTheme, setApiKey, apiKey: storedApiKey, selectedCollectionId, setSelectedCollectionId } = useStore()
  const { toast } = useToast()
  const queryClient = useQueryClient()

  // State for dialogs
  const [showCreateDialog, setShowCreateDialog] = useState(false)
  const [showNewKeyDialog, setShowNewKeyDialog] = useState(false)
  const [deleteKeyId, setDeleteKeyId] = useState<string | null>(null)
  const [deleteCollectionId, setDeleteCollectionId] = useState<string | null>(null)
  const [newKeyName, setNewKeyName] = useState('')
  const [generatedKey, setGeneratedKey] = useState('')
  const [copied, setCopied] = useState(false)
  const [showKey, setShowKey] = useState(false)

  // Check if we have a stored API key
  const hasActiveKey = !!storedApiKey && storedApiKey.trim().length > 0
  const isDevelopment = import.meta.env.DEV

  // Fetch API keys
  const { data: apiKeys, isLoading: isLoadingApiKeys } = useQuery({
    queryKey: ['apiKeys'],
    queryFn: async () => {
      const response = await apiKeysApi.getAll()
      return response.data.data || []
    },
  })

  // Fetch collections
  const { data: collections, isLoading: isLoadingCollections } = useQuery({
    queryKey: ['collections'],
    queryFn: async () => {
      const response = await collectionsApi.getAll()
      return response.data.data || []
    },
  })

  // Create API key mutation
  const createMutation = useMutation({
    mutationFn: (name: string) => apiKeysApi.create({ name, role: 'Admin' }),
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: ['apiKeys'] })
      const keyData = response.data.data
      if (keyData) {
        setGeneratedKey(keyData.rawKey)
        setShowNewKeyDialog(true)
        // Auto-set as active key
        setApiKey(keyData.rawKey)
        localStorage.setItem('fluxindex-api-key', keyData.rawKey)
      }
      setShowCreateDialog(false)
      setNewKeyName('')
    },
    onError: (error: Error) => {
      toast({
        title: 'Failed to create API key',
        description: error.message,
        variant: 'destructive',
      })
    },
  })

  // Delete API key mutation
  const deleteMutation = useMutation({
    mutationFn: (id: string) => apiKeysApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apiKeys'] })
      toast({ title: 'API key deleted successfully' })
      setDeleteKeyId(null)
    },
    onError: (error: Error) => {
      toast({
        title: 'Failed to delete API key',
        description: error.message,
        variant: 'destructive',
      })
    },
  })

  // Activate/Deactivate mutation
  const toggleActiveMutation = useMutation({
    mutationFn: ({ id, activate }: { id: string; activate: boolean }) =>
      activate ? apiKeysApi.activate(id) : apiKeysApi.deactivate(id),
    onSuccess: (_, { activate }) => {
      queryClient.invalidateQueries({ queryKey: ['apiKeys'] })
      toast({ title: `API key ${activate ? 'activated' : 'deactivated'} successfully` })
    },
    onError: (error: Error) => {
      toast({
        title: 'Failed to update API key',
        description: error.message,
        variant: 'destructive',
      })
    },
  })

  // Delete collection mutation
  const deleteCollectionMutation = useMutation({
    mutationFn: (id: string) => collectionsApi.delete(id),
    onSuccess: (_, deletedId) => {
      queryClient.invalidateQueries({ queryKey: ['collections'] })
      // If deleted collection was selected, clear selection
      if (selectedCollectionId === deletedId) {
        setSelectedCollectionId(null)
      }
      toast({ title: 'Collection deleted successfully' })
      setDeleteCollectionId(null)
    },
    onError: (error: Error) => {
      toast({
        title: 'Failed to delete collection',
        description: error.message,
        variant: 'destructive',
      })
    },
  })

  const handleCreate = () => {
    if (!newKeyName.trim()) return
    createMutation.mutate(newKeyName)
  }

  const handleCopyKey = async () => {
    await navigator.clipboard.writeText(generatedKey)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
    toast({ title: 'API key copied to clipboard' })
  }

  const handleUseKey = (keyPrefix: string) => {
    // Find the key and show instructions since we don't have the raw key
    toast({
      title: 'Cannot retrieve full key',
      description: `The key starting with "${keyPrefix}..." was stored at creation. Generate a new key if needed.`,
    })
  }

  const toggleTheme = () => {
    setTheme(theme === 'light' ? 'dark' : 'light')
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">Settings</h2>
        <p className="text-muted-foreground">
          Configure your FluxIndex Service settings
        </p>
      </div>

      {/* Current Auth Status */}
      {isDevelopment && (
        <Card className="border-blue-200 bg-blue-50">
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3">
                <Shield className="h-5 w-5 text-blue-600" />
                <div>
                  <p className="font-medium text-blue-900">Development Mode</p>
                  <p className="text-sm text-blue-700">
                    {hasActiveKey
                      ? 'Using stored API key for authentication'
                      : 'No API key set - using automatic Admin access'}
                  </p>
                </div>
              </div>
              {hasActiveKey && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    setApiKey(null)
                    toast({ title: 'API key cleared - now using dev mode bypass' })
                  }}
                >
                  Clear Key
                </Button>
              )}
            </div>
          </CardContent>
        </Card>
      )}

      {/* API Keys Management */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="flex items-center space-x-2">
                <Key className="h-5 w-5" />
                <span>API Keys</span>
              </CardTitle>
              <CardDescription>
                Manage API keys for authentication. Keys are generated securely and shown only once.
              </CardDescription>
            </div>
            <Button onClick={() => setShowCreateDialog(true)}>
              <Plus className="mr-2 h-4 w-4" />
              Generate New Key
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {isLoadingApiKeys ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          ) : apiKeys && apiKeys.length > 0 ? (
            <div className="space-y-3">
              {apiKeys.map((key) => (
                <div
                  key={key.id}
                  className="flex items-center justify-between p-4 border rounded-lg"
                >
                  <div className="flex items-center gap-4">
                    <div
                      className={`p-2 rounded-full ${
                        key.isActive ? 'bg-green-100 text-green-600' : 'bg-gray-100 text-gray-400'
                      }`}
                    >
                      {key.isActive ? (
                        <Shield className="h-4 w-4" />
                      ) : (
                        <ShieldOff className="h-4 w-4" />
                      )}
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-medium">{key.name}</span>
                        <span
                          className={`text-xs px-2 py-0.5 rounded-full ${
                            key.isActive
                              ? 'bg-green-100 text-green-700'
                              : 'bg-gray-100 text-gray-500'
                          }`}
                        >
                          {key.isActive ? 'Active' : 'Inactive'}
                        </span>
                        <span className="text-xs px-2 py-0.5 rounded-full bg-blue-100 text-blue-700">
                          {key.role}
                        </span>
                      </div>
                      <div className="flex items-center gap-4 text-sm text-muted-foreground mt-1">
                        <span className="font-mono">{key.keyPrefix}...</span>
                        <span>Created: {formatDate(key.createdAt)}</span>
                        {key.lastUsedAt && (
                          <span>Last used: {formatDate(key.lastUsedAt)}</span>
                        )}
                        {key.expiresAt && (
                          <span className="text-orange-500">
                            Expires: {formatDate(key.expiresAt)}
                          </span>
                        )}
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleUseKey(key.keyPrefix)}
                    >
                      <Copy className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() =>
                        toggleActiveMutation.mutate({
                          id: key.id,
                          activate: !key.isActive,
                        })
                      }
                      disabled={toggleActiveMutation.isPending}
                    >
                      {key.isActive ? (
                        <ShieldOff className="h-4 w-4" />
                      ) : (
                        <Shield className="h-4 w-4" />
                      )}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setDeleteKeyId(key.id)}
                    >
                      <Trash2 className="h-4 w-4 text-red-500" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-12">
              <Key className="h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-medium">No API keys yet</h3>
              <p className="text-sm text-muted-foreground mb-4">
                Generate your first API key to authenticate requests
              </p>
              <Button onClick={() => setShowCreateDialog(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Generate New Key
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Collections Management */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center space-x-2">
            <FolderOpen className="h-5 w-5" />
            <span>Collections</span>
          </CardTitle>
          <CardDescription>
            Manage your document collections. Deleting a collection will remove all its documents.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoadingCollections ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          ) : collections && collections.length > 0 ? (
            <div className="space-y-3">
              {collections.map((collection) => {
                const isSelected = selectedCollectionId === collection.id
                return (
                  <div
                    key={collection.id}
                    className={`flex items-center justify-between p-4 border rounded-lg ${
                      isSelected ? 'border-primary bg-primary/5' : ''
                    }`}
                  >
                    <div className="flex items-center gap-4">
                      <div className={`p-2 rounded-full ${isSelected ? 'bg-primary/10 text-primary' : 'bg-gray-100 text-gray-500'}`}>
                        <FolderOpen className="h-4 w-4" />
                      </div>
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="font-medium">{collection.name}</span>
                          {isSelected && (
                            <span className="text-xs px-2 py-0.5 rounded-full bg-primary/10 text-primary">
                              Current
                            </span>
                          )}
                        </div>
                        <div className="flex items-center gap-4 text-sm text-muted-foreground mt-1">
                          <span>{collection.documentCount} documents</span>
                          <span>Created: {formatDate(collection.createdAt)}</span>
                          {collection.description && (
                            <span className="truncate max-w-[200px]">{collection.description}</span>
                          )}
                        </div>
                      </div>
                    </div>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setDeleteCollectionId(collection.id)}
                      className="text-red-500 hover:text-red-600 hover:bg-red-50"
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                )
              })}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-8">
              <FolderOpen className="h-10 w-10 text-muted-foreground mb-3" />
              <p className="text-sm text-muted-foreground">No collections yet</p>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Appearance */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center space-x-2">
            {theme === 'light' ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
            <span>Appearance</span>
          </CardTitle>
          <CardDescription>
            Customize the look and feel of the application
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center justify-between">
            <div>
              <p className="font-medium">Theme</p>
              <p className="text-sm text-muted-foreground">
                Current theme: {theme === 'light' ? 'Light' : 'Dark'}
              </p>
            </div>
            <Button variant="outline" onClick={toggleTheme}>
              {theme === 'light' ? (
                <>
                  <Moon className="mr-2 h-4 w-4" />
                  Switch to Dark
                </>
              ) : (
                <>
                  <Sun className="mr-2 h-4 w-4" />
                  Switch to Light
                </>
              )}
            </Button>
          </div>
        </CardContent>
      </Card>

      {/* About */}
      <Card>
        <CardHeader>
          <CardTitle>About</CardTitle>
          <CardDescription>
            Information about FluxIndex Service
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Version</span>
              <span className="font-medium">0.1.0</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">API Version</span>
              <span className="font-medium">v1</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Documentation</span>
              <a href="/swagger" className="font-medium text-primary hover:underline">
                API Documentation
              </a>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Create API Key Dialog */}
      <Dialog open={showCreateDialog} onOpenChange={setShowCreateDialog}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Generate New API Key</DialogTitle>
            <DialogDescription>
              Create a new API key for authentication. The key will only be shown once.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <label className="text-sm font-medium">Key Name</label>
              <Input
                placeholder="e.g., Production Key, Development Key"
                value={newKeyName}
                onChange={(e) => setNewKeyName(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleCreate()}
                autoFocus
              />
              <p className="text-xs text-muted-foreground mt-1">
                A descriptive name to identify this key
              </p>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowCreateDialog(false)}>
              Cancel
            </Button>
            <Button
              onClick={handleCreate}
              disabled={createMutation.isPending || !newKeyName.trim()}
            >
              {createMutation.isPending ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Generating...
                </>
              ) : (
                <>
                  <RefreshCw className="mr-2 h-4 w-4" />
                  Generate Key
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* New Key Generated Dialog */}
      <Dialog open={showNewKeyDialog} onOpenChange={setShowNewKeyDialog}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2 text-green-600">
              <Check className="h-5 w-5" />
              API Key Generated
            </DialogTitle>
            <DialogDescription>
              Copy your API key now. You won't be able to see it again!
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="p-4 bg-muted rounded-lg">
              <div className="flex items-center justify-between gap-2">
                <code className="text-sm flex-1 break-all">
                  {showKey ? generatedKey : '•'.repeat(40)}
                </code>
                <div className="flex gap-1">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setShowKey(!showKey)}
                  >
                    {showKey ? (
                      <EyeOff className="h-4 w-4" />
                    ) : (
                      <Eye className="h-4 w-4" />
                    )}
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={handleCopyKey}
                  >
                    {copied ? (
                      <Check className="h-4 w-4 text-green-500" />
                    ) : (
                      <Copy className="h-4 w-4" />
                    )}
                  </Button>
                </div>
              </div>
            </div>
            <div className="p-3 bg-yellow-50 border border-yellow-200 rounded-lg text-yellow-800 text-sm">
              <strong>Important:</strong> This key has been automatically set as your active API key.
              Store it securely - it won't be shown again.
            </div>
          </div>
          <DialogFooter>
            <Button onClick={() => setShowNewKeyDialog(false)}>
              Done
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete API Key Confirmation Dialog */}
      <AlertDialog open={!!deleteKeyId} onOpenChange={() => setDeleteKeyId(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete API Key?</AlertDialogTitle>
            <AlertDialogDescription>
              This action cannot be undone. Any applications using this key will no longer be able to authenticate.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className="bg-red-500 hover:bg-red-600"
              onClick={() => deleteKeyId && deleteMutation.mutate(deleteKeyId)}
            >
              {deleteMutation.isPending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Trash2 className="mr-2 h-4 w-4" />
              )}
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Delete Collection Confirmation Dialog */}
      <AlertDialog open={!!deleteCollectionId} onOpenChange={() => setDeleteCollectionId(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle className="flex items-center gap-2">
              <AlertTriangle className="h-5 w-5 text-red-500" />
              Delete Collection?
            </AlertDialogTitle>
            <AlertDialogDescription>
              This action cannot be undone. All documents in this collection will be permanently deleted.
              {collections?.find(c => c.id === deleteCollectionId)?.documentCount ? (
                <span className="block mt-2 font-medium text-red-600">
                  This will delete {collections?.find(c => c.id === deleteCollectionId)?.documentCount} document(s).
                </span>
              ) : null}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className="bg-red-500 hover:bg-red-600"
              onClick={() => deleteCollectionId && deleteCollectionMutation.mutate(deleteCollectionId)}
            >
              {deleteCollectionMutation.isPending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Trash2 className="mr-2 h-4 w-4" />
              )}
              Delete Collection
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
