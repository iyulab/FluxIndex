import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Badge } from '@/components/ui/badge'
import { Progress } from '@/components/ui/progress'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible'
import { vaultApi, type WatchedFolder, type VaultStatus } from '@/lib/api'
import { formatDate } from '@/lib/utils'
import {
  FolderSync,
  Plus,
  Trash2,
  RefreshCw,
  Play,
  Pause,
  CheckCircle,
  Clock,
  AlertCircle,
  XCircle,
  FolderOpen,
  FileText,
  ChevronDown,
  ChevronRight,
  Loader2,
  HardDrive,
  Eye,
} from 'lucide-react'
import { useToast } from '@/hooks/use-toast'

const statusColors: Record<string, string> = {
  Active: 'bg-green-500',
  Paused: 'bg-yellow-500',
  Error: 'bg-red-500',
  Untracked: 'bg-gray-400',
  Queued: 'bg-blue-500',
  Processing: 'bg-blue-600',
  Memorized: 'bg-green-500',
  Stale: 'bg-orange-500',
  Orphaned: 'bg-gray-500',
}

const statusIcons: Record<string, typeof CheckCircle> = {
  Active: CheckCircle,
  Paused: Pause,
  Error: XCircle,
  Memorized: CheckCircle,
  Queued: Clock,
  Processing: RefreshCw,
  Stale: AlertCircle,
  Orphaned: XCircle,
}

