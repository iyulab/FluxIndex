import { Routes, Route, Navigate } from 'react-router-dom'
import { Toaster } from '@/components/ui/toaster'
import Layout from '@/components/layout/Layout'
import DashboardPage from '@/pages/DashboardPage'
import CollectionsPage from '@/pages/CollectionsPage'
import DocumentsPage from '@/pages/DocumentsPage'
import SearchPage from '@/pages/SearchPage'
import SettingsPage from '@/pages/SettingsPage'

function App() {
  return (
    <>
      <Routes>
        <Route path="/" element={<Layout />}>
          <Route index element={<Navigate to="/dashboard" replace />} />
          <Route path="dashboard" element={<DashboardPage />} />
          <Route path="collections" element={<CollectionsPage />} />
          <Route path="documents" element={<DocumentsPage />} />
          <Route path="search" element={<SearchPage />} />
          <Route path="settings" element={<SettingsPage />} />
        </Route>
      </Routes>
      <Toaster />
    </>
  )
}

export default App
