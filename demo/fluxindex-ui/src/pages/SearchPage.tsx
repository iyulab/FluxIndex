import { useState } from 'react';
import { Search } from 'lucide-react';
import { useSearch } from '../hooks/useApi';
import { useToast } from '../components/Toast';
import type { SearchResult } from '../types/api';
import './SearchPage.css';

function truncateText(text: string, maxLength: number): string {
  if (!text) return '';
  if (text.length <= maxLength) return text;
  return text.substring(0, maxLength) + '...';
}

function highlightQuery(text: string, query: string): string {
  if (!query || !text) return text;

  const escaped = text.replace(/[&<>"']/g, (char) => {
    const entities: Record<string, string> = {
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;',
    };
    return entities[char] || char;
  });

  const queryWords = query.toLowerCase().split(/\s+/).filter((w) => w.length > 2);
  let result = escaped;

  queryWords.forEach((word) => {
    const escapedWord = word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const regex = new RegExp(`(${escapedWord})`, 'gi');
    result = result.replace(regex, '<mark>$1</mark>');
  });

  return result;
}

export default function SearchPage() {
  const [query, setQuery] = useState('');
  const [useReranker, setUseReranker] = useState(true);
  const [topK, setTopK] = useState(10);
  const { showToast } = useToast();
  const searchMutation = useSearch();

  const handleSearch = () => {
    if (!query.trim()) {
      showToast('info', 'Please enter a search query');
      return;
    }
    searchMutation.mutate({ query: query.trim(), topK, useReranker });
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleSearch();
    }
  };

  const renderResult = (result: SearchResult, index: number) => {
    const content = truncateText(result.content, 400);
    const highlightedContent = highlightQuery(content, query);

    return (
      <div key={result.chunkId} className="result-item">
        <div className="result-header">
          <span className="result-rank">{index + 1}</span>
          <span className="result-source">{result.source || 'Unknown source'}</span>
          <div className="result-scores">
            <span className="score-badge vector">Vector: {result.score.toFixed(4)}</span>
            {result.wasReranked && result.rerankedScore !== undefined && (
              <span className="score-badge reranked">
                Reranked: {result.rerankedScore.toFixed(4)}
              </span>
            )}
          </div>
        </div>
        <div
          className="result-content"
          dangerouslySetInnerHTML={{ __html: highlightedContent }}
        />
        <div className="result-meta">
          {result.metadata?.quality != null && (
            <span>Quality: {String(result.metadata.quality as string | number)}</span>
          )}
          {result.metadata?.importance != null && (
            <span>Importance: {String(result.metadata.importance as string | number)}</span>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="page">
      <div className="page-header">
        <h1>Semantic Search</h1>
        <p>Search your indexed documents with AI-powered semantic understanding</p>
      </div>

      <div className="search-container">
        <div className="search-box">
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyPress={handleKeyPress}
            placeholder="Enter your search query..."
            autoComplete="off"
          />
          <button className="search-btn" onClick={handleSearch} disabled={searchMutation.isPending}>
            <Search size={18} />
            Search
          </button>
        </div>

        <div className="search-options">
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
            <label>Results:</label>
            <div className="select-wrapper">
              <select value={topK} onChange={(e) => setTopK(Number(e.target.value))}>
                <option value={5}>5</option>
                <option value={10}>10</option>
                <option value={20}>20</option>
                <option value={50}>50</option>
              </select>
            </div>
          </div>
        </div>
      </div>

      <div className="search-results">
        {searchMutation.isPending && (
          <div className="loading-overlay">
            <div className="spinner"></div>
          </div>
        )}

        {searchMutation.isError && (
          <div className="empty-state">
            <div className="empty-icon">Error</div>
            <h3>Search Failed</h3>
            <p>{searchMutation.error?.message || 'An error occurred'}</p>
          </div>
        )}

        {searchMutation.isSuccess && searchMutation.data.error && (
          <div className="empty-state">
            <div className="empty-icon">Error</div>
            <h3>Search Error</h3>
            <p>{searchMutation.data.error}</p>
          </div>
        )}

        {searchMutation.isSuccess && !searchMutation.data.error && (
          <>
            {searchMutation.data.results.length === 0 ? (
              <div className="empty-state">
                <div className="empty-icon">Search</div>
                <h3>No Results Found</h3>
                <p>Try adjusting your search query or index more documents</p>
              </div>
            ) : (
              <>
                <div className="search-stats">
                  <span>
                    Found <strong>{searchMutation.data.totalResults}</strong> results
                  </span>
                  <span>
                    Search time: <strong>{searchMutation.data.searchTimeMs}ms</strong>
                  </span>
                  <span>
                    Reranker: <strong>{searchMutation.data.usedReranker ? 'Yes' : 'No'}</strong>
                  </span>
                </div>
                {searchMutation.data.results.map((result, index) =>
                  renderResult(result, index)
                )}
              </>
            )}
          </>
        )}

        {!searchMutation.isPending && !searchMutation.isSuccess && !searchMutation.isError && (
          <div className="empty-state">
            <div className="empty-icon">Search</div>
            <h3>Ready to Search</h3>
            <p>Enter a query above to search your indexed documents</p>
          </div>
        )}
      </div>
    </div>
  );
}
