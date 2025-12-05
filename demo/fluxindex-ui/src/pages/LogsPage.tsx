import { useState, useMemo, useEffect, useRef } from 'react';
import { RefreshCw, Trash2, Filter, AlertCircle, CheckCircle, Info, AlertTriangle, Clock } from 'lucide-react';
import { useLogs, useClearLogs } from '../hooks/useApi';
import { useToast } from '../components/Toast';
import type { LogEntry } from '../types/api';
import './LogsPage.css';

function formatTimestamp(dateStr: string): string {
  try {
    const date = new Date(dateStr);
    return date.toLocaleTimeString('ko-KR', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  } catch {
    return dateStr;
  }
}

function formatDate(dateStr: string): string {
  try {
    const date = new Date(dateStr);
    return date.toLocaleDateString('ko-KR', {
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return '';
  }
}

const levelIcons: Record<string, React.ReactNode> = {
  info: <Info size={14} />,
  success: <CheckCircle size={14} />,
  warning: <AlertTriangle size={14} />,
  error: <AlertCircle size={14} />,
};

const levelColors: Record<string, string> = {
  info: 'level-info',
  success: 'level-success',
  warning: 'level-warning',
  error: 'level-error',
};

export default function LogsPage() {
  const [categoryFilter, setCategoryFilter] = useState<string>('');
  const [levelFilter, setLevelFilter] = useState<string>('');
  const [autoScroll, setAutoScroll] = useState(true);
  const logsContainerRef = useRef<HTMLDivElement>(null);

  const { data: logs, isLoading, refetch } = useLogs({
    limit: 200,
    category: categoryFilter || undefined,
    level: levelFilter || undefined,
    refetchInterval: 2000,
  });
  const clearLogsMutation = useClearLogs();
  const { showToast } = useToast();

  // Auto-scroll to bottom when new logs arrive
  useEffect(() => {
    if (autoScroll && logsContainerRef.current && logs) {
      logsContainerRef.current.scrollTop = logsContainerRef.current.scrollHeight;
    }
  }, [logs, autoScroll]);

  // Extract unique categories for filter dropdown
  const categories = useMemo(() => {
    if (!logs) return [];
    const uniqueCategories = [...new Set(logs.map(log => log.category))];
    return uniqueCategories.sort();
  }, [logs]);

  // Group logs by date for display
  const groupedLogs = useMemo(() => {
    if (!logs) return [];

    const groups: { date: string; logs: LogEntry[] }[] = [];
    let currentDate = '';

    // Logs are already in reverse chronological order, so we need to reverse for display
    const sortedLogs = [...logs].reverse();

    for (const log of sortedLogs) {
      const date = formatDate(log.timestamp);
      if (date !== currentDate) {
        groups.push({ date, logs: [] });
        currentDate = date;
      }
      groups[groups.length - 1].logs.push(log);
    }

    return groups;
  }, [logs]);

  const handleClearLogs = async () => {
    if (!logs || logs.length === 0) return;
    if (!confirm('Clear all process logs?')) return;

    try {
      await clearLogsMutation.mutateAsync();
      showToast('success', 'Logs cleared');
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Failed to clear logs';
      showToast('error', errorMessage);
    }
  };

  return (
    <div className="page">
      <div className="page-header">
        <div className="page-header-text">
          <h1>Process Logs</h1>
          <p>Real-time activity monitoring for uploads and QA generation</p>
        </div>
        <div className="page-header-actions">
          <label className="auto-scroll-toggle">
            <input
              type="checkbox"
              checked={autoScroll}
              onChange={(e) => setAutoScroll(e.target.checked)}
            />
            <span>Auto-scroll</span>
          </label>
          <button className="btn btn-secondary" onClick={() => refetch()}>
            <RefreshCw size={16} />
            Refresh
          </button>
        </div>
      </div>

      <div className="logs-container">
        <div className="logs-toolbar">
          <div className="filter-group">
            <Filter size={16} />
            <select
              value={categoryFilter}
              onChange={(e) => setCategoryFilter(e.target.value)}
              className="filter-select"
            >
              <option value="">All Categories</option>
              {categories.map(cat => (
                <option key={cat} value={cat}>{cat}</option>
              ))}
            </select>
            <select
              value={levelFilter}
              onChange={(e) => setLevelFilter(e.target.value)}
              className="filter-select"
            >
              <option value="">All Levels</option>
              <option value="info">Info</option>
              <option value="success">Success</option>
              <option value="warning">Warning</option>
              <option value="error">Error</option>
            </select>
          </div>
          <div className="logs-stats">
            <span className="log-count">
              <Clock size={14} />
              {logs?.length || 0} entries
            </span>
            <button
              className="btn btn-danger btn-sm"
              onClick={handleClearLogs}
              disabled={!logs?.length || clearLogsMutation.isPending}
            >
              <Trash2 size={14} />
              Clear Logs
            </button>
          </div>
        </div>

        <div className="logs-list" ref={logsContainerRef}>
          {isLoading && (
            <div className="loading-overlay">
              <div className="spinner"></div>
            </div>
          )}

          {!isLoading && (!logs || logs.length === 0) && (
            <div className="empty-state">
              <div className="empty-icon">
                <Clock size={48} />
              </div>
              <h3>No Logs</h3>
              <p>Process logs will appear here when you upload documents or generate Q&A pairs</p>
            </div>
          )}

          {groupedLogs.map((group, groupIndex) => (
            <div key={groupIndex} className="log-group">
              <div className="log-date-header">{group.date}</div>
              {group.logs.map((log, logIndex) => (
                <div key={`${groupIndex}-${logIndex}`} className={`log-entry ${levelColors[log.level] || ''}`}>
                  <div className="log-time">{formatTimestamp(log.timestamp)}</div>
                  <div className={`log-level ${levelColors[log.level] || ''}`}>
                    {levelIcons[log.level] || <Info size={14} />}
                    <span>{log.level.toUpperCase()}</span>
                  </div>
                  <div className="log-category">{log.category}</div>
                  <div className="log-message">
                    {log.message}
                    {log.details && (
                      <span className="log-details" title={log.details}>
                        - {log.details}
                      </span>
                    )}
                  </div>
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
