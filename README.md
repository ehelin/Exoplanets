# ExoPlanets – Daily Exoplanet Ingestion (Azure Functions + PostgreSQL)

This repository contains an Azure Functions–based ingestion service that periodically fetches exoplanet data from a public source and persists it to a PostgreSQL database.

The system is intentionally small, observable, and production-shaped: one scheduled job, explicit configuration, deterministic deployment, and clear upgrade paths.

---

## High-level Architecture

- **Azure Functions (.NET 8, isolated worker)**
- **TimerTrigger** for scheduled daily execution
- **PostgreSQL** (local for development, Azure PostgreSQL in production)
- **Consumption plan** (scale-to-zero, pay-per-use)
- **Visual Studio publish profile**–based deployment

The function runs once per day, performs an idempotent ingestion, and logs results for observability.

---

## Project Structure

