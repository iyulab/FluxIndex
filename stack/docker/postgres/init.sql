-- FluxIndex PostgreSQL Initialization Script
-- This script runs automatically when the PostgreSQL container is first created

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gin;
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- Create optimized indexes for text search (will be created on tables after EF Core migration)
-- These are placeholder comments for reference

-- Performance settings verification
DO $$
BEGIN
    RAISE NOTICE 'FluxIndex PostgreSQL initialized successfully';
    RAISE NOTICE 'Extensions enabled: vector, pg_trgm, btree_gin, btree_gist';
END $$;
