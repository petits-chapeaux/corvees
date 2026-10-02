#!/usr/bin/env bash
set -euo pipefail

psql -X -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v operator_password="$CORVEES_OPERATOR_PASSWORD" -v app_password="$CORVEES_APP_PASSWORD" <<'SQL'
CREATE ROLE corvees_operator LOGIN PASSWORD :'operator_password';
CREATE ROLE corvees_app LOGIN PASSWORD :'app_password';
ALTER DATABASE corvees OWNER TO corvees_operator;
REVOKE ALL ON DATABASE corvees FROM PUBLIC;
GRANT CONNECT ON DATABASE corvees TO corvees_app;
ALTER SCHEMA public OWNER TO corvees_operator;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO corvees_app;
SQL
