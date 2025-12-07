import { useQuery } from '@tanstack/react-query'
import { ChevronDown, Database, Loader2, FolderOpen } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { collectionsApi, type Collection } from '@/lib/api'
import { useStore } from '@/store/useStore'
import { cn } from '@/lib/utils'

export default function CollectionSelector() {
  const { selectedCollectionId, setSelectedCollectionId } = useStore()

  const { data, isLoading, error } = useQuery({
    queryKey: ['collections'],
    queryFn: async () => {
      const response = await collectionsApi.getAll()
      return response.data.data || []
    },
  })

  const collections = data || []
  const selectedCollection = collections.find(c => c.id === selectedCollectionId)

  const handleSelect = (collection: Collection | null) => {
    setSelectedCollectionId(collection?.id || null)
  }

  if (error) {
    return (
      <Button variant="outline" size="sm" disabled className="text-destructive">
        <Database className="h-4 w-4 mr-2" />
        Error loading collections
      </Button>
    )
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className="min-w-[180px] justify-between">
          {isLoading ? (
            <>
              <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              Loading...
            </>
          ) : selectedCollection ? (
            <>
              <Database className="h-4 w-4 mr-2 text-primary" />
              <span className="truncate max-w-[120px]">{selectedCollection.name}</span>
              <span className="ml-1 text-xs text-muted-foreground">
                ({selectedCollection.documentCount})
              </span>
            </>
          ) : (
            <>
              <FolderOpen className="h-4 w-4 mr-2 text-muted-foreground" />
              <span className="text-muted-foreground">All Collections</span>
            </>
          )}
          <ChevronDown className="h-4 w-4 ml-2 shrink-0 opacity-50" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="w-[220px]">
        <DropdownMenuLabel>Select Collection</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onClick={() => handleSelect(null)}
          className={cn(
            'cursor-pointer',
            !selectedCollectionId && 'bg-accent'
          )}
        >
          <FolderOpen className="h-4 w-4 mr-2" />
          All Collections
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        {collections.length === 0 ? (
          <div className="px-2 py-4 text-center text-sm text-muted-foreground">
            No collections found.
            <br />
            <span className="text-xs">Create one to get started.</span>
          </div>
        ) : (
          collections.map((collection) => (
            <DropdownMenuItem
              key={collection.id}
              onClick={() => handleSelect(collection)}
              className={cn(
                'cursor-pointer',
                selectedCollectionId === collection.id && 'bg-accent'
              )}
            >
              <Database className="h-4 w-4 mr-2" />
              <span className="truncate flex-1">{collection.name}</span>
              <span className="ml-2 text-xs text-muted-foreground">
                {collection.documentCount}
              </span>
            </DropdownMenuItem>
          ))
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
