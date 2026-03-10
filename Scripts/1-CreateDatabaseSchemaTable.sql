CREATE DATABASE exoplanetdb
    WITH
    OWNER = postgres
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.utf8'
    LC_CTYPE = 'en_US.utf8'
    TEMPLATE = template0;

-- Schema is optional; keep if you like organization.
CREATE SCHEMA IF NOT EXISTS exoplanet;

CREATE TABLE IF NOT EXISTS exoplanet.exoplanets
(
    exoplanet_id   BIGSERIAL PRIMARY KEY,

    planet_name    TEXT NOT NULL,
    host_star      TEXT NOT NULL,
    discovery_year INTEGER NULL,

    created_utc     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_utc     TIMESTAMPTZ NOT NULL DEFAULT now(),

    -- Natural key for dedupe (what "new" means).
    CONSTRAINT uq_exoplanets_planet_host UNIQUE (planet_name, host_star)
);

SET search_path TO exoplanet;

CREATE TABLE pipeline_log (
    id              BIGSERIAL PRIMARY KEY,
    ingest_run_id   INT REFERENCES ingest_run(id),
    log_level       VARCHAR(20) NOT NULL,       -- INFO, WARNING, ERROR
    message         TEXT NOT NULL,
    exception       TEXT,
    logged_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_pipeline_log_run ON pipeline_log (ingest_run_id);
CREATE INDEX idx_pipeline_log_time ON pipeline_log (logged_at DESC);
CREATE INDEX idx_pipeline_log_level ON pipeline_log (log_level);