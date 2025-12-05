import { useState } from 'react'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useStore } from '@/store/useStore'
import { useToast } from '@/hooks/use-toast'
import { Key, Moon, Sun, Trash2 } from 'lucide-react'

export default function SettingsPage() {
  const { apiKey, setApiKey, theme, setTheme } = useStore()
  const [newApiKey, setNewApiKey] = useState(apiKey || '')
  const { toast } = useToast()

  const handleSaveApiKey = () => {
    setApiKey(newApiKey || null)
    toast({ title: 'API key saved successfully' })
  }

  const handleClearApiKey = () => {
    setApiKey(null)
    setNewApiKey('')
    toast({ title: 'API key cleared' })
  }

  const toggleTheme = () => {
    setTheme(theme === 'light' ? 'dark' : 'light')
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">Settings</h2>
        <p className="text-muted-foreground">
          Configure your FluxIndex Service settings
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center space-x-2">
            <Key className="h-5 w-5" />
            <span>API Key</span>
          </CardTitle>
          <CardDescription>
            Configure your API key for authentication
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div>
            <label className="text-sm font-medium">API Key</label>
            <Input
              type="password"
              placeholder="Enter your API key"
              value={newApiKey}
              onChange={(e) => setNewApiKey(e.target.value)}
            />
            <p className="text-xs text-muted-foreground mt-1">
              Your API key is stored locally and used for authenticating requests
            </p>
          </div>
          <div className="flex space-x-2">
            <Button onClick={handleSaveApiKey}>Save API Key</Button>
            {apiKey && (
              <Button variant="outline" onClick={handleClearApiKey}>
                <Trash2 className="mr-2 h-4 w-4" />
                Clear
              </Button>
            )}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center space-x-2">
            {theme === 'light' ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
            <span>Appearance</span>
          </CardTitle>
          <CardDescription>
            Customize the look and feel of the application
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center justify-between">
            <div>
              <p className="font-medium">Theme</p>
              <p className="text-sm text-muted-foreground">
                Current theme: {theme === 'light' ? 'Light' : 'Dark'}
              </p>
            </div>
            <Button variant="outline" onClick={toggleTheme}>
              {theme === 'light' ? (
                <>
                  <Moon className="mr-2 h-4 w-4" />
                  Switch to Dark
                </>
              ) : (
                <>
                  <Sun className="mr-2 h-4 w-4" />
                  Switch to Light
                </>
              )}
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>About</CardTitle>
          <CardDescription>
            Information about FluxIndex Service
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Version</span>
              <span className="font-medium">0.1.0</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">API Version</span>
              <span className="font-medium">v1</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Documentation</span>
              <a href="/swagger" className="font-medium text-primary hover:underline">
                API Documentation
              </a>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
