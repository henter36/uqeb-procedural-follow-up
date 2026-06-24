#!/usr/bin/env bash
set -euo pipefail

if [ -z "${API_BASE_URL:-}" ]; then
  echo "API_BASE_URL is required for reporting production smoke test." >&2
  exit 1
fi

USERNAME="${USERNAME:-admin}"
PASSWORD="${PASSWORD:-}"

echo "Reporting production smoke test starting against ${API_BASE_URL}"

login_payload=$(USERNAME="$USERNAME" PASSWORD="$PASSWORD" python3 -c 'import json, os; print(json.dumps({"username": os.environ["USERNAME"], "password": os.environ["PASSWORD"]}))')
token=$(curl -fsS -H "Content-Type: application/json" -d "$login_payload" "$API_BASE_URL/auth/login" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')

curl -fsS -H "Authorization: Bearer $token" "$API_BASE_URL/institutional-reports/configuration" >/dev/null
curl -fsS -H "Authorization: Bearer $token" "$API_BASE_URL/institutional-reports/readiness" >/dev/null

preview_payload='{"reportType":1,"sectionIds":[1,2],"filters":{"dateFrom":"2025-01-01","dateTo":"2025-12-31"}}'
curl -fsS -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d "$preview_payload" "$API_BASE_URL/institutional-reports/preview" >/dev/null

echo "Smoke test passed."
