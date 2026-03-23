
select * from exoplanet.pipeline_log;
select * from exoplanet.ingest_run;
select * from exoplanet.change_log;
select * from exoplanet.exoplanets;
select * from exoplanet.change_report;

TRUNCATE exoplanet.eval_result, 
exoplanet.pipeline_log, 
exoplanet.change_report, 
exoplanet.change_log, 
exoplanet.atmospheres, 
exoplanet.ingest_run, 
exoplanet.planet_stars, 
exoplanet.planets, 
exoplanet.stars, 
exoplanet.solar_systems CASCADE;

SELECT 
    e.planet_name,
    e.host_star,
    e.discovery_year,
    e.classification,
    cl.change_type,
    cl.field_name,
    cl.old_value,
    cl.new_value,
    cl.ai_classification,
    cl.ai_reasoning
FROM exoplanet.exoplanets e
JOIN exoplanet.change_log cl ON cl.planet_name = e.planet_name
WHERE e.classification IS NOT NULL
ORDER BY e.planet_name, cl.detected_at
LIMIT 30;