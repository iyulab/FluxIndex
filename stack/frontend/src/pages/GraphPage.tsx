import { useState, useCallback } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  ReactFlow,
  MiniMap,
  Controls,
  Background,
  useNodesState,
  useEdgesState,
  addEdge,
  Node,
  Edge,
  Connection,
  MarkerType,
  BackgroundVariant,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { graphApi, GraphStatistics, EntityRelationship } from '@/lib/api'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { Label } from '@/components/ui/label'
import { useToast } from '@/hooks/use-toast'
import {
  Network,
  RefreshCw,
  Play,
  AlertCircle,
  Search,
  GitBranch,
  Users,
  Loader2,
  CircleDot,
  ArrowRight,
  Zap,
} from 'lucide-react'

// Entity type color mapping
const entityTypeColors: Record<string, string> = {
  Person: '#3b82f6',       // blue
  Organization: '#10b981', // green
  Location: '#f59e0b',     // amber
  Technology: '#8b5cf6',   // purple
  Product: '#ef4444',      // red
  Event: '#ec4899',        // pink
  Concept: '#06b6d4',      // cyan
  Date: '#84cc16',         // lime
  Money: '#f97316',        // orange
  Unknown: '#6b7280',      // gray
}

// Get entity type from entity ID (format: "type:name")
function getEntityType(entityId: string): string {
  const parts = entityId.split(':')
  if (parts.length >= 1) {
    return parts[0].charAt(0).toUpperCase() + parts[0].slice(1).toLowerCase()
  }
  return 'Unknown'
}

// Get entity name from entity ID
function getEntityName(entityId: string): string {
  const parts = entityId.split(':')
  if (parts.length >= 2) {
    return parts.slice(1).join(':')
  }
  return entityId
}

// Create nodes and edges from relationships
function createGraphElements(
  relationships: EntityRelationship[]
): { nodes: Node[]; edges: Edge[] } {
  const nodeMap = new Map<string, Node>()
  const edges: Edge[] = []

  relationships.forEach((rel, index) => {
    // Create source node if not exists
    if (!nodeMap.has(rel.sourceEntityId)) {
      const type = getEntityType(rel.sourceEntityId)
      nodeMap.set(rel.sourceEntityId, {
        id: rel.sourceEntityId,
        data: {
          label: getEntityName(rel.sourceEntityId),
          type,
        },
        position: { x: 0, y: 0 }, // Will be updated by layout
        style: {
          background: entityTypeColors[type] || entityTypeColors.Unknown,
          color: 'white',
          padding: '10px 15px',
          borderRadius: '8px',
          fontSize: '12px',
          fontWeight: 500,
          border: '2px solid rgba(255,255,255,0.3)',
          boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.1)',
        },
      })
    }

    // Create target node if not exists
    if (!nodeMap.has(rel.targetEntityId)) {
      const type = getEntityType(rel.targetEntityId)
      nodeMap.set(rel.targetEntityId, {
        id: rel.targetEntityId,
        data: {
          label: getEntityName(rel.targetEntityId),
          type,
        },
        position: { x: 0, y: 0 },
        style: {
          background: entityTypeColors[type] || entityTypeColors.Unknown,
          color: 'white',
          padding: '10px 15px',
          borderRadius: '8px',
          fontSize: '12px',
          fontWeight: 500,
          border: '2px solid rgba(255,255,255,0.3)',
          boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.1)',
        },
      })
    }

    // Create edge
    edges.push({
      id: `${rel.sourceEntityId}-${rel.relationshipType}-${rel.targetEntityId}-${index}`,
      source: rel.sourceEntityId,
      target: rel.targetEntityId,
      label: rel.relationshipType,
      labelStyle: { fontSize: 10, fontWeight: 500 },
      labelBgStyle: { fill: 'white', fillOpacity: 0.9 },
      labelBgPadding: [4, 2] as [number, number],
      animated: false,
      style: { stroke: '#94a3b8', strokeWidth: 2 },
      markerEnd: {
        type: MarkerType.ArrowClosed,
        color: '#94a3b8',
      },
    })
  })

  // Apply simple circular layout
  const nodes = Array.from(nodeMap.values())
  const radius = Math.max(200, nodes.length * 30)
  const angleStep = (2 * Math.PI) / nodes.length

  nodes.forEach((node, index) => {
    node.position = {
      x: 400 + radius * Math.cos(index * angleStep),
      y: 300 + radius * Math.sin(index * angleStep),
    }
  })

  return { nodes, edges }
}

