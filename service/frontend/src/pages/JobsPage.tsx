import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Progress } from '@/components/ui/progress'
import { jobsApi, type IndexingJob, type PagedResult } from '@/lib/api'
import { formatDate } from '@/lib/utils'
import {
  Loader2, CheckCircle, Clock, XCircle, AlertCircle, RefreshCw,
  StopCircle, FileText, Timer, Zap
} from 'lucide-react'
import { useToast } from '@/hooks/use-toast'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'

const statusIcons: Record<string, typeof CheckCircle> = {
  Queued: Clock,
  Processing: RefreshCw,
  Completed: CheckCircle,
  Failed: XCircle,
  Cancelled: StopCircle,
}

const statusColors: Record<string, string> = {
  Queued: 'text-yellow-500',
  Processing: 'text-blue-500',
  Completed: 'text-green-500',
  Failed: 'text-red-500',
  Cancelled: 'text-gray-500',
}

const statusBgColors: Record<string, string> = {
  Queued: 'bg-yellow-50 border-yellow-200 dark:bg-yellow-950/20 dark:border-yellow-900',
  Processing: 'bg-blue-50 border-blue-200 dark:bg-blue-950/20 dark:border-blue-900',
  Completed: 'bg-green-50 border-green-200 dark:bg-green-950/20 dark:border-green-900',
  Failed: 'bg-red-50 border-red-200 dark:bg-red-950/20 dark:border-red-900',
  Cancelled: 'bg-gray-50 border-gray-200 dark:bg-gray-950/20 dark:border-gray-900',
}

