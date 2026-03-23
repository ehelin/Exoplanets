-- ============================================================
-- Exoplanet Discovery Monitor - Phase 3 Schema
-- Schema: exoplanet
-- ============================================================

-- ------------------------------------------------------------
-- DROP (order matters - children before parents)
-- ------------------------------------------------------------
DROP TABLE IF EXISTS exoplanet.eval_result;
DROP TABLE IF EXISTS exoplanet.pipeline_log;
DROP TABLE IF EXISTS exoplanet.change_report;
DROP TABLE IF EXISTS exoplanet.atmospheres;
DROP TABLE IF EXISTS exoplanet.change_log;
DROP TABLE IF EXISTS exoplanet.ingest_run;
DROP TABLE IF EXISTS exoplanet.planet_stars;
DROP TABLE IF EXISTS exoplanet.planets;
DROP TABLE IF EXISTS exoplanet.stars;
DROP TABLE IF EXISTS exoplanet.solar_systems;

-- ------------------------------------------------------------
-- CREATE SCHEMA (if not exists)
-- ------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS exoplanet;

-- ------------------------------------------------------------
-- solar_systems
-- ------------------------------------------------------------
CREATE TABLE exoplanet.solar_systems (
    id                  SERIAL PRIMARY KEY,
    distance_parsecs    NUMERIC(12, 6),
    num_stars           INT,
    num_planets         INT,
    created_utc         TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_utc         TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ------------------------------------------------------------
-- stars
-- ------------------------------------------------------------
CREATE TABLE exoplanet.stars (
    id                  SERIAL PRIMARY KEY,
    solar_system_id     INT NOT NULL REFERENCES exoplanet.solar_systems(id),
    name                VARCHAR(255) NOT NULL,
    temperature_k       NUMERIC(12, 4),
    radius_solar        NUMERIC(12, 6),
    mass_solar          NUMERIC(12, 6),
    spectral_type       VARCHAR(50),
    created_utc         TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_utc         TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ------------------------------------------------------------
-- planets
-- ------------------------------------------------------------
CREATE TABLE exoplanet.planets (
    id                      SERIAL PRIMARY KEY,
    solar_system_id         INT NOT NULL REFERENCES exoplanet.solar_systems(id),
    planet_name             VARCHAR(255) NOT NULL UNIQUE,
    discovery_year          INT,
    discovery_method        VARCHAR(100),
    planet_radius           NUMERIC(12, 6),         -- Earth radii
    planet_mass             NUMERIC(12, 6),         -- Earth masses
    orbital_period          NUMERIC(14, 6),         -- days
    semi_major_axis         NUMERIC(12, 6),         -- AU
    eccentricity            NUMERIC(10, 6),
    equilibrium_temp        NUMERIC(12, 4),         -- Kelvin
    planet_density          NUMERIC(12, 6),
    insolation_flux         NUMERIC(14, 6),
    classification          VARCHAR(255),
    plavalova_code          VARCHAR(50),
    habitability_score      VARCHAR(50),
    created_utc             TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_utc             TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ------------------------------------------------------------
-- planet_stars  (junction: planets <-> stars)
-- ------------------------------------------------------------
CREATE TABLE exoplanet.planet_stars (
    id          SERIAL PRIMARY KEY,
    planet_id   INT NOT NULL REFERENCES exoplanet.planets(id),
    star_id     INT NOT NULL REFERENCES exoplanet.stars(id)
);

-- ------------------------------------------------------------
-- ingest_run
-- One row per pipeline execution - written before anything else
-- ------------------------------------------------------------
CREATE TABLE exoplanet.ingest_run (
    id              SERIAL PRIMARY KEY,
    run_timestamp   TIMESTAMP NOT NULL DEFAULT NOW(),
    source          VARCHAR(255) NOT NULL,
    status          VARCHAR(50) NOT NULL DEFAULT 'running',
    rows_fetched    INT NOT NULL DEFAULT 0,
    rows_new        INT NOT NULL DEFAULT 0,
    rows_updated    INT NOT NULL DEFAULT 0,
    rows_deleted    INT NOT NULL DEFAULT 0,
    completed_at    TIMESTAMP
);

-- ------------------------------------------------------------
-- change_log
-- Field-level diffs written before AI is invoked
-- AI classification writes a NEW row (change_type = 'AI_CLASSIFICATION')
-- never modifies the original INSERT/UPDATE row
-- ------------------------------------------------------------
CREATE TABLE exoplanet.change_log (
    id                  BIGSERIAL PRIMARY KEY,
    ingest_run_id       INT NOT NULL REFERENCES exoplanet.ingest_run(id),
    planet_name         VARCHAR(255) NOT NULL,
    change_type         VARCHAR(50) NOT NULL,       -- INSERT, UPDATE, AI_CLASSIFICATION
    field_name          VARCHAR(100),
    old_value           TEXT,
    new_value           TEXT,
    ai_classification   VARCHAR(255),
    ai_reasoning        TEXT,
    detected_at         TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ------------------------------------------------------------
-- atmospheres
-- Atmospheric evidence per planet per ingest run
-- ------------------------------------------------------------
CREATE TABLE exoplanet.atmospheres (
    id                      SERIAL PRIMARY KEY,
    planet_id               INT NOT NULL REFERENCES exoplanet.planets(id),
    ingest_run_id           INT NOT NULL REFERENCES exoplanet.ingest_run(id),
    molecule                VARCHAR(100),
    detection_type          VARCHAR(100),
    spectral_reference      VARCHAR(255),
    habitability_score      NUMERIC(5, 2),
    habitability_reasoning  TEXT,
    created_utc             TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_utc             TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ------------------------------------------------------------
-- change_report
-- AI-generated narrative from stored diffs only
-- ------------------------------------------------------------
CREATE TABLE exoplanet.change_report (
    id              SERIAL PRIMARY KEY,
    ingest_run_id   INT NOT NULL REFERENCES exoplanet.ingest_run(id),
    model_used      VARCHAR(100),
    prompt_sent     TEXT,
    report_text     TEXT NOT NULL,
    tokens_used     INT,
    generated_at    TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ------------------------------------------------------------
-- pipeline_log
-- All logging goes here - replaces ILogger
-- ingest_run_id nullable: some logs fire before run exists
-- ------------------------------------------------------------
CREATE TABLE exoplanet.pipeline_log (
    id              BIGSERIAL PRIMARY KEY,
    ingest_run_id   INT REFERENCES exoplanet.ingest_run(id),
    log_level       VARCHAR(20) NOT NULL,            -- INFO, WARN, ERROR
    message         TEXT NOT NULL,
    exception       TEXT,
    logged_at       TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ------------------------------------------------------------
-- eval_result
-- How did the AI do?
-- eval_type: CLASSIFICATION or LIFE_ASSESSMENT
-- ------------------------------------------------------------
CREATE TABLE exoplanet.eval_result (
    id              SERIAL PRIMARY KEY,
    ingest_run_id   INT NOT NULL REFERENCES exoplanet.ingest_run(id),
    eval_type       VARCHAR(50) NOT NULL,
    planet_name     VARCHAR(255) NOT NULL,
    expected_value  TEXT,
    actual_value    TEXT,
    score           INT,                             -- 0-100
    dimension       VARCHAR(100),                    -- accuracy, completeness, consistency
    pass_fail       VARCHAR(10),                     -- PASS, FAIL
    evaluated_at    TIMESTAMP NOT NULL DEFAULT NOW()
);

ALTER TABLE exoplanet.ingest_run ADD COLUMN source_url TEXT;
ALTER TABLE exoplanet.ingest_run ADD COLUMN rows_unchanged INT NOT NULL DEFAULT 0;
ALTER TABLE exoplanet.ingest_run ADD COLUMN error_message TEXT;