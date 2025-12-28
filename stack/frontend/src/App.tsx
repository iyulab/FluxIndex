import { Routes, Route, Navigate } from 'react-router-dom'
import { Toaster } from '@/components/ui/toaster'
import Layout from '@/components/layout/Layout'
import DashboardPage from '@/pages/DashboardPage'
import DocumentsPage from '@/pages/DocumentsPage'
import DocumentDetailPage from '@/pages/DocumentDetailPage'
import JobsPage from '@/pages/JobsPage'
import SearchPage from '@/pages/SearchPage'
import GraphPage from '@/pages/GraphPage'
import EvaluationPage from '@/pages/EvaluationPage'
import QualityGatePage from '@/pages/QualityGatePage'
import SettingsPage from '@/pages/SettingsPage'

function App() {
  return (
    <>
      <Routes>
        <Route path="/" element={<Layout />}>
          <Route index element={<Navigate to="/dashboard" replace />} />
          <Route path="dashboard" element={<DashboardPage />} />
          <Route path="documents" element={<DocumentsPage />} />
          <Route path="documents/:id" element={<DocumentDetailPage />} />
          <Route path="jobs" element={<JobsPage />} />
          <Route path="search" element={<SearchPage />} />
          <Route path="graph" element={<GraphPage />} />
          <Route path="evaluation" element={<EvaluationPage />} />
          <Route path="quality-gate" element={<QualityGatePage />} />
          <Route path="settings" element={<SettingsPage />} />
        </Route>
      </Routes>
      <Toaster />
    </>
  )
}

export default App
