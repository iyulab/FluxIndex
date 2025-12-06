import { useQuery } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { analyticsApi } from '@/lib/api'
import { formatBytes } from '@/lib/utils'
import { FileText, FolderOpen, Database, CheckCircle, Clock, XCircle } from 'lucide-react'

export default function DashboardPage() {
  const { data: stats, isLoading } = useQuery({
    queryKey: ['systemStats'],
    queryFn: async () => {
      const response = await analyticsApi.getSystemStats()
      return response.data.data
    },
  })

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary" />
      </div>
    )
  }

  const statCards = [
    {
      title: 'Total Documents',
      value: stats?.totalDocuments ?? 0,
      description: 'Documents in the system',
      icon: FileText,
    },
    {
      title: 'Total Collections',
      value: stats?.totalCollections ?? 0,
      description: 'Organized collections',
      icon: FolderOpen,
    },
    {
      title: 'Total Chunks',
      value: stats?.totalChunks ?? 0,
      description: 'Indexed text chunks',
      icon: Database,
    },
    {
      title: 'Storage Used',
      value: formatBytes(stats?.totalStorageBytes ?? 0),
      description: 'Total storage consumption',
      icon: Database,
    },
  ]

  const statusCards = [
    {
      title: 'Indexed',
      value: stats?.indexedDocuments ?? 0,
      icon: CheckCircle,
      color: 'text-green-500',
    },
    {
      title: 'Pending',
      value: stats?.pendingDocuments ?? 0,
      icon: Clock,
      color: 'text-yellow-500',
    },
    {
      title: 'Failed',
      value: stats?.failedDocuments ?? 0,
      icon: XCircle,
      color: 'text-red-500',
    },
  ]

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">Dashboard</h2>
        <p className="text-muted-foreground">
          Overview of your FluxIndex Service
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {statCards.map((stat) => (
          <Card key={stat.title}>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">{stat.title}</CardTitle>
              <stat.icon className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{stat.value}</div>
              <p className="text-xs text-muted-foreground">{stat.description}</p>
            </CardContent>
          </Card>
        ))}
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <Card className="col-span-2">
          <CardHeader>
            <CardTitle>Document Status</CardTitle>
            <CardDescription>Current indexing status of documents</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="flex space-x-8">
              {statusCards.map((status) => (
                <div key={status.title} className="flex items-center space-x-2">
                  <status.icon className={`h-5 w-5 ${status.color}`} />
                  <div>
                    <p className="text-sm font-medium">{status.title}</p>
                    <p className="text-2xl font-bold">{status.value}</p>
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Quick Actions</CardTitle>
            <CardDescription>Common tasks</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            <a
              href="/documents"
              className="block p-3 rounded-lg hover:bg-accent transition-colors"
            >
              <p className="font-medium">Upload Documents</p>
              <p className="text-sm text-muted-foreground">Add new documents to index</p>
            </a>
            <a
              href="/search"
              className="block p-3 rounded-lg hover:bg-accent transition-colors"
            >
              <p className="font-medium">Search</p>
              <p className="text-sm text-muted-foreground">Query your knowledge base</p>
            </a>
            <a
              href="/collections"
              className="block p-3 rounded-lg hover:bg-accent transition-colors"
            >
              <p className="font-medium">Manage Collections</p>
              <p className="text-sm text-muted-foreground">Organize your documents</p>
            </a>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
