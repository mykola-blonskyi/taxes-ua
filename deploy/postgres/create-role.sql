-- Creates this project's role and database in an existing PostgreSQL instance (ADR-006).
-- Run as a superuser, connected to any database other than the target one. Safe to run again.
--
--   psql -v role=taxes_ua -v db=taxes_ua -f create-role.sql
--
-- The role is created without a password. Set it afterwards with `\password taxes_ua`, which
-- sends only a SCRAM hash, so the secret never reaches shell history, `ps` or the server log.

\set ON_ERROR_STOP on

\if :{?role}
\else
  \set role taxes_ua
\endif
\if :{?db}
\else
  \set db taxes_ua
\endif

SELECT format('CREATE ROLE %I LOGIN', :'role')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'role')
\gexec

SELECT format(
    'ALTER ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 20',
    :'role')
\gexec

-- Owned by the superuser running this, not by the role, so the role cannot drop the database or
-- create schemas in it.
SELECT format('CREATE DATABASE %I', :'db')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'db')
\gexec

SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'db')
\gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'db', :'role')
\gexec

\connect :"db"

-- PostgreSQL 15 already withholds CREATE on public from PUBLIC; this makes older instances match.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
SELECT format('GRANT USAGE, CREATE ON SCHEMA public TO %I', :'role')
\gexec

-- Every other database the role can still enter. PUBLIC holds CONNECT on a new database by
-- default and PostgreSQL has no per-role deny, so an empty result needs either PUBLIC's CONNECT
-- revoked there or the pg_hba.conf lines in docs/deploy.md.
SELECT datname AS "other database the role can connect to"
FROM pg_database
WHERE datallowconn
  AND datname <> :'db'
  AND has_database_privilege(:'role', datname, 'CONNECT')
ORDER BY datname;
