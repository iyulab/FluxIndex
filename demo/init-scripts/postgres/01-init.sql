-- FluxIndex PostgreSQL Initialization Script
-- Enables pgvector extension and creates required tables

-- Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Log successful initialization
DO $$
BEGIN
    RAISE NOTICE 'FluxIndex database initialized with pgvector extension';
END $$;
