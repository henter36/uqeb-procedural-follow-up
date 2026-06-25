# Performance baseline (P1)

This directory defines **baseline contracts and templates only**. It does not change production API behavior.

## Layout

| Path | Purpose |
| --- | --- |
| `schema.json` | JSON schema for baseline records |
| `records/*.template.json` | Unmeasured templates to copy after a run |
| `../../tests/performance/baseline/` | k6 scenarios with fixed baseline thresholds |
| `../../artifacts/performance-baseline/` | Git-ignored measured outputs (local/CI) |

## Record a baseline (API read smoke)

```bash
export UQEB_API_BASE_URL="http://localhost:5000/api"
export UQEB_TEST_USERNAME="set-in-env"
export UQEB_TEST_PASSWORD="set-in-env"

k6 run tests/performance/baseline/read-smoke-baseline.js
```

Copy the summary metrics into `artifacts/performance-baseline/` using `records/baseline-records.template.json` as the shape guide.

## Record a baseline (reporting export)

```bash
export RUN_REPORTING_ACCEPTANCE=1
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj \
  --filter "FullyQualifiedName~ReportingAcceptanceBenchmarkTests.SmallBenchmark"
```

Use `artifacts/reporting-acceptance/reporting-benchmark-results.json` as the measured source.

## Record a baseline (analysis pipeline stages)

```bash
export RUN_REPORTING_ANALYSIS_BENCHMARK=1
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj \
  --filter "FullyQualifiedName~InstitutionalReportAnalysisPipelineBenchmarkTests"
```

Stage timings are written to `artifacts/performance-baseline/analysis-pipeline-<snapshotCount>.json`.

## Comparison policy (later phases)

Future phases compare new runs against these records. Do not edit templates in `records/` with live numbers; store measurements under `artifacts/performance-baseline/`.
