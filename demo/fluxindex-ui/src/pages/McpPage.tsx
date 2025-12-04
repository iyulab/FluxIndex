import { useState } from 'react';
import { Plug, Copy } from 'lucide-react';
import { useMcpSearch } from '../hooks/useApi';
import { useToast } from '../components/Toast';
import './McpPage.css';

export default function McpPage() {
  const [query, setQuery] = useState('');
  const [useReranker, setUseReranker] = useState(true);
  const [includeMetadata, setIncludeMetadata] = useState(true);
  const [topK, setTopK] = useState(5);
  const [maxTokens, setMaxTokens] = useState(5000);
  const mcpMutation = useMcpSearch();
  const { showToast } = useToast();

  const handleCall = () => {
    if (!query.trim()) {
      showToast('info', 'Please enter a query');
      return;
    }
    mcpMutation.mutate({
      query: query.trim(),
      topK,
      useReranker,
      includeMetadata,
      maxTokens,
    });
  };

  const handleCopy = async () => {
    if (!mcpMutation.data) return;
    try {
      await navigator.clipboard.writeText(JSON.stringify(mcpMutation.data, null, 2));
      showToast('success', 'Copied to clipboard');
    } catch {
      showToast('error', 'Failed to copy');
    }
  };

  const getOutputContent = (): string => {
    if (mcpMutation.isPending) {
      return '// Calling MCP function...';
    }
    if (mcpMutation.isError) {
      return `// Error: ${mcpMutation.error?.message || 'Unknown error'}`;
    }
    if (mcpMutation.data) {
      return JSON.stringify(mcpMutation.data, null, 2);
    }
    return `// MCP response will appear here
// This simulates the response format for AI assistant integration

{
  "toolName": "fluxindex_search",
  "query": "",
  "results": []
}`;
  };

  return (
    <div className="page">
      <div className="page-header">
        <h1>MCP Function Test</h1>
        <p>Test the Model Context Protocol (MCP) integration for AI assistants</p>
      </div>

      <div className="mcp-container">
        <div className="mcp-input-section">
          <div className="mcp-form">
            <label htmlFor="mcpQuery">Query</label>
            <textarea
              id="mcpQuery"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Enter your search query for MCP function test..."
            />
          </div>

          <div className="mcp-options">
            <div className="option-group">
              <label className="toggle-label">
                <input
                  type="checkbox"
                  checked={useReranker}
                  onChange={(e) => setUseReranker(e.target.checked)}
                />
                <span className="toggle-switch"></span>
                <span>Use Reranker</span>
              </label>
            </div>
            <div className="option-group">
              <label className="toggle-label">
                <input
                  type="checkbox"
                  checked={includeMetadata}
                  onChange={(e) => setIncludeMetadata(e.target.checked)}
                />
                <span className="toggle-switch"></span>
                <span>Include Metadata</span>
              </label>
            </div>
            <div className="option-group">
              <label>Top K:</label>
              <div className="select-wrapper">
                <select value={topK} onChange={(e) => setTopK(Number(e.target.value))}>
                  <option value={3}>3</option>
                  <option value={5}>5</option>
                  <option value={10}>10</option>
                </select>
              </div>
            </div>
            <div className="option-group">
              <label>Max Tokens:</label>
              <div className="select-wrapper">
                <select
                  value={maxTokens}
                  onChange={(e) => setMaxTokens(Number(e.target.value))}
                >
                  <option value={1000}>1,000</option>
                  <option value={2000}>2,000</option>
                  <option value={3000}>3,000</option>
                  <option value={5000}>5,000</option>
                  <option value={8000}>8,000</option>
                  <option value={10000}>10,000</option>
                  <option value={16000}>16,000</option>
                </select>
              </div>
            </div>
            <button
              className="btn btn-primary"
              onClick={handleCall}
              disabled={mcpMutation.isPending}
            >
              <Plug size={16} />
              Call MCP Function
            </button>
          </div>
        </div>

        <div className="mcp-output-section">
          <div className="mcp-output-header">
            <h3>Response</h3>
            <button
              className="btn-icon"
              onClick={handleCopy}
              disabled={!mcpMutation.data}
              title="Copy to clipboard"
            >
              <Copy size={16} />
            </button>
          </div>
          <div className="mcp-output">
            <pre>{getOutputContent()}</pre>
          </div>
        </div>
      </div>
    </div>
  );
}
