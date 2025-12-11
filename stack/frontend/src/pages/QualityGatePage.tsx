import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  qualityGateApi,
  evaluationApi,
  type QualityGateRequest,
  type QualityGateResult,
  type QualityThresholds,
} from '@/lib/api'
import { useToast } from '@/hooks/use-toast'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Progress } from '@/components/ui/progress'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Play,
  RefreshCw,
  CheckCircle2,
  XCircle,
  Shield,
  AlertTriangle,
  Clock,
  Zap,
  Terminal,
} from 'lucide-react'
import { cn } from '@/lib/utils'

const DEFAULT_THRESHOLDS: QualityThresholds = {
  minPrecision: 0.7,
  minRecall: 0.7,
  minF1Score: 0.7,
  minMRR: 0.7,
  minNDCG: 0.7,
}

export default function QualityGatePage() {
  const { toast } = useToast()
  const [activeTab, setActiveTab] = useState('execute')

  // Execute form state
  const [systemVersion, setSystemVersion] = useState('')
  const [selectedDatasetId, setSelectedDatasetId] = useState('')
  const [thresholds, setThresholds] = useState<QualityThresholds>(DEFAULT_THRESHOLDS)

  // Quick check form state
  const [quickVersion, setQuickVersion] = useState('')
  const [quickDatasetId, setQuickDatasetId] = useState('')
  const [quickMinScore, setQuickMinScore] = useState(0.7)

  // Results state
  const [lastResult, setLastResult] = useState<QualityGateResult | null>(null)
  const [quickCheckResult, setQuickCheckResult] = useState<{ status: string; version: string; score: number } | null>(null)

  // Fetch available evaluation jobs as potential datasets
  const { data: jobsData } = useQuery({
    queryKey: ['evaluation-jobs-for-gate'],
    queryFn: async () => {
      const response = await evaluationApi.listJobs({ status: 'Completed', pageSize: 50 })
      return response.data.data
    },
  })

  // Execute quality gate mutation
  const executeMutation = useMutation({
    mutationFn: (request: QualityGateRequest) => qualityGateApi.execute(request),
    onSuccess: (response) => {
      const result = response.data.data
      if (result) {
        setLastResult(result)
        toast({
          title: result.passed ? 'Quality Gate Passed' : 'Quality Gate Failed',
          description: result.passed
            ? `Version ${result.systemVersion} meets all quality criteria.`
            : `${result.failedCriteria.length} criteria failed.`,
          variant: result.passed ? 'default' : 'destructive',
        })
      }
    },
    onError: (error: Error) => {
      toast({
        title: 'Quality Gate Execution Failed',
        description: error.message,
        variant: 'destructive',
      })
    },
  })

  // Quick check mutation
  const quickCheckMutation = useMutation({
    mutationFn: ({ version, datasetId, minScore }: { version: string; datasetId: string; minScore: number }) =>
      qualityGateApi.quickCheck(version, datasetId, minScore),
    onSuccess: (response) => {
      const result = response.data.data
      if (result) {
        setQuickCheckResult(result)
        toast({
          title: result.status === 'passed' ? 'Check Passed' : 'Check Failed',
          description: `Score: ${(result.score * 100).toFixed(1)}%`,
          variant: result.status === 'passed' ? 'default' : 'destructive',
        })
      }
    },
    onError: (error: Error) => {
      toast({
        title: 'Quick Check Failed',
        description: error.message,
        variant: 'destructive',
      })
    },
  })

  const handleExecute = () => {
    if (!systemVersion.trim()) {
      toast({ title: 'Version Required', description: 'Please enter a system version.', variant: 'destructive' })
      return
    }
    if (!selectedDatasetId) {
      toast({ title: 'Dataset Required', description: 'Please select a dataset.', variant: 'destructive' })
      return
    }

    executeMutation.mutate({
      systemVersion: systemVersion.trim(),
      datasetId: selectedDatasetId,
      thresholds,
    })
  }

  const handleQuickCheck = () => {
    if (!quickVersion.trim() || !quickDatasetId) {
      toast({ title: 'Missing Fields', description: 'Please fill in all fields.', variant: 'destructive' })
      return
    }

    quickCheckMutation.mutate({
      version: quickVersion.trim(),
      datasetId: quickDatasetId,
      minScore: quickMinScore,
    })
  }

  const updateThreshold = (key: keyof QualityThresholds, value: number) => {
    setThresholds(prev => ({ ...prev, [key]: Math.max(0, Math.min(1, value)) }))
  }

  const formatScore = (score: number | undefined) => {
    if (score === undefined) return '-'
    return (score * 100).toFixed(1) + '%'
  }

  const copyCliCommand = () => {
    const cmd = `curl -X POST "${window.location.origin}/api/v1/qualitygate/execute" \\
  -H "Content-Type: application/json" \\
  -d '{
    "systemVersion": "${systemVersion || 'v1.0.0'}",
    "datasetId": "${selectedDatasetId || '<dataset-id>'}",
    "thresholds": {
      "minPrecision": ${thresholds.minPrecision},
      "minRecall": ${thresholds.minRecall},
      "minF1Score": ${thresholds.minF1Score},
      "minMRR": ${thresholds.minMRR},
      "minNDCG": ${thresholds.minNDCG}
    }
  }'`

    navigator.clipboard.writeText(cmd)
    toast({ title: 'Copied!', description: 'CLI command copied to clipboard.' })
  }

  const completedJobs = jobsData?.items?.filter(j => j.status === 'Completed') || []

  return (
    <div className="container mx-auto p-6 space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold flex items-center gap-2">
            <Shield className="h-8 w-8" />
            Quality Gate
          </h1>
          <p className="text-muted-foreground">
            CI/CD integration for RAG system quality validation
          </p>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Configuration Panel */}
        <div className="lg:col-span-2">
          <Card>
            <CardHeader>
              <CardTitle>Quality Gate Configuration</CardTitle>
              <CardDescription>
                Define quality thresholds and execute gate checks
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Tabs value={activeTab} onValueChange={setActiveTab}>
                <TabsList className="grid w-full grid-cols-2">
                  <TabsTrigger value="execute">Full Execute</TabsTrigger>
                  <TabsTrigger value="quick">Quick Check</TabsTrigger>
                </TabsList>

                <TabsContent value="execute" className="space-y-6 mt-4">
                  {/* Basic Info */}
                  <div className="grid grid-cols-2 gap-4">
                    <div className="space-y-2">
                      <Label htmlFor="version">System Version</Label>
                      <Input
                        id="version"
                        placeholder="e.g., v1.2.3 or git-abc123"
                        value={systemVersion}
                        onChange={(e) => setSystemVersion(e.target.value)}
                      />
                      <p className="text-xs text-muted-foreground">
                        Git commit hash or semantic version
                      </p>
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="dataset">Golden Dataset</Label>
                      <Select value={selectedDatasetId} onValueChange={setSelectedDatasetId}>
                        <SelectTrigger>
                          <SelectValue placeholder="Select dataset..." />
                        </SelectTrigger>
                        <SelectContent>
                          {completedJobs.length === 0 ? (
                            <SelectItem value="none" disabled>
                              No completed evaluations
                            </SelectItem>
                          ) : (
                            completedJobs.map((job) => (
                              <SelectItem key={job.jobId} value={job.jobId}>
                                {job.jobName} ({job.totalQueries} queries)
                              </SelectItem>
                            ))
                          )}
                        </SelectContent>
                      </Select>
                      <p className="text-xs text-muted-foreground">
                        Use completed evaluation as reference
                      </p>
                    </div>
                  </div>

                  {/* Thresholds */}
                  <div className="space-y-4">
                    <h4 className="font-medium">Quality Thresholds</h4>
                    <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                      {[
                        { key: 'minPrecision', label: 'Min Precision' },
                        { key: 'minRecall', label: 'Min Recall' },
                        { key: 'minF1Score', label: 'Min F1 Score' },
                        { key: 'minMRR', label: 'Min MRR' },
                        { key: 'minNDCG', label: 'Min NDCG' },
                      ].map(({ key, label }) => (
                        <div key={key} className="space-y-2">
                          <Label htmlFor={key}>{label}</Label>
                          <div className="flex items-center gap-2">
                            <Input
                              id={key}
                              type="number"
                              min={0}
                              max={1}
                              step={0.05}
                              value={thresholds[key as keyof QualityThresholds]}
                              onChange={(e) => updateThreshold(key as keyof QualityThresholds, parseFloat(e.target.value))}
                              className="w-20"
                            />
                            <Progress
                              value={(thresholds[key as keyof QualityThresholds] ?? 0) * 100}
                              className="flex-1 h-2"
                            />
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* Actions */}
                  <div className="flex items-center justify-between pt-4 border-t">
                    <Button variant="outline" onClick={copyCliCommand}>
                      <Terminal className="h-4 w-4 mr-2" />
                      Copy CLI Command
                    </Button>
                    <Button
                      onClick={handleExecute}
                      disabled={executeMutation.isPending}
                    >
                      {executeMutation.isPending ? (
                        <>
                          <RefreshCw className="h-4 w-4 mr-2 animate-spin" />
                          Executing...
                        </>
                      ) : (
                        <>
                          <Play className="h-4 w-4 mr-2" />
                          Execute Gate
                        </>
                      )}
                    </Button>
                  </div>
                </TabsContent>

                <TabsContent value="quick" className="space-y-6 mt-4">
                  <div className="grid grid-cols-3 gap-4">
                    <div className="space-y-2">
                      <Label>Version</Label>
                      <Input
                        placeholder="v1.0.0"
                        value={quickVersion}
                        onChange={(e) => setQuickVersion(e.target.value)}
                      />
                    </div>
                    <div className="space-y-2">
                      <Label>Dataset</Label>
                      <Select value={quickDatasetId} onValueChange={setQuickDatasetId}>
                        <SelectTrigger>
                          <SelectValue placeholder="Select..." />
                        </SelectTrigger>
                        <SelectContent>
                          {completedJobs.map((job) => (
                            <SelectItem key={job.jobId} value={job.jobId}>
                              {job.jobName}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                    <div className="space-y-2">
                      <Label>Min Score</Label>
                      <Input
                        type="number"
                        min={0}
                        max={1}
                        step={0.05}
                        value={quickMinScore}
                        onChange={(e) => setQuickMinScore(parseFloat(e.target.value))}
                      />
                    </div>
                  </div>

                  <Button
                    onClick={handleQuickCheck}
                    disabled={quickCheckMutation.isPending}
                    className="w-full"
                  >
                    {quickCheckMutation.isPending ? (
                      <>
                        <RefreshCw className="h-4 w-4 mr-2 animate-spin" />
                        Checking...
                      </>
                    ) : (
                      <>
                        <Zap className="h-4 w-4 mr-2" />
                        Quick Check
                      </>
                    )}
                  </Button>

                  {quickCheckResult && (
                    <Card className={cn(
                      'border-2',
                      quickCheckResult.status === 'passed' ? 'border-green-500 bg-green-500/5' : 'border-red-500 bg-red-500/5'
                    )}>
                      <CardContent className="pt-6">
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-3">
                            {quickCheckResult.status === 'passed' ? (
                              <CheckCircle2 className="h-8 w-8 text-green-500" />
                            ) : (
                              <XCircle className="h-8 w-8 text-red-500" />
                            )}
                            <div>
                              <p className="font-medium">
                                {quickCheckResult.status === 'passed' ? 'Check Passed' : 'Check Failed'}
                              </p>
                              <p className="text-sm text-muted-foreground">
                                Version: {quickCheckResult.version}
                              </p>
                            </div>
                          </div>
                          <div className="text-right">
                            <p className="text-3xl font-bold">
                              {formatScore(quickCheckResult.score)}
                            </p>
                            <p className="text-sm text-muted-foreground">Overall Score</p>
                          </div>
                        </div>
                      </CardContent>
                    </Card>
                  )}
                </TabsContent>
              </Tabs>
            </CardContent>
          </Card>
        </div>

        {/* Results Panel */}
        <div className="space-y-4">
          {/* Status Card */}
          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-lg">Gate Status</CardTitle>
            </CardHeader>
            <CardContent>
              {lastResult ? (
                <div className="space-y-4">
                  <div className={cn(
                    'flex items-center gap-3 p-4 rounded-lg',
                    lastResult.passed ? 'bg-green-500/10' : 'bg-red-500/10'
                  )}>
                    {lastResult.passed ? (
                      <CheckCircle2 className="h-10 w-10 text-green-500" />
                    ) : (
                      <XCircle className="h-10 w-10 text-red-500" />
                    )}
                    <div>
                      <p className="font-semibold text-lg">
                        {lastResult.passed ? 'PASSED' : 'FAILED'}
                      </p>
                      <p className="text-sm text-muted-foreground">
                        {lastResult.systemVersion}
                      </p>
                    </div>
                  </div>

                  <div className="space-y-2 text-sm">
                    <div className="flex justify-between">
                      <span className="text-muted-foreground">Executed</span>
                      <span>{new Date(lastResult.executedAt).toLocaleString()}</span>
                    </div>
                    {lastResult.durationMs && (
                      <div className="flex justify-between">
                        <span className="text-muted-foreground">Duration</span>
                        <span>{lastResult.durationMs}ms</span>
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                <div className="text-center py-8 text-muted-foreground">
                  <Clock className="h-12 w-12 mx-auto mb-2 opacity-50" />
                  <p>No gate executed yet</p>
                  <p className="text-sm">Run a quality gate check</p>
                </div>
              )}
            </CardContent>
          </Card>

          {/* Metrics Card */}
          {lastResult?.metrics && (
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-lg">Achieved Metrics</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                {[
                  { label: 'MRR', value: lastResult.metrics.mrr, threshold: lastResult.appliedThresholds.minMRR },
                  { label: 'Precision', value: lastResult.metrics.precisionAtK, threshold: lastResult.appliedThresholds.minPrecision },
                  { label: 'Recall', value: lastResult.metrics.recallAtK, threshold: lastResult.appliedThresholds.minRecall },
                  { label: 'NDCG', value: lastResult.metrics.ndcg, threshold: lastResult.appliedThresholds.minNDCG },
                ].map(({ label, value, threshold }) => {
                  const passed = value >= threshold
                  return (
                    <div key={label} className="space-y-1">
                      <div className="flex justify-between text-sm">
                        <span>{label}</span>
                        <span className={cn(
                          'font-medium',
                          passed ? 'text-green-500' : 'text-red-500'
                        )}>
                          {formatScore(value)} / {formatScore(threshold)}
                        </span>
                      </div>
                      <div className="relative h-2 bg-muted rounded-full overflow-hidden">
                        <div
                          className={cn(
                            'absolute h-full rounded-full transition-all',
                            passed ? 'bg-green-500' : 'bg-red-500'
                          )}
                          style={{ width: `${Math.min(value * 100, 100)}%` }}
                        />
                        <div
                          className="absolute h-full w-0.5 bg-foreground/50"
                          style={{ left: `${threshold * 100}%` }}
                        />
                      </div>
                    </div>
                  )
                })}
              </CardContent>
            </Card>
          )}

          {/* Failed Criteria */}
          {lastResult && !lastResult.passed && lastResult.failedCriteria.length > 0 && (
            <Card className="border-red-500/50">
              <CardHeader className="pb-2">
                <CardTitle className="text-lg flex items-center gap-2 text-red-500">
                  <AlertTriangle className="h-5 w-5" />
                  Failed Criteria
                </CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="space-y-2">
                  {lastResult.failedCriteria.map((criteria, index) => (
                    <li key={index} className="flex items-start gap-2 text-sm">
                      <XCircle className="h-4 w-4 text-red-500 mt-0.5 flex-shrink-0" />
                      <span>{criteria}</span>
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          )}

          {/* CI/CD Integration Info */}
          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-lg">CI/CD Integration</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <p className="text-sm text-muted-foreground">
                Use quality gates in your deployment pipeline to prevent regressions.
              </p>
              <div className="bg-muted p-3 rounded-md">
                <code className="text-xs">
                  POST /api/v1/qualitygate/execute
                </code>
              </div>
              <div className="flex items-center gap-2">
                <Badge variant="outline">Exit Code 0</Badge>
                <span className="text-xs text-muted-foreground">= All thresholds met</span>
              </div>
              <div className="flex items-center gap-2">
                <Badge variant="destructive">Exit Code 1</Badge>
                <span className="text-xs text-muted-foreground">= One or more failures</span>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  )
}
