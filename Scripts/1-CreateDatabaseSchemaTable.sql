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

