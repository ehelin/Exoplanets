
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
    p.planet_name,
    s.name AS host_star,
    p.discovery_year,
    p.planet_mass,
    p.planet_radius,
    p.equilibrium_temp,
    p.classification,
    p.plavalova_code,
    cl.change_type,
    cl.field_name,
    cl.old_value,
    cl.new_value,
    cl.ai_classification,
    cl.ai_reasoning
FROM exoplanet.planets p
JOIN exoplanet.planet_stars ps ON ps.planet_id = p.id
JOIN exoplanet.stars s ON s.id = ps.star_id
JOIN exoplanet.change_log cl ON cl.planet_name = p.planet_name
WHERE p.classification IS NOT NULL
ORDER BY p.planet_name, cl.detected_at
LIMIT 30;