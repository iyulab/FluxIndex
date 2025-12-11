import { Link, useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { cn } from '@/lib/utils'
import { collectionsApi } from '@/lib/api'
import { useStore } from '@/store/useStore'
import {
  LayoutDashboard,
  FileText,
  Search,
  Settings,
  Database,
  FolderOpen,
  ChevronDown,
  X,
  ListTodo,
  Target,
  Shield,
} from 'lucide-react'
import { useState } from 'react'

const navigation = [
  { name: 'Overview', href: '/dashboard', icon: LayoutDashboard },
  { name: 'Documents', href: '/documents', icon: FileText },
  { name: 'Jobs', href: '/jobs', icon: ListTodo },
  { name: 'Search', href: '/search', icon: Search },
  { name: 'Evaluation', href: '/evaluation', icon: Target },
  { name: 'Quality Gate', href: '/quality-gate', icon: Shield },
  { name: 'Settings', href: '/settings', icon: Settings },
]

export default function Sidebar() {
  const location = useLocation()
  const [scopeOpen, setScopeOpen] = useState(false)
  const { selectedCollectionId, setSelectedCollectionId } = useStore()

  const { data: collections } = useQuery({
    queryKey: ['collections'],
    queryFn: async () => {
      const response = await collectionsApi.getAll()
      return response.data.data || []
    },
  })

  const selectedCollection = collections?.find(c => c.id === selectedCollectionId)

  return (
    <div className="flex flex-col w-64 bg-card border-r">
      <div className="flex items-center h-16 px-4 border-b">
        <Database className="h-8 w-8 text-primary mr-2" />
        <span className="text-xl font-bold">FluxIndex</span>
      </div>

      {/* Search Scope Filter */}
      <div className="p-3 border-b">
        <div className="text-xs font-medium text-muted-foreground mb-2 px-1">
          SEARCH SCOPE
        </div>
        <div className="relative">
          <button
            onClick={() => setScopeOpen(!scopeOpen)}
            className={cn(
              'w-full flex items-center justify-between px-3 py-2 text-sm rounded-md transition-colors',
              'bg-muted/50 hover:bg-muted'
            )}
          >
            <div className="flex items-center gap-2">
              {selectedCollectionId ? (
                <>
                  <FolderOpen className="h-4 w-4 text-primary" />
                  <span className="truncate">{selectedCollection?.name || 'Loading...'}</span>
                </>
              ) : (
                <>
                  <Database className="h-4 w-4" />
                  <span>All Documents</span>
                </>
              )}
            </div>
            <ChevronDown className={cn('h-4 w-4 transition-transform', scopeOpen && 'rotate-180')} />
          </button>

          {scopeOpen && (
            <div className="absolute top-full left-0 right-0 mt-1 bg-popover border rounded-md shadow-lg z-50 py-1">
              <button
                onClick={() => {
                  setSelectedCollectionId(null)
                  setScopeOpen(false)
                }}
                className={cn(
                  'w-full flex items-center gap-2 px-3 py-2 text-sm hover:bg-accent',
                  !selectedCollectionId && 'bg-accent'
                )}
              >
                <Database className="h-4 w-4" />
                <span>All Documents</span>
              </button>

              {collections && collections.length > 0 && (
                <>
                  <div className="border-t my-1" />
                  <div className="px-3 py-1 text-xs text-muted-foreground">Collections</div>
                  {collections.map((collection) => (
                    <button
                      key={collection.id}
                      onClick={() => {
                        setSelectedCollectionId(collection.id)
                        setScopeOpen(false)
                      }}
                      className={cn(
                        'w-full flex items-center justify-between px-3 py-2 text-sm hover:bg-accent',
                        selectedCollectionId === collection.id && 'bg-accent'
                      )}
                    >
                      <div className="flex items-center gap-2">
                        <FolderOpen className="h-4 w-4" />
                        <span className="truncate">{collection.name}</span>
                      </div>
                      <span className="text-xs text-muted-foreground">{collection.documentCount}</span>
                    </button>
                  ))}
                </>
              )}
            </div>
          )}
        </div>

        {selectedCollectionId && (
          <button
            onClick={() => setSelectedCollectionId(null)}
            className="mt-2 w-full flex items-center justify-center gap-1 px-2 py-1 text-xs text-muted-foreground hover:text-foreground"
          >
            <X className="h-3 w-3" />
            Clear filter
          </button>
        )}
      </div>

      <nav className="flex-1 p-4 space-y-1">
        {navigation.map((item) => {
          const isActive = location.pathname === item.href
          return (
            <Link
              key={item.name}
              to={item.href}
              className={cn(
                'flex items-center px-4 py-2 text-sm font-medium rounded-md transition-colors',
                isActive
                  ? 'bg-primary text-primary-foreground'
                  : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
              )}
            >
              <item.icon className="mr-3 h-5 w-5" />
              {item.name}
            </Link>
          )
        })}
      </nav>
      <div className="p-4 border-t">
        <p className="text-xs text-muted-foreground">
          FluxIndex Service v0.1.0
        </p>
      </div>
    </div>
  )
}
