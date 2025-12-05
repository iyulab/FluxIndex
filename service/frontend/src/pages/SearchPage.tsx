import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { searchApi, type SearchRequest, type SearchResponse } from '@/lib/api'
import { truncate } from '@/lib/utils'
import { Search, FileText, Zap } from 'lucide-react'

export default function SearchPage() {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResponse | null>(null)

  const searchMutation = useMutation({
    mutationFn: (request: SearchRequest) => searchApi.search(request),
    onSuccess: (response) => {
      setResults(response.data.data as SearchResponse)
    },
  })

  const handleSearch = () => {
    if (!query.trim()) return
    searchMutation.mutate({
      query,
      topK: 10,
      mode: 'Hybrid',
      includeContent: true,
      includeMetadata: true,
    })
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleSearch()
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">Search</h2>
        <p className="text-muted-foreground">
          Search your knowledge base using semantic search
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Search Query</CardTitle>
          <CardDescription>
            Enter your search query to find relevant documents
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex space-x-4">
            <Input
              placeholder="Enter your search query..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={handleKeyDown}
              className="flex-1"
            />
            <Button onClick={handleSearch} disabled={searchMutation.isPending}>
              <Search className="mr-2 h-4 w-4" />
              {searchMutation.isPending ? 'Searching...' : 'Search'}
            </Button>
          </div>
        </CardContent>
      </Card>

      {results && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle>Results</CardTitle>
                <CardDescription>
                  Found {results.totalResults} results in {results.executionTimeMs.toFixed(2)}ms
                </CardDescription>
              </div>
              <div className="flex items-center space-x-2 text-sm text-muted-foreground">
                <Zap className="h-4 w-4" />
                <span>{results.mode} Search</span>
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {results.results.length > 0 ? (
              <div className="space-y-4">
                {results.results.map((result, index) => (
                  <div
                    key={result.chunkId}
                    className="p-4 border rounded-lg hover:bg-accent transition-colors"
                  >
                    <div className="flex items-start justify-between mb-2">
                      <div className="flex items-center space-x-2">
                        <span className="flex items-center justify-center w-6 h-6 text-xs font-medium bg-primary text-primary-foreground rounded-full">
                          {index + 1}
                        </span>
                        <FileText className="h-4 w-4 text-muted-foreground" />
                        <span className="font-medium">{result.documentTitle}</span>
                      </div>
                      <div className="flex items-center space-x-4 text-sm">
                        <span className="text-muted-foreground">
                          Score: {(result.score * 100).toFixed(1)}%
                        </span>
                        {result.vectorScore && (
                          <span className="text-muted-foreground">
                            Vector: {(result.vectorScore * 100).toFixed(1)}%
                          </span>
                        )}
                      </div>
                    </div>
                    {result.content && (
                      <p className="text-sm text-muted-foreground mt-2">
                        {truncate(result.content, 300)}
                      </p>
                    )}
                    {result.highlights && result.highlights.length > 0 && (
                      <div className="mt-2 p-2 bg-yellow-50 dark:bg-yellow-900/20 rounded text-sm">
                        {result.highlights.map((highlight, i) => (
                          <span key={i} dangerouslySetInnerHTML={{ __html: highlight }} />
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center py-12">
                <Search className="h-12 w-12 text-muted-foreground mb-4" />
                <h3 className="text-lg font-medium">No results found</h3>
                <p className="text-sm text-muted-foreground">
                  Try a different search query
                </p>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {!results && !searchMutation.isPending && (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-12">
            <Search className="h-12 w-12 text-muted-foreground mb-4" />
            <h3 className="text-lg font-medium">Start Searching</h3>
            <p className="text-sm text-muted-foreground">
              Enter a query above to search your knowledge base
            </p>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