export default function GraphPage() {
  const { toast } = useToast()
  const queryClient = useQueryClient()
  const [searchQuery, setSearchQuery] = useState('')
  const [pathSource, setPathSource] = useState('')
  const [pathTarget, setPathTarget] = useState('')

  // Graph states
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([])
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([])

  // Fetch graph statistics
  const { data: statsData, isLoading: statsLoading } = useQuery({
    queryKey: ['graph-statistics'],
    queryFn: async () => {
      const response = await graphApi.getStatistics()
      return response.data.data
    },
  })

  // Fetch graph health
  const { data: healthData } = useQuery({
    queryKey: ['graph-health'],
    queryFn: async () => {
      const response = await graphApi.getHealth()
      return response.data.data
    },
    retry: false,
  })

  // Query expansion mutation
  const expandQueryMutation = useMutation({
    mutationFn: async (query: string) => {
      const response = await graphApi.expandQuery({ query, maxEntities: 10 })
      return response.data.data
    },
    onSuccess: (data) => {
      if (data && data.relatedTerms.length > 0) {
        toast({
          title: 'Query Expanded',
          description: `Found ${data.relatedTerms.length} related terms`,
        })
      } else {
        toast({
          title: 'No Related Terms',
          description: 'No related entities found in the knowledge graph',
          variant: 'destructive',
        })
      }
    },
  })

  // Find paths mutation
  const findPathsMutation = useMutation({
    mutationFn: async ({ source, target }: { source: string; target: string }) => {
      const response = await graphApi.findPaths({
        sourceEntityId: source,
        targetEntityId: target,
        maxPathLength: 5,
      })
      return response.data.data
    },
    onSuccess: (data) => {
      if (data && data.paths.length > 0) {
        toast({
          title: 'Paths Found',
          description: `Found ${data.paths.length} path(s) between entities`,
        })
        // Highlight path nodes/edges could be added here
      } else {
        toast({
          title: 'No Paths Found',
          description: 'No connection found between the specified entities',
          variant: 'destructive',
        })
      }
    },
  })

  // Community detection mutation
  const communityDetectionMutation = useMutation({
    mutationFn: async () => {
      const response = await graphApi.runCommunityDetection()
      return response.data.data
    },
    onSuccess: (data) => {
      if (data) {
        toast({
          title: 'Community Detection Complete',
          description: `Detected ${data.communitiesDetected} communities in ${data.executionTimeMs.toFixed(0)}ms`,
        })
        queryClient.invalidateQueries({ queryKey: ['graph-statistics'] })
      }
    },
    onError: () => {
      toast({
        title: 'Community Detection Failed',
        description: 'Failed to run community detection algorithm',
        variant: 'destructive',
      })
    },
  })

  // Get related entities mutation
  const getRelatedEntitiesMutation = useMutation({
    mutationFn: async (entityIds: string[]) => {
      const response = await graphApi.getRelatedEntities({ entityIds, maxHops: 2 })
      return response.data.data
    },
    onSuccess: (data) => {
      if (data && data.relationships.length > 0) {
        const { nodes: newNodes, edges: newEdges } = createGraphElements(data.relationships)
        setNodes(newNodes)
        setEdges(newEdges)
        toast({
          title: 'Graph Loaded',
          description: `Loaded ${newNodes.length} entities and ${newEdges.length} relationships`,
        })
      } else {
        toast({
          title: 'No Relationships',
          description: 'No relationships found for the specified entities',
          variant: 'destructive',
        })
      }
    },
  })

  const onConnect = useCallback(
    (params: Connection) => setEdges((eds) => addEdge(params, eds)),
    [setEdges]
  )

  const handleSearch = () => {
    if (!searchQuery.trim()) return
    // Search for entities containing the query
    const entityId = searchQuery.toLowerCase().includes(':')
      ? searchQuery.toLowerCase()
      : `unknown:${searchQuery.toLowerCase()}`
    getRelatedEntitiesMutation.mutate([entityId])
  }

  const handleExpandQuery = () => {
    if (!searchQuery.trim()) return
    expandQueryMutation.mutate(searchQuery)
  }

  const handleFindPaths = () => {
    if (!pathSource.trim() || !pathTarget.trim()) {
      toast({
        title: 'Missing Input',
        description: 'Please enter both source and target entity IDs',
        variant: 'destructive',
      })
      return
    }
    findPathsMutation.mutate({ source: pathSource, target: pathTarget })
  }

  const isGraphAvailable = healthData?.isAvailable ?? false

  // Statistics summary
  const stats: GraphStatistics | undefined = statsData

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight flex items-center gap-2">
            <Network className="h-8 w-8" />
            Knowledge Graph
          </h1>
          <p className="text-muted-foreground">
            Visualize and explore entity relationships in your documents
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Badge variant={isGraphAvailable ? 'default' : 'destructive'}>
            {isGraphAvailable ? 'Neo4j Connected' : 'Neo4j Unavailable'}
          </Badge>
          <Button
            variant="outline"
            size="sm"
            onClick={() => queryClient.invalidateQueries({ queryKey: ['graph-statistics'] })}
          >
            <RefreshCw className="h-4 w-4 mr-2" />
            Refresh
          </Button>
        </div>
      </div>

      {/* Statistics Cards */}
      <div className="grid gap-4 md:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Total Entities</CardTitle>
            <CircleDot className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">
              {statsLoading ? <Loader2 className="h-5 w-5 animate-spin" /> : (stats?.totalNodes ?? 0).toLocaleString()}
            </div>
            <p className="text-xs text-muted-foreground">
              Nodes in the knowledge graph
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Total Relationships</CardTitle>
            <ArrowRight className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">
              {statsLoading ? <Loader2 className="h-5 w-5 animate-spin" /> : (stats?.totalRelationships ?? 0).toLocaleString()}
            </div>
            <p className="text-xs text-muted-foreground">
              Edges connecting entities
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Communities</CardTitle>
            <Users className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">
              {statsLoading ? <Loader2 className="h-5 w-5 animate-spin" /> : (stats?.totalCommunities ?? 0).toLocaleString()}
            </div>
            <p className="text-xs text-muted-foreground">
              Detected entity clusters
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Entity Types</CardTitle>
            <GitBranch className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">
              {statsLoading ? <Loader2 className="h-5 w-5 animate-spin" /> : Object.keys(stats?.nodesByType ?? {}).length}
            </div>
            <p className="text-xs text-muted-foreground">
              Distinct entity categories
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Entity Types Breakdown */}
      {stats?.nodesByType && Object.keys(stats.nodesByType).length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Entity Types Distribution</CardTitle>
            <CardDescription>Breakdown of entities by type</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap gap-2">
              {Object.entries(stats.nodesByType).map(([type, count]) => (
                <Badge
                  key={type}
                  variant="outline"
                  style={{
                    borderColor: entityTypeColors[type] || entityTypeColors.Unknown,
                    color: entityTypeColors[type] || entityTypeColors.Unknown,
                  }}
                >
                  {type}: {count}
                </Badge>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Search and Exploration Tools */}
      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Search className="h-5 w-5" />
              Entity Search
            </CardTitle>
            <CardDescription>
              Search for entities and explore their relationships
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex gap-2">
              <Input
                placeholder="Enter entity name or ID (e.g., person:john)"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              />
              <Button
                onClick={handleSearch}
                disabled={!isGraphAvailable || getRelatedEntitiesMutation.isPending}
              >
                {getRelatedEntitiesMutation.isPending ? (
                  <Loader2 className="h-4 w-4 animate-spin" />
                ) : (
                  <Search className="h-4 w-4" />
                )}
              </Button>
            </div>
            <Button
              variant="outline"
              onClick={handleExpandQuery}
              disabled={!searchQuery.trim() || expandQueryMutation.isPending}
              className="w-full"
            >
              {expandQueryMutation.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin mr-2" />
              ) : (
                <Zap className="h-4 w-4 mr-2" />
              )}
              Expand Query with Related Entities
            </Button>
            {expandQueryMutation.data && (
              <div className="text-sm">
                <p className="font-medium">Expanded Query:</p>
                <p className="text-muted-foreground">{expandQueryMutation.data.expandedQuery}</p>
                {expandQueryMutation.data.relatedTerms.length > 0 && (
                  <div className="mt-2 flex flex-wrap gap-1">
                    {expandQueryMutation.data.relatedTerms.map((term, i) => (
                      <Badge key={i} variant="secondary">{term}</Badge>
                    ))}
                  </div>
                )}
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <GitBranch className="h-5 w-5" />
              Path Finder
            </CardTitle>
            <CardDescription>
              Find paths between two entities in the graph
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label>Source Entity ID</Label>
              <Input
                placeholder="e.g., person:john"
                value={pathSource}
                onChange={(e) => setPathSource(e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label>Target Entity ID</Label>
              <Input
                placeholder="e.g., organization:acme"
                value={pathTarget}
                onChange={(e) => setPathTarget(e.target.value)}
              />
            </div>
            <Button
              onClick={handleFindPaths}
              disabled={!isGraphAvailable || findPathsMutation.isPending}
              className="w-full"
            >
              {findPathsMutation.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin mr-2" />
              ) : (
                <GitBranch className="h-4 w-4 mr-2" />
              )}
              Find Paths
            </Button>
            {findPathsMutation.data && findPathsMutation.data.paths.length > 0 && (
              <div className="text-sm space-y-2">
                <p className="font-medium">Found {findPathsMutation.data.paths.length} path(s):</p>
                {findPathsMutation.data.paths.slice(0, 3).map((path, i) => (
                  <div key={i} className="text-muted-foreground text-xs">
                    {path.entityIds.join(' → ')}
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Graph Actions */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Users className="h-5 w-5" />
            Graph Operations
          </CardTitle>
          <CardDescription>
            Run algorithms and operations on the knowledge graph
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button
            onClick={() => communityDetectionMutation.mutate()}
            disabled={!isGraphAvailable || communityDetectionMutation.isPending}
          >
            {communityDetectionMutation.isPending ? (
              <Loader2 className="h-4 w-4 animate-spin mr-2" />
            ) : (
              <Play className="h-4 w-4 mr-2" />
            )}
            Run Community Detection
          </Button>
        </CardContent>
      </Card>

      {/* Graph Visualization */}
      <Card>
        <CardHeader>
          <CardTitle>Graph Visualization</CardTitle>
          <CardDescription>
            Interactive visualization of entity relationships.
            Search for an entity above to load its neighborhood.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="h-[500px] border rounded-lg overflow-hidden bg-slate-50 dark:bg-slate-900">
            {!isGraphAvailable ? (
              <div className="h-full flex items-center justify-center">
                <div className="text-center text-muted-foreground">
                  <AlertCircle className="h-12 w-12 mx-auto mb-4 opacity-50" />
                  <p className="font-medium">Neo4j Graph Service Unavailable</p>
                  <p className="text-sm mt-2">
                    Connect Neo4j to enable graph visualization
                  </p>
                </div>
              </div>
            ) : nodes.length === 0 ? (
              <div className="h-full flex items-center justify-center">
                <div className="text-center text-muted-foreground">
                  <Network className="h-12 w-12 mx-auto mb-4 opacity-50" />
                  <p className="font-medium">No Graph Data Loaded</p>
                  <p className="text-sm mt-2">
                    Search for an entity to visualize its relationships
                  </p>
                </div>
              </div>
            ) : (
              <ReactFlow
                nodes={nodes}
                edges={edges}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onConnect={onConnect}
                fitView
                attributionPosition="bottom-left"
              >
                <Controls />
                <MiniMap
                  nodeColor={(node) => {
                    const type = node.data?.type as string
                    return entityTypeColors[type] || entityTypeColors.Unknown
                  }}
                  maskColor="rgba(0, 0, 0, 0.1)"
                />
                <Background variant={BackgroundVariant.Dots} gap={12} size={1} />
              </ReactFlow>
            )}
          </div>
        </CardContent>
      </Card>

      {/* Legend */}
      <Card>
        <CardHeader>
          <CardTitle>Entity Type Legend</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex flex-wrap gap-4">
            {Object.entries(entityTypeColors).map(([type, color]) => (
              <div key={type} className="flex items-center gap-2">
                <div
                  className="w-4 h-4 rounded"
                  style={{ backgroundColor: color }}
                />
                <span className="text-sm">{type}</span>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
