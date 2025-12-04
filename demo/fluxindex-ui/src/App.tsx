import { useState } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import Sidebar from './components/Sidebar';
import { ToastProvider } from './components/Toast';
import SearchPage from './pages/SearchPage';
import UploadPage from './pages/UploadPage';
import DocumentsPage from './pages/DocumentsPage';
import McpPage from './pages/McpPage';
import './styles/globals.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30000,
      retry: 1,
    },
  },
});

function AppContent() {
  const [activeTab, setActiveTab] = useState('search');

  const renderPage = () => {
    switch (activeTab) {
      case 'search':
        return <SearchPage />;
      case 'upload':
        return <UploadPage />;
      case 'documents':
        return <DocumentsPage />;
      case 'mcp':
        return <McpPage />;
      default:
        return <SearchPage />;
    }
  };

  return (
    <div className="app">
      <Sidebar activeTab={activeTab} onTabChange={setActiveTab} />
      <main className="main-content">{renderPage()}</main>
    </div>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <AppContent />
      </ToastProvider>
    </QueryClientProvider>
  );
}
