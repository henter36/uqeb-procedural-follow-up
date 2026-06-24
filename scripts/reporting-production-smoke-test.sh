#!/usr/bin/env bash
set -euo pipefail

API_BASE_URL="${API_BASE_URL:-http://localhost:5000/api}"
USERNAME="${USERNAME:-admin}"
PASSWORD="${PASSWORD:-}"

echo "Reporting production smoke test starting..."

login_payload=$(printf '{"username":"%s","password":"%s"}' "$USERNAME" "$PASSWORD")
token=$(curl -fsS -H "Content-Type: application/json" -d "$login_payload" "$API_BASE_URL/auth/login" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')

curl -fsS -H "Authorization: Bearer $token" "$API_BASE_URL/institutional-reports/configuration" >/dev/null
curl -fsS -H "Authorization: Bearer $token" "$API_BASE_URL/institutional-reports/readiness" >/dev/null

preview_payload='{"reportType":1,"sectionIds":[1,2],"filters":{"dateFrom":"2025-01-01","dateTo":"2025-12-31"}}'
curl -fsS -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d "$preview_payload" "$API_BASE_URL/institutional-reports/preview" >/dev/null

echo "Smoke test passed."
