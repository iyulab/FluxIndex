import { Link, useLocation } from 'react-router-dom'
import { cn } from '@/lib/utils'
import {
  LayoutDashboard,
  FolderOpen,
  FileText,
  Search,
  Settings,
  Database,
} from 'lucide-react'

const navigation = [
  { name: 'Dashboard', href: '/dashboard', icon: LayoutDashboard },
  { name: 'Collections', href: '/collections', icon: FolderOpen },
  { name: 'Documents', href: '/documents', icon: FileText },
  { name: 'Search', href: '/search', icon: Search },
  { name: 'Settings', href: '/settings', icon: Settings },
]

export default function Sidebar() {
  const location = useLocation()

  return (
    <div className="flex flex-col w-64 bg-card border-r">
      <div className="flex items-center h-16 px-4 border-b">
        <Database className="h-8 w-8 text-primary mr-2" />
        <span className="text-xl font-bold">FluxIndex</span>
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
