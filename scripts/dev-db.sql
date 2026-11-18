-- Run once as a Postgres superuser: sudo -u postgres psql -f scripts/dev-db.sql
CREATE ROLE gtek LOGIN PASSWORD 'gtek_dev' CREATEDB;
CREATE DATABASE gtek_erp OWNER gtek;
