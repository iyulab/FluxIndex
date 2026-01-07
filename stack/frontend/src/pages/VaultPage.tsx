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
import { vaultApi, type WatchedFolder, type VaultStatus, type TrackedFile } from '@/lib/api'
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
  FolderX,
  MapPin,
} from 'lucide-react'
import { useToast } from '@/hooks/use-toast'

const statusColors: Record<string, string> = {
  Active: 'bg-green-500',
  Paused: 'bg-yellow-500',
  Error: 'bg-red-500',
  Invalid: 'bg-red-600',
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
  Invalid: FolderX,
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
  const [updatePathDialogOpen, setUpdatePathDialogOpen] = useState(false)
  const [folderToUpdate, setFolderToUpdate] = useState<WatchedFolder | null>(null)
  const [newPath, setNewPath] = useState('')

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

  // Update folder path mutation
  const updatePathMutation = useMutation({
    mutationFn: ({ id, newPath }: { id: string; newPath: string }) =>
      vaultApi.updateFolderPath(id, newPath),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['vault'] })
      setUpdatePathDialogOpen(false)
      setFolderToUpdate(null)
      setNewPath('')
      toast({ title: 'Path updated', description: 'Folder path has been updated successfully.' })
    },
    onError: (error: Error) => {
      toast({ title: 'Failed to update path', description: error.message, variant: 'destructive' })
    },
  })

  const openUpdatePathDialog = (folder: WatchedFolder) => {
    setFolderToUpdate(folder)
    setNewPath(folder.path)
    setUpdatePathDialogOpen(true)
  }

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

          {/* Update Path Dialog */}
          <Dialog open={updatePathDialogOpen} onOpenChange={setUpdatePathDialogOpen}>
            <DialogContent className="sm:max-w-[500px]">
              <DialogHeader>
                <DialogTitle className="flex items-center gap-2">
                  <MapPin className="h-5 w-5 text-muted-foreground" />
                  Update Folder Path
                </DialogTitle>
                <DialogDescription>
                  The folder "{folderToUpdate?.name}" was moved or no longer exists at its original location.
                  Enter the new path to continue watching.
                </DialogDescription>
              </DialogHeader>
              <div className="grid gap-4 py-4">
                <div className="grid gap-2">
                  <Label>Original Path</Label>
                  <p className="text-sm text-muted-foreground bg-muted p-2 rounded-md font-mono break-all">
                    {folderToUpdate?.path}
                  </p>
                </div>
                <div className="grid gap-2">
                  <Label htmlFor="newPath">New Path</Label>
                  <Input
                    id="newPath"
                    placeholder="Enter the new folder path"
                    value={newPath}
                    onChange={(e) => setNewPath(e.target.value)}
                  />
                </div>
              </div>
              <DialogFooter>
                <Button variant="outline" onClick={() => setUpdatePathDialogOpen(false)}>
                  Cancel
                </Button>
                <Button
                  onClick={() => folderToUpdate && updatePathMutation.mutate({ id: folderToUpdate.id, newPath })}
                  disabled={!newPath || newPath === folderToUpdate?.path || updatePathMutation.isPending}
                >
                  {updatePathMutation.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                  Update Path
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
                  onUpdatePath={() => openUpdatePathDialog(folder)}
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
  onUpdatePath: () => void
  isScanning: boolean
}

function FolderCard({
  folder,
  isExpanded,
  onToggleExpand,
  onScan,
  onToggle,
  onRemove,
  onUpdatePath,
  isScanning,
}: FolderCardProps) {
  // StatusIcon unused for now but reserved for future visual enhancements
  void (statusIcons[folder.status] || AlertCircle)

  const pathMissing = !folder.pathExists

  // Fetch files when folder is expanded
  const { data: files, isLoading: filesLoading } = useQuery({
    queryKey: ['vault', 'folders', folder.id, 'files'],
    queryFn: async () => {
      const response = await vaultApi.getFilesByFolder(folder.id)
      return response.data.data || []
    },
    enabled: isExpanded,
    staleTime: 10000,
  })

  return (
    <Collapsible open={isExpanded} onOpenChange={onToggleExpand}>
      <div className={`border rounded-lg ${pathMissing ? 'border-destructive/50 bg-destructive/5' : ''}`}>
        <div className="flex items-center justify-between p-4">
          <CollapsibleTrigger className="flex items-center gap-3 flex-1 text-left">
            {isExpanded ? (
              <ChevronDown className="h-4 w-4 text-muted-foreground" />
            ) : (
              <ChevronRight className="h-4 w-4 text-muted-foreground" />
            )}
            {pathMissing ? (
              <FolderX className="h-5 w-5 text-destructive" />
            ) : (
              <FolderOpen className="h-5 w-5 text-primary" />
            )}
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2">
                <span className="font-medium truncate">{folder.name}</span>
                <Badge
                  variant="outline"
                  className={`${statusColors[folder.status]} text-white border-0 text-xs`}
                >
                  {folder.status}
                </Badge>
                {pathMissing && (
                  <Badge variant="destructive" className="text-xs">
                    Path Missing
                  </Badge>
                )}
              </div>
              <p className={`text-sm truncate ${pathMissing ? 'text-destructive line-through' : 'text-muted-foreground'}`}>
                {folder.path}
              </p>
            </div>
          </CollapsibleTrigger>
          <div className="flex items-center gap-2 ml-4">
            <span className="text-sm text-muted-foreground">
              {folder.trackedFileCount} files
            </span>
            {pathMissing ? (
              <Button
                variant="outline"
                size="sm"
                onClick={(e) => {
                  e.stopPropagation()
                  onUpdatePath()
                }}
                className="text-xs"
              >
                <MapPin className="h-3 w-3 mr-1" />
                Update Path
              </Button>
            ) : (
              <>
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={(e) => {
                    e.stopPropagation()
                    onScan()
                  }}
                  disabled={isScanning}
                  title="Scan folder"
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
                  title={folder.status === 'Active' ? 'Pause watching' : 'Resume watching'}
                >
                  {folder.status === 'Active' ? (
                    <Pause className="h-4 w-4" />
                  ) : (
                    <Play className="h-4 w-4" />
                  )}
                </Button>
              </>
            )}
            <Button
              variant="ghost"
              size="icon"
              onClick={(e) => {
                e.stopPropagation()
                onRemove()
              }}
              title="Remove folder"
            >
              <Trash2 className="h-4 w-4 text-destructive" />
            </Button>
          </div>
        </div>

        <CollapsibleContent>
          <div className="border-t px-4 py-3 bg-muted/30">
            {pathMissing && (
              <div className="mb-4 p-3 bg-destructive/10 border border-destructive/30 rounded-md">
                <div className="flex items-start gap-2">
                  <AlertCircle className="h-4 w-4 text-destructive mt-0.5 shrink-0" />
                  <div className="flex-1">
                    <p className="text-sm font-medium text-destructive">Folder path not found</p>
                    <p className="text-sm text-muted-foreground mt-1">
                      The folder at this path no longer exists or has been moved.
                      Update the path to continue watching, or remove this folder.
                    </p>
                    <div className="mt-2 flex gap-2">
                      <Button variant="outline" size="sm" onClick={onUpdatePath}>
                        <MapPin className="h-3 w-3 mr-1" />
                        Update Path
                      </Button>
                      <Button variant="ghost" size="sm" onClick={onRemove} className="text-destructive">
                        <Trash2 className="h-3 w-3 mr-1" />
                        Remove
                      </Button>
                    </div>
                  </div>
                </div>
              </div>
            )}
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

            {/* Tracked Files List */}
            <div className="mt-4 border-t pt-4">
              <div className="flex items-center justify-between mb-3">
                <h4 className="text-sm font-medium flex items-center gap-2">
                  <FileText className="h-4 w-4" />
                  Tracked Files
                </h4>
                <span className="text-xs text-muted-foreground">
                  {files?.length || 0} files
                </span>
              </div>
              {filesLoading ? (
                <div className="flex items-center justify-center py-4">
                  <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
                </div>
              ) : files && files.length > 0 ? (
                <div className="space-y-1 max-h-64 overflow-y-auto">
                  {files.map((file: TrackedFile) => (
                    <FileRow key={file.id} file={file} />
                  ))}
                </div>
              ) : (
                <p className="text-sm text-muted-foreground text-center py-4">
                  No tracked files in this folder
                </p>
              )}
            </div>
          </div>
        </CollapsibleContent>
      </div>
    </Collapsible>
  )
}

interface FileRowProps {
  file: TrackedFile
}

function FileRow({ file }: FileRowProps) {
  // Use effectiveStatus which combines TrackedFile + Document status
  const displayStatus = file.effectiveStatus || file.status
  const StatusIcon = statusIcons[displayStatus] || AlertCircle

  const formatFileSize = (bytes?: number) => {
    if (!bytes) return '-'
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  return (
    <div className="flex items-center gap-2 px-2 py-1.5 rounded-md hover:bg-muted/50 text-sm group">
      <StatusIcon className={`h-3.5 w-3.5 shrink-0 ${
        displayStatus === 'Indexed' ? 'text-green-500' :
        displayStatus === 'Pending' ? 'text-yellow-500' :
        displayStatus === 'Indexing' ? 'text-blue-600' :
        displayStatus === 'Queued' ? 'text-blue-500' :
        displayStatus === 'Processing' ? 'text-blue-600' :
        displayStatus === 'Stale' ? 'text-orange-500' :
        displayStatus === 'Error' ? 'text-red-500' :
        displayStatus === 'Orphaned' ? 'text-gray-500' :
        'text-muted-foreground'
      }`} />
      <span className="flex-1 truncate font-mono text-xs" title={file.sourcePath}>
        {file.fileName}
      </span>
      <Badge
        variant="outline"
        className={`text-[10px] px-1.5 py-0 h-5 ${statusColors[displayStatus] || 'bg-gray-500'} text-white border-0`}
      >
        {displayStatus}
      </Badge>
      <span className="text-xs text-muted-foreground w-16 text-right">
        {formatFileSize(file.fileSize)}
      </span>
    </div>
  )
}
