import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  evaluationApi,
  type EvaluationResult,
  type RunEvaluationRequest
} from '@/lib/api'
import { useToast } from '@/hooks/use-toast'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
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
  Play,
  RefreshCw,
  Plus,
  Trash2,
  CheckCircle2,
  XCircle,
  Clock,
  AlertCircle,
  Target,
  Activity,
  BarChart3,
} from 'lucide-react'
import { cn } from '@/lib/utils'

interface QueryInput {
  id: string
  query: string
  expectedAnswer: string
}

export default function EvaluationPage() {
  const { toast } = useToast()
  const queryClient = useQueryClient()
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null)
  const [isNewJobDialogOpen, setIsNewJobDialogOpen] = useState(false)
  const [jobName, setJobName] = useState('')
  const [topK, setTopK] = useState(5)
  const [generateAnswers, setGenerateAnswers] = useState(true)
  const [queries, setQueries] = useState<QueryInput[]>([
    { id: '1', query: '', expectedAnswer: '' }
  ])

  // Fetch evaluation jobs
  const { data: jobsData, isLoading: jobsLoading, refetch: refetchJobs } = useQuery({
    queryKey: ['evaluation-jobs'],
    queryFn: async () => {
      const response = await evaluationApi.listJobs({ pageSize: 50 })
      return response.data.data
    },
    refetchInterval: 5000, // Poll every 5 seconds for job updates
  })

  // Fetch selected job results
  const { data: resultData, isLoading: resultLoading } = useQuery({
    queryKey: ['evaluation-result', selectedJobId],
    queryFn: async () => {
      if (!selectedJobId) return null
      const response = await evaluationApi.getResults(selectedJobId, true)
      return response.data.data
    },
    enabled: !!selectedJobId,
    refetchInterval: (query) => {
      const data = query.state.data as EvaluationResult | null | undefined
      // Poll if job is still running
      return data?.status === 'Running' || data?.status === 'Queued' ? 3000 : false
    },
  })

  // Run evaluation mutation
  const runEvaluationMutation = useMutation({
    mutationFn: (request: RunEvaluationRequest) => evaluationApi.runEvaluation(request),
    onSuccess: (response) => {
      toast({
        title: 'Evaluation Started',
        description: `Job "${response.data.data?.jobName}" has been queued.`,
      })
      setIsNewJobDialogOpen(false)
      resetForm()
      queryClient.invalidateQueries({ queryKey: ['evaluation-jobs'] })
      if (response.data.data?.jobId) {
        setSelectedJobId(response.data.data.jobId)
      }
    },
    onError: (error: Error) => {
      toast({
        title: 'Failed to Start Evaluation',
        description: error.message,
        variant: 'destructive',
      })
    },
  })

  // Cancel job mutation
  const cancelJobMutation = useMutation({
    mutationFn: (jobId: string) => evaluationApi.cancelJob(jobId),
    onSuccess: () => {
      toast({ title: 'Job Cancelled' })
      queryClient.invalidateQueries({ queryKey: ['evaluation-jobs'] })
    },
  })

  const resetForm = () => {
    setJobName('')
    setTopK(5)
    setGenerateAnswers(true)
    setQueries([{ id: '1', query: '', expectedAnswer: '' }])
  }

  const addQuery = () => {
    setQueries([...queries, { id: Date.now().toString(), query: '', expectedAnswer: '' }])
  }

  const removeQuery = (id: string) => {
    if (queries.length > 1) {
      setQueries(queries.filter(q => q.id !== id))
    }
  }

  const updateQuery = (id: string, field: 'query' | 'expectedAnswer', value: string) => {
    setQueries(queries.map(q => q.id === id ? { ...q, [field]: value } : q))
  }

  const handleRunEvaluation = () => {
    const validQueries = queries.filter(q => q.query.trim() && q.expectedAnswer.trim())
    if (validQueries.length === 0) {
      toast({
        title: 'No Valid Queries',
        description: 'Please add at least one query with expected answer.',
        variant: 'destructive',
      })
      return
    }

    const request: RunEvaluationRequest = {
      jobName: jobName || `Evaluation ${new Date().toLocaleString()}`,
      queries: validQueries.map(q => ({
        query: q.query,
        expectedAnswer: q.expectedAnswer,
      })),
      topK,
      generateAnswers,
    }

    runEvaluationMutation.mutate(request)
  }

  const getStatusBadge = (status: string) => {
    const statusConfig: Record<string, { color: string; icon: React.ReactNode }> = {
      Queued: { color: 'bg-yellow-500/10 text-yellow-500', icon: <Clock className="h-3 w-3" /> },
      Running: { color: 'bg-blue-500/10 text-blue-500', icon: <RefreshCw className="h-3 w-3 animate-spin" /> },
      Completed: { color: 'bg-green-500/10 text-green-500', icon: <CheckCircle2 className="h-3 w-3" /> },
      Failed: { color: 'bg-red-500/10 text-red-500', icon: <XCircle className="h-3 w-3" /> },
      Cancelled: { color: 'bg-gray-500/10 text-gray-500', icon: <AlertCircle className="h-3 w-3" /> },
    }
    const config = statusConfig[status] || statusConfig.Queued
    return (
      <Badge variant="secondary" className={cn('gap-1', config.color)}>
        {config.icon}
        {status}
      </Badge>
    )
  }

  const getQualityTierColor = (tier: string) => {
    switch (tier) {
      case 'Excellent': return 'text-green-500'
      case 'High': return 'text-blue-500'
      case 'Medium': return 'text-yellow-500'
      case 'Low': return 'text-red-500'
      default: return 'text-gray-500'
    }
  }

  const formatScore = (score: number | undefined) => {
    if (score === undefined) return '-'
    return (score * 100).toFixed(1) + '%'
  }

  const jobs = jobsData?.items || []

  return (
    <div className="container mx-auto p-6 space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold">RAG Evaluation</h1>
          <p className="text-muted-foreground">
            Evaluate retrieval quality with golden datasets
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => refetchJobs()}>
            <RefreshCw className="h-4 w-4 mr-2" />
            Refresh
          </Button>
          <Dialog open={isNewJobDialogOpen} onOpenChange={setIsNewJobDialogOpen}>
            <DialogTrigger asChild>
              <Button>
                <Plus className="h-4 w-4 mr-2" />
                New Evaluation
              </Button>
            </DialogTrigger>
            <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
              <DialogHeader>
                <DialogTitle>Create Evaluation Job</DialogTitle>
                <DialogDescription>
                  Add queries with expected answers to evaluate RAG quality
                </DialogDescription>
              </DialogHeader>

              <div className="space-y-4 py-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">Job Name</label>
                  <Input
                    placeholder="My Evaluation"
                    value={jobName}
                    onChange={(e) => setJobName(e.target.value)}
                  />
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <label className="text-sm font-medium">Top K Results</label>
                    <Input
                      type="number"
                      min={1}
                      max={20}
                      value={topK}
                      onChange={(e) => setTopK(parseInt(e.target.value) || 5)}
                    />
                  </div>
                  <div className="space-y-2">
                    <label className="text-sm font-medium">Generate Answers</label>
                    <div className="flex items-center gap-2 h-10">
                      <input
                        type="checkbox"
                        id="generateAnswers"
                        checked={generateAnswers}
                        onChange={(e) => setGenerateAnswers(e.target.checked)}
                        className="h-4 w-4"
                      />
                      <label htmlFor="generateAnswers" className="text-sm text-muted-foreground">
                        Use LLM to generate answers
                      </label>
                    </div>
                  </div>
                </div>

                <div className="space-y-2">
                  <div className="flex items-center justify-between">
                    <label className="text-sm font-medium">Test Queries</label>
                    <Button variant="outline" size="sm" onClick={addQuery}>
                      <Plus className="h-3 w-3 mr-1" />
                      Add Query
                    </Button>
                  </div>

                  <div className="space-y-4 max-h-[300px] overflow-y-auto pr-2">
                    {queries.map((q, index) => (
                      <Card key={q.id} className="p-3">
                        <div className="flex items-start gap-2">
                          <span className="text-sm font-medium text-muted-foreground w-6">
                            {index + 1}.
                          </span>
                          <div className="flex-1 space-y-2">
                            <Input
                              placeholder="Enter test query..."
                              value={q.query}
                              onChange={(e) => updateQuery(q.id, 'query', e.target.value)}
                            />
                            <Textarea
                              placeholder="Expected answer (ground truth)..."
                              value={q.expectedAnswer}
                              onChange={(e) => updateQuery(q.id, 'expectedAnswer', e.target.value)}
                              rows={2}
                            />
                          </div>
                          {queries.length > 1 && (
                            <Button
                              variant="ghost"
                              size="icon"
                              onClick={() => removeQuery(q.id)}
                            >
                              <Trash2 className="h-4 w-4 text-destructive" />
                            </Button>
                          )}
                        </div>
                      </Card>
                    ))}
                  </div>
                </div>
              </div>

              <DialogFooter>
                <Button variant="outline" onClick={() => setIsNewJobDialogOpen(false)}>
                  Cancel
                </Button>
                <Button
                  onClick={handleRunEvaluation}
                  disabled={runEvaluationMutation.isPending}
                >
                  {runEvaluationMutation.isPending ? (
                    <>
                      <RefreshCw className="h-4 w-4 mr-2 animate-spin" />
                      Starting...
                    </>
                  ) : (
                    <>
                      <Play className="h-4 w-4 mr-2" />
                      Run Evaluation
                    </>
                  )}
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Jobs List */}
        <div className="lg:col-span-1">
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Evaluation Jobs</CardTitle>
              <CardDescription>
                {jobs.length} evaluation{jobs.length !== 1 ? 's' : ''} total
              </CardDescription>
            </CardHeader>
            <CardContent className="p-0">
              {jobsLoading ? (
                <div className="p-4 text-center text-muted-foreground">
                  Loading jobs...
                </div>
              ) : jobs.length === 0 ? (
                <div className="p-8 text-center text-muted-foreground">
                  <Target className="h-12 w-12 mx-auto mb-2 opacity-50" />
                  <p>No evaluations yet</p>
                  <p className="text-sm">Create your first evaluation job</p>
                </div>
              ) : (
                <div className="divide-y max-h-[600px] overflow-y-auto">
                  {jobs.map((job) => (
                    <button
                      key={job.jobId}
                      onClick={() => setSelectedJobId(job.jobId)}
                      className={cn(
                        'w-full text-left p-4 hover:bg-accent transition-colors',
                        selectedJobId === job.jobId && 'bg-accent'
                      )}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex-1 min-w-0">
                          <p className="font-medium truncate">{job.jobName}</p>
                          <p className="text-sm text-muted-foreground">
                            {job.totalQueries} queries
                          </p>
                          <p className="text-xs text-muted-foreground">
                            {new Date(job.createdAt).toLocaleString()}
                          </p>
                        </div>
                        <div className="flex flex-col items-end gap-1">
                          {getStatusBadge(job.status)}
                          {(job.status === 'Running' || job.status === 'Queued') && (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="h-6 px-2 text-xs"
                              onClick={(e) => {
                                e.stopPropagation()
                                cancelJobMutation.mutate(job.jobId)
                              }}
                            >
                              Cancel
                            </Button>
                          )}
                        </div>
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </div>

        {/* Results Panel */}
        <div className="lg:col-span-2">
          {!selectedJobId ? (
            <Card className="h-full flex items-center justify-center min-h-[400px]">
              <div className="text-center text-muted-foreground">
                <BarChart3 className="h-16 w-16 mx-auto mb-4 opacity-50" />
                <p>Select an evaluation job to view results</p>
              </div>
            </Card>
          ) : resultLoading ? (
            <Card className="h-full flex items-center justify-center min-h-[400px]">
              <div className="text-center">
                <RefreshCw className="h-8 w-8 mx-auto mb-2 animate-spin text-primary" />
                <p className="text-muted-foreground">Loading results...</p>
              </div>
            </Card>
          ) : resultData ? (
            <div className="space-y-4">
              {/* Summary Card */}
              <Card>
                <CardHeader className="pb-2">
                  <div className="flex items-center justify-between">
                    <div>
                      <CardTitle>{resultData.jobName}</CardTitle>
                      <CardDescription>
                        {resultData.status === 'Running' ? 'Evaluation in progress...' :
                         resultData.status === 'Completed' ? `Completed in ${resultData.durationMs}ms` :
                         resultData.status}
                      </CardDescription>
                    </div>
                    {getStatusBadge(resultData.status)}
                  </div>
                </CardHeader>
                <CardContent>
                  {resultData.status === 'Running' && (
                    <div className="mb-4">
                      <Progress
                        value={(resultData.successfulQueries / resultData.totalQueries) * 100}
                        className="h-2"
                      />
                      <p className="text-sm text-muted-foreground mt-1">
                        {resultData.successfulQueries} / {resultData.totalQueries} queries processed
                      </p>
                    </div>
                  )}

                  {resultData.metrics && (
                    <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                      <div className="text-center p-3 bg-muted/50 rounded-lg">
                        <p className="text-2xl font-bold">{formatScore(resultData.metrics.mrr)}</p>
                        <p className="text-xs text-muted-foreground">MRR</p>
                      </div>
                      <div className="text-center p-3 bg-muted/50 rounded-lg">
                        <p className="text-2xl font-bold">{formatScore(resultData.metrics.precisionAtK)}</p>
                        <p className="text-xs text-muted-foreground">Precision@K</p>
                      </div>
                      <div className="text-center p-3 bg-muted/50 rounded-lg">
                        <p className="text-2xl font-bold">{formatScore(resultData.metrics.recallAtK)}</p>
                        <p className="text-xs text-muted-foreground">Recall@K</p>
                      </div>
                      <div className="text-center p-3 bg-muted/50 rounded-lg">
                        <p className="text-2xl font-bold">{formatScore(resultData.metrics.ndcg)}</p>
                        <p className="text-xs text-muted-foreground">NDCG</p>
                      </div>
                    </div>
                  )}

                  {resultData.metrics && (
                    <div className="mt-4 flex items-center justify-between p-4 bg-muted/30 rounded-lg">
                      <div className="flex items-center gap-2">
                        <Activity className="h-5 w-5" />
                        <span className="font-medium">Overall Score</span>
                      </div>
                      <div className="flex items-center gap-3">
                        <span className="text-2xl font-bold">
                          {formatScore(resultData.metrics.overallScore)}
                        </span>
                        <Badge className={getQualityTierColor(resultData.metrics.qualityTier)}>
                          {resultData.metrics.qualityTier}
                        </Badge>
                      </div>
                    </div>
                  )}
                </CardContent>
              </Card>

              {/* Query Results */}
              {resultData.queryResults && resultData.queryResults.length > 0 && (
                <Card>
                  <CardHeader>
                    <CardTitle className="text-lg">Query Results</CardTitle>
                    <CardDescription>
                      Individual query evaluation details
                    </CardDescription>
                  </CardHeader>
                  <CardContent className="p-0">
                    <div className="divide-y">
                      {resultData.queryResults.map((qr, index) => (
                        <div key={index} className="p-4">
                          <div className="flex items-start gap-3">
                            <div className={cn(
                              'w-6 h-6 rounded-full flex items-center justify-center text-xs font-medium',
                              qr.success ? 'bg-green-500/10 text-green-500' : 'bg-red-500/10 text-red-500'
                            )}>
                              {qr.success ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                            </div>
                            <div className="flex-1 min-w-0">
                              <p className="font-medium">{qr.query}</p>
                              <p className="text-sm text-muted-foreground mt-1">
                                <span className="font-medium">Expected:</span> {qr.expectedAnswer}
                              </p>
                              {qr.generatedAnswer && (
                                <p className="text-sm text-muted-foreground mt-1">
                                  <span className="font-medium">Generated:</span> {qr.generatedAnswer}
                                </p>
                              )}
                              <div className="flex items-center gap-4 mt-2 text-xs text-muted-foreground">
                                <span>{qr.retrievedChunks} chunks retrieved</span>
                                <span>{qr.retrievalLatencyMs.toFixed(0)}ms</span>
                                {qr.metrics && (
                                  <>
                                    <span>P: {(qr.metrics.precision * 100).toFixed(0)}%</span>
                                    <span>R: {(qr.metrics.recall * 100).toFixed(0)}%</span>
                                  </>
                                )}
                              </div>
                              {qr.errorMessage && (
                                <p className="text-sm text-destructive mt-1">{qr.errorMessage}</p>
                              )}
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              )}

              {resultData.errorMessage && (
                <Card className="border-destructive">
                  <CardContent className="pt-6">
                    <div className="flex items-start gap-2 text-destructive">
                      <AlertCircle className="h-5 w-5 mt-0.5" />
                      <div>
                        <p className="font-medium">Evaluation Failed</p>
                        <p className="text-sm">{resultData.errorMessage}</p>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              )}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  )
}