export default function VaultPage() {
  const { toast } = useToast()
  const queryClient = useQueryClient()
  const [addDialogOpen, setAddDialogOpen] = useState(false)
  const [expandedFolders, setExpandedFolders] = useState<Set<string>>(new Set())
  const [newFolder, setNewFolder] = useState({
    path: '',
    name: '',
    isRecursive: true,
    autoMemorize: true,
  })

  // Fetch vault status
  const { data: status, isLoading: statusLoading } = useQuery({
    queryKey: ['vault', 'status'],
    queryFn: async () => {
      const response = await vaultApi.getStatus()
      return response.data.data
    },
    refetchInterval: (query) => {
      const s = query.state.data as VaultStatus | undefined
      // Auto-refresh if there are queued or processing files
      return (s?.queuedFiles || 0) + (s?.processingFiles || 0) > 0 ? 3000 : 10000
    },
  })

  // Fetch watched folders
  const { data: folders, isLoading: foldersLoading } = useQuery({
    queryKey: ['vault', 'folders'],
    queryFn: async () => {
      const response = await vaultApi.getFolders()
      return response.data.data || []
    },
    refetchInterval: 10000,
  })

  // Add folder mutation
  const addFolderMutation = useMutation({
    mutationFn: (data: typeof newFolder) => vaultApi.addFolder(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['vault'] })
      setAddDialogOpen(false)
      setNewFolder({ path: '', name: '', isRecursive: true, autoMemorize: true })
      toast({ title: 'Folder added', description: 'Watched folder has been added successfully.' })
    },
    onError: (error: Error) => {
      toast({ title: 'Error', description: error.message, variant: 'destructive' })
    },
  })

  // Remove folder mutation
  const removeFolderMutation = useMutation({
    mutationFn: (id: string) => vaultApi.removeFolder(id, true),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['vault'] })
      toast({ title: 'Folder removed', description: 'Watched folder has been removed.' })
    },
    onError: (error: Error) => {
      toast({ title: 'Error', description: error.message, variant: 'destructive' })
    },
  })

  // Scan folder mutation
  const scanFolderMutation = useMutation({
    mutationFn: (id: string) => vaultApi.scanFolder(id),
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: ['vault'] })
      const result = response.data.data
      toast({
        title: 'Scan complete',
        description: `Found ${result?.totalFilesFound} files, queued ${result?.newFilesQueued} new files.`,
      })
    },
    onError: (error: Error) => {
      toast({ title: 'Scan failed', description: error.message, variant: 'destructive' })
    },
  })

  // Pause/Resume folder mutation
  const toggleFolderMutation = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'pause' | 'resume' }) =>
      action === 'pause' ? vaultApi.pauseFolder(id) : vaultApi.resumeFolder(id),
    onSuccess: (_, { action }) => {
      queryClient.invalidateQueries({ queryKey: ['vault'] })
      toast({ title: action === 'pause' ? 'Folder paused' : 'Folder resumed' })
    },
    onError: (error: Error) => {
      toast({ title: 'Error', description: error.message, variant: 'destructive' })
    },
  })

  // Sync all mutation
  const syncAllMutation = useMutation({
    mutationFn: () => vaultApi.syncAll(),
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: ['vault'] })
      const result = response.data.data
      toast({
        title: 'Sync complete',
        description: `Scanned ${result?.foldersScanned} folders, queued ${result?.filesQueued} files.`,
      })
    },
    onError: (error: Error) => {
      toast({ title: 'Sync failed', description: error.message, variant: 'destructive' })
    },
  })

  // Cleanup mutation
  const cleanupMutation = useMutation({
    mutationFn: () => vaultApi.cleanup(),
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: ['vault'] })
      const count = response.data.data
      toast({ title: 'Cleanup complete', description: `Cleaned up ${count} orphaned files.` })
    },
    onError: (error: Error) => {
      toast({ title: 'Cleanup failed', description: error.message, variant: 'destructive' })
    },
  })

  const toggleExpanded = (id: string) => {
    setExpandedFolders((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  const isLoading = statusLoading || foldersLoading

  // Calculate progress
  const totalFiles = status?.totalTrackedFiles || 0
  const memorizedFiles = status?.memorizedFiles || 0
  const progressPercent = totalFiles > 0 ? (memorizedFiles / totalFiles) * 100 : 0

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Vault</h1>
          <p className="text-muted-foreground">
            Monitor and synchronize files from your local file system
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            onClick={() => syncAllMutation.mutate()}
            disabled={syncAllMutation.isPending}
          >
            {syncAllMutation.isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-2 h-4 w-4" />
            )}
            Sync All
          </Button>
          <Button
            variant="outline"
            onClick={() => cleanupMutation.mutate()}
            disabled={cleanupMutation.isPending}
          >
            {cleanupMutation.isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Trash2 className="mr-2 h-4 w-4" />
            )}
            Cleanup
          </Button>
          <Dialog open={addDialogOpen} onOpenChange={setAddDialogOpen}>
            <DialogTrigger asChild>
              <Button>
                <Plus className="mr-2 h-4 w-4" />
                Add Folder
              </Button>
            </DialogTrigger>
            <DialogContent className="sm:max-w-[500px]">
              <DialogHeader>
                <DialogTitle>Add Watched Folder</DialogTitle>
                <DialogDescription>
                  Add a folder to monitor for file changes. Files will be automatically indexed.
                </DialogDescription>
              </DialogHeader>
              <div className="grid gap-4 py-4">
                <div className="grid gap-2">
                  <Label htmlFor="path">Folder Path</Label>
                  <Input
                    id="path"
                    placeholder="C:\Documents\MyFolder or /home/user/documents"
                    value={newFolder.path}
                    onChange={(e) => setNewFolder({ ...newFolder, path: e.target.value })}
                  />
                </div>
                <div className="grid gap-2">
                  <Label htmlFor="name">Display Name</Label>
                  <Input
                    id="name"
                    placeholder="My Documents"
                    value={newFolder.name}
                    onChange={(e) => setNewFolder({ ...newFolder, name: e.target.value })}
                  />
                </div>
                <div className="flex items-center justify-between">
                  <div className="space-y-0.5">
                    <Label>Include Subfolders</Label>
                    <p className="text-sm text-muted-foreground">Watch subdirectories recursively</p>
                  </div>
                  <Switch
                    checked={newFolder.isRecursive}
                    onCheckedChange={(checked: boolean) => setNewFolder({ ...newFolder, isRecursive: checked })}
                  />
                </div>
                <div className="flex items-center justify-between">
                  <div className="space-y-0.5">
                    <Label>Auto Memorize</Label>
                    <p className="text-sm text-muted-foreground">Automatically index new files</p>
                  </div>
                  <Switch
                    checked={newFolder.autoMemorize}
                    onCheckedChange={(checked: boolean) => setNewFolder({ ...newFolder, autoMemorize: checked })}
                  />
                </div>
              </div>
              <DialogFooter>
                <Button variant="outline" onClick={() => setAddDialogOpen(false)}>
                  Cancel
                </Button>
                <Button
                  onClick={() => addFolderMutation.mutate(newFolder)}
                  disabled={!newFolder.path || !newFolder.name || addFolderMutation.isPending}
                >
                  {addFolderMutation.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                  Add Folder
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>
      </div>

      {/* Status Cards */}
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Active Watchers</CardTitle>
            <Eye className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{status?.activeWatchers || 0}</div>
            <p className="text-xs text-muted-foreground">
              {folders?.length || 0} folders configured
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Tracked Files</CardTitle>
            <FileText className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{status?.totalTrackedFiles || 0}</div>
            <div className="mt-2">
              <Progress value={progressPercent} className="h-2" />
            </div>
            <p className="text-xs text-muted-foreground mt-1">
              {status?.memorizedFiles || 0} memorized ({progressPercent.toFixed(0)}%)
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Processing Queue</CardTitle>
            <Clock className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">
              {(status?.queuedFiles || 0) + (status?.processingFiles || 0)}
            </div>
            <p className="text-xs text-muted-foreground">
              {status?.queuedFiles || 0} queued, {status?.processingFiles || 0} processing
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Issues</CardTitle>
            <AlertCircle className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">
              {(status?.staleFiles || 0) + (status?.orphanedFiles || 0) + (status?.errorFiles || 0)}
            </div>
            <p className="text-xs text-muted-foreground">
              {status?.staleFiles || 0} stale, {status?.orphanedFiles || 0} orphaned, {status?.errorFiles || 0} errors
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Watched Folders */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <FolderSync className="h-5 w-5" />
            Watched Folders
          </CardTitle>
          <CardDescription>
            Folders being monitored for file changes
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
            </div>
          ) : folders && folders.length > 0 ? (
            <div className="space-y-3">
              {folders.map((folder) => (
                <FolderCard
                  key={folder.id}
                  folder={folder}
                  isExpanded={expandedFolders.has(folder.id)}
                  onToggleExpand={() => toggleExpanded(folder.id)}
                  onScan={() => scanFolderMutation.mutate(folder.id)}
                  onToggle={(action) => toggleFolderMutation.mutate({ id: folder.id, action })}
                  onRemove={() => removeFolderMutation.mutate(folder.id)}
                  isScanning={scanFolderMutation.isPending && scanFolderMutation.variables === folder.id}
                />
              ))}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <HardDrive className="h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-semibold">No watched folders</h3>
              <p className="text-muted-foreground mb-4">
                Add a folder to start monitoring files for automatic indexing
              </p>
              <Button onClick={() => setAddDialogOpen(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Add Folder
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

interface FolderCardProps {
  folder: WatchedFolder
  isExpanded: boolean
  onToggleExpand: () => void
  onScan: () => void
  onToggle: (action: 'pause' | 'resume') => void
  onRemove: () => void
  isScanning: boolean
}

function FolderCard({
  folder,
  isExpanded,
  onToggleExpand,
  onScan,
  onToggle,
  onRemove,
  isScanning,
}: FolderCardProps) {
  // StatusIcon unused for now but reserved for future visual enhancements
  void (statusIcons[folder.status] || AlertCircle)

  return (
    <Collapsible open={isExpanded} onOpenChange={onToggleExpand}>
      <div className="border rounded-lg">
        <div className="flex items-center justify-between p-4">
          <CollapsibleTrigger className="flex items-center gap-3 flex-1 text-left">
            {isExpanded ? (
              <ChevronDown className="h-4 w-4 text-muted-foreground" />
            ) : (
              <ChevronRight className="h-4 w-4 text-muted-foreground" />
            )}
            <FolderOpen className="h-5 w-5 text-primary" />
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2">
                <span className="font-medium truncate">{folder.name}</span>
                <Badge
                  variant="outline"
                  className={`${statusColors[folder.status]} text-white border-0 text-xs`}
                >
                  {folder.status}
                </Badge>
              </div>
              <p className="text-sm text-muted-foreground truncate">{folder.path}</p>
            </div>
          </CollapsibleTrigger>
          <div className="flex items-center gap-2 ml-4">
            <span className="text-sm text-muted-foreground">
              {folder.trackedFileCount} files
            </span>
            <Button
              variant="ghost"
              size="icon"
              onClick={(e) => {
                e.stopPropagation()
                onScan()
              }}
              disabled={isScanning}
            >
              {isScanning ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <RefreshCw className="h-4 w-4" />
              )}
            </Button>
            <Button
              variant="ghost"
              size="icon"
              onClick={(e) => {
                e.stopPropagation()
                onToggle(folder.status === 'Active' ? 'pause' : 'resume')
              }}
            >
              {folder.status === 'Active' ? (
                <Pause className="h-4 w-4" />
              ) : (
                <Play className="h-4 w-4" />
              )}
            </Button>
            <Button
              variant="ghost"
              size="icon"
              onClick={(e) => {
                e.stopPropagation()
                onRemove()
              }}
            >
              <Trash2 className="h-4 w-4 text-destructive" />
            </Button>
          </div>
        </div>

        <CollapsibleContent>
          <div className="border-t px-4 py-3 bg-muted/30">
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
              <div>
                <p className="text-muted-foreground">Recursive</p>
                <p className="font-medium">{folder.isRecursive ? 'Yes' : 'No'}</p>
              </div>
              <div>
                <p className="text-muted-foreground">Auto Memorize</p>
                <p className="font-medium">{folder.autoMemorize ? 'Yes' : 'No'}</p>
              </div>
              <div>
                <p className="text-muted-foreground">Created</p>
                <p className="font-medium">{formatDate(folder.createdAt)}</p>
              </div>
              <div>
                <p className="text-muted-foreground">Last Scanned</p>
                <p className="font-medium">
                  {folder.lastScannedAt ? formatDate(folder.lastScannedAt) : 'Never'}
                </p>
              </div>
            </div>
            {folder.includePatterns && folder.includePatterns.length > 0 && (
              <div className="mt-3">
                <p className="text-sm text-muted-foreground mb-1">Include Patterns</p>
                <div className="flex flex-wrap gap-1">
                  {folder.includePatterns.map((pattern, i) => (
                    <Badge key={i} variant="secondary" className="text-xs">
                      {pattern}
                    </Badge>
                  ))}
                </div>
              </div>
            )}
            {folder.excludePatterns && folder.excludePatterns.length > 0 && (
              <div className="mt-3">
                <p className="text-sm text-muted-foreground mb-1">Exclude Patterns</p>
                <div className="flex flex-wrap gap-1">
                  {folder.excludePatterns.map((pattern, i) => (
                    <Badge key={i} variant="outline" className="text-xs">
                      {pattern}
                    </Badge>
                  ))}
                </div>
              </div>
            )}
            {folder.errorMessage && (
              <div className="mt-3 p-2 bg-destructive/10 rounded-md">
                <p className="text-sm text-destructive">{folder.errorMessage}</p>
              </div>
            )}
          </div>
        </CollapsibleContent>
      </div>
    </Collapsible>
  )
}
