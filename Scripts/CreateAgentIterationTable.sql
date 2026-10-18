-- Post 1 (Real Thinking Agents): per-iteration log for every agent's thinking loop.
-- Run against exoplanetdb before Post 1 code is exercised.
CREATE TABLE IF NOT EXISTS exoplanet.agent_iteration (
    id              SERIAL PRIMARY KEY,
    ingest_run_id   INTEGER NOT NULL,
    agent_name      VARCHAR(50) NOT NULL,
    iteration_number INTEGER NOT NULL,
    approach        TEXT NOT NULL,
    result_summary  TEXT NOT NULL,
    check_result    TEXT NOT NULL,
    iterated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_agent_iteration_run_agent
    ON exoplanet.agent_iteration (ingest_run_id, agent_name);
