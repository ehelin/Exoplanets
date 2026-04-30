-- Create the vector database
CREATE DATABASE exoplanet_vectordb;

-- Connect to it, then enable pgvector
-- exoplanet_vectordb
CREATE EXTENSION IF NOT EXISTS vector;

-- The references table
CREATE TABLE exoplanet_reference (
    id              SERIAL PRIMARY KEY,
    planet_name     VARCHAR(255) NOT NULL,
    reference_name  TEXT NOT NULL,
    pub_date        VARCHAR(50),
    content         TEXT NOT NULL,
    embedding       VECTOR(384),
    created_utc     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index for fast similarity search
CREATE INDEX idx_reference_embedding 
    ON exoplanet_reference 
    USING hnsw (embedding vector_cosine_ops);

-- Index for planet name lookups
CREATE INDEX idx_reference_planet 
    ON exoplanet_reference (planet_name);

ALTER TABLE exoplanet_reference ALTER COLUMN embedding TYPE VECTOR(1536);