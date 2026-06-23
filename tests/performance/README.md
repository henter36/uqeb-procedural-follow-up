# Post-Merge Operational Acceptance — k6 Performance Scenarios

These scripts require a running API, valid test credentials via environment variables, and must not be committed with secrets.

## Prerequisites

```bash
export UQEB_API_BASE_URL="http://localhost:5000/api"
export UQEB_TEST_USERNAME="set-in-env"
export UQEB_TEST_PASSWORD="set-in-env"
```

Install [k6](https://k6.io/docs/get-started/installation/).

## Scenarios

| Script | Purpose |
|--------|---------|
| `read-smoke.js` | Login, dashboard, transactions list, transaction detail |
| `write-smoke.js` | Login + lightweight mutation probe |

## Run

```bash
k6 run tests/performance/read-smoke.js
k6 run tests/performance/write-smoke.js
```

## Initial thresholds (adjust after baseline)

```text
GET p95 <= 1.5s
Mutation p95 <= 2.5s
Error rate < 1%
```

Record results in `docs/post_merge_operational_acceptance.md`.