function formatDuration(ms: number | undefined): string {
  if (!ms) return '-'
  if (ms < 1000) return `${ms}ms`
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`
  return `${(ms / 60000).toFixed(1)}m`
}

export default function JobsPage() {
  const [page] = useState(1)
  const [statusFilter, setStatusFilter] = useState<string | undefined>()
  const { toast } = useToast()
  const queryClient = useQueryClient()

  // Fetch job summary
  const { data: summary } = useQuery({
    queryKey: ['jobs', 'summary'],
    queryFn: async () => {
      const response = await jobsApi.getSummary()
      return response.data.data
    },
    refetchInterval: 3000,
  })

  // Fetch jobs with auto-refresh when there are active jobs
  const { data: jobsResult, isLoading } = useQuery({
    queryKey: ['jobs', page, statusFilter],
    queryFn: async () => {
      const response = await jobsApi.getAll({
        page,
        pageSize: 50,
        status: statusFilter,
      })
      return response.data.data
    },
    refetchInterval: (query) => {
      const result = query.state.data as PagedResult<IndexingJob> | undefined
      const hasActiveJobs = result?.items?.some(j => j.status === 'Queued' || j.status === 'Processing')
      return hasActiveJobs ? 2000 : 10000
    },
  })

  const jobs = jobsResult?.items || []

  const cancelMutation = useMutation({
    mutationFn: (id: string) => jobsApi.cancel(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      toast({ title: 'Job cancelled successfully' })
    },
    onError: () => {
      toast({ title: 'Failed to cancel job', variant: 'destructive' })
    },
  })

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
          <h2 className="text-3xl font-bold tracking-tight">Indexing Jobs</h2>
          <p className="text-muted-foreground">
            Monitor and manage document indexing processes
          </p>
        </div>
      </div>

      {/* Summary Cards */}
      <div className="grid gap-4 md:grid-cols-5">
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Queued</p>
                <p className="text-2xl font-bold text-yellow-600">{summary?.queuedCount ?? 0}</p>
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
                <p className="text-2xl font-bold text-blue-600">{summary?.processingCount ?? 0}</p>
              </div>
              <Loader2 className="h-8 w-8 text-blue-500/30 animate-spin" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Completed</p>
                <p className="text-2xl font-bold text-green-600">{summary?.completedCount ?? 0}</p>
              </div>
              <CheckCircle className="h-8 w-8 text-green-500/30" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Failed</p>
                <p className="text-2xl font-bold text-red-600">{summary?.failedCount ?? 0}</p>
              </div>
              <XCircle className="h-8 w-8 text-red-500/30" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-muted-foreground">Avg Time</p>
                <p className="text-2xl font-bold">{formatDuration(summary?.averageProcessingTimeMs)}</p>
              </div>
              <Timer className="h-8 w-8 text-muted-foreground/30" />
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Status Filter */}
      <div className="flex gap-2">
        <Button
          variant={!statusFilter ? 'default' : 'outline'}
          size="sm"
          onClick={() => setStatusFilter(undefined)}
        >
          All
        </Button>
        {['Queued', 'Processing', 'Completed', 'Failed'].map((status) => (
          <Button
            key={status}
            variant={statusFilter === status ? 'default' : 'outline'}
            size="sm"
            onClick={() => setStatusFilter(status)}
          >
            {status}
          </Button>
        ))}
      </div>

      {/* Jobs List */}
      <Card>
        <CardHeader>
          <CardTitle>Recent Jobs</CardTitle>
          <CardDescription>
            {jobsResult?.totalCount ?? 0} jobs {statusFilter ? `with status "${statusFilter}"` : 'total'}
            {jobsResult && jobsResult.totalPages > 1 && ` (Page ${jobsResult.page} of ${jobsResult.totalPages})`}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <TooltipProvider>
            {jobs && jobs.length > 0 ? (
              <div className="space-y-3">
                {jobs.map((job) => {
                  const StatusIcon = statusIcons[job.status] || AlertCircle
                  const statusColor = statusColors[job.status] || 'text-gray-500'
                  const bgColor = statusBgColors[job.status] || ''
                  const isActive = job.status === 'Processing'
                  const canCancel = job.status === 'Queued' || job.status === 'Processing'

                  return (
                    <div
                      key={job.id}
                      className={`flex items-center justify-between p-4 border rounded-lg transition-colors ${bgColor}`}
                    >
                      <div className="flex items-center space-x-4 flex-1 min-w-0">
                        <FileText className="h-8 w-8 flex-shrink-0 text-muted-foreground" />
                        <div className="min-w-0 flex-1">
                          <h4 className="font-medium truncate">{job.documentTitle || 'Unknown Document'}</h4>
                          <div className="flex items-center flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
                            <span className="font-mono text-xs">{job.id.substring(0, 8)}...</span>
                            <span>{formatDate(job.createdAt)}</span>
                            {job.startedAt && (
                              <span>Started: {formatDate(job.startedAt)}</span>
                            )}
                            {job.durationMs && (
                              <span className="flex items-center gap-1">
                                <Zap className="h-3 w-3" />
                                {formatDuration(job.durationMs)}
                              </span>
                            )}
                          </div>
                          {/* Error message display */}
                          {job.status === 'Failed' && job.errorMessage && (
                            <p className="text-xs text-red-600 mt-1 truncate">
                              Error: {job.errorMessage}
                            </p>
                          )}
                        </div>
                      </div>

                      <div className="flex items-center space-x-3 flex-shrink-0">
                        {/* Progress bar for processing jobs */}
                        {isActive && (
                          <div className="w-32">
                            <div className="flex items-center justify-between text-xs text-muted-foreground mb-1">
                              <span>{job.processedChunks}/{job.totalChunks} chunks</span>
                              <span>{job.progressPercentage}%</span>
                            </div>
                            <Progress value={job.progressPercentage} className="h-2" />
                          </div>
                        )}

                        {/* Completed chunk count */}
                        {job.status === 'Completed' && job.totalChunks > 0 && (
                          <span className="text-sm text-muted-foreground">
                            {job.totalChunks} chunks
                          </span>
                        )}

                        {/* Status indicator */}
                        <div className={`flex items-center space-x-1 ${statusColor}`}>
                          <StatusIcon className={`h-4 w-4 ${isActive ? 'animate-spin' : ''}`} />
                          <span className="text-sm">{job.status}</span>
                        </div>

                        {/* Cancel button */}
                        {canCancel && (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <Button
                                variant="ghost"
                                size="icon"
                                onClick={() => cancelMutation.mutate(job.id)}
                                disabled={cancelMutation.isPending}
                              >
                                <StopCircle className="h-4 w-4" />
                              </Button>
                            </TooltipTrigger>
                            <TooltipContent>
                              <p>Cancel job</p>
                            </TooltipContent>
                          </Tooltip>
                        )}
                      </div>
                    </div>
                  )
                })}
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center py-12">
                <Loader2 className="h-12 w-12 text-muted-foreground mb-4" />
                <h3 className="text-lg font-medium">No indexing jobs</h3>
                <p className="text-sm text-muted-foreground">
                  Jobs will appear here when documents are uploaded
                </p>
              </div>
            )}
          </TooltipProvider>
        </CardContent>
      </Card>
    </div>
  )
}
