import { Zap, Search, Upload, Files, Plug, ScrollText } from 'lucide-react';
import { useStatus } from '../hooks/useApi';
import './Sidebar.css';

interface SidebarProps {
  activeTab: string;
  onTabChange: (tab: string) => void;
}

const navItems = [
  { id: 'search', label: 'Search', icon: Search },
  { id: 'upload', label: 'Upload', icon: Upload },
  { id: 'documents', label: 'Documents', icon: Files },
  { id: 'mcp', label: 'MCP Test', icon: Plug },
  { id: 'logs', label: 'Logs', icon: ScrollText },
];

function formatNumber(num: number): string {
  if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`;
  if (num >= 1000) return `${(num / 1000).toFixed(1)}K`;
  return num.toString();
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '-';
  try {
    const date = new Date(dateStr);
    return date.toLocaleDateString('ko-KR', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return dateStr;
  }
}

export default function Sidebar({ activeTab, onTabChange }: SidebarProps) {
  const { data: status } = useStatus();

  return (
    <aside className="sidebar">
      <div className="sidebar-header">
        <div className="logo">
          <span className="logo-icon"><Zap size={20} /></span>
          <span className="logo-text">FluxIndex</span>
        </div>
        <span className="version">v1.0</span>
      </div>

      <nav className="sidebar-nav">
        {navItems.map((item) => (
          <button
            key={item.id}
            className={`nav-item ${activeTab === item.id ? 'active' : ''}`}
            onClick={() => onTabChange(item.id)}
          >
            <span className="nav-icon"><item.icon size={18} /></span>
            <span>{item.label}</span>
          </button>
        ))}
      </nav>

      <div className="sidebar-stats">
        <div className="stat-card">
          <div className="stat-value">{formatNumber(status?.totalDocuments ?? 0)}</div>
          <div className="stat-label">Documents</div>
        </div>
        <div className="stat-card">
          <div className="stat-value">{formatNumber(status?.totalChunks ?? 0)}</div>
          <div className="stat-label">Chunks</div>
        </div>
      </div>

      <div className="sidebar-footer">
        <div className="model-info">
          <span className="model-label">Embedding</span>
          <span className="model-name">{status?.embeddingModel ?? '-'}</span>
        </div>
        <div className="model-info">
          <span className="model-label">LLM</span>
          <span className="model-name">{status?.completionModel ?? '-'}</span>
        </div>
        <div className="last-indexed">
          <span>Last Indexed:</span>
          <span>{formatDate(status?.lastIndexed ?? null)}</span>
        </div>
      </div>
    </aside>
  );
}
