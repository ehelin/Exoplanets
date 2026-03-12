select * from exoplanet.change_log;
select * from exoplanet.change_report;
select * from exoplanet.pipeline_log;
select * from exoplanet.ingest_run;
select * from exoplanet.exoplanets;

TRUNCATE exoplanet.ingest_run CASCADE;
TRUNCATE exoplanet.exoplanets;