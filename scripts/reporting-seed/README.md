# Reporting synthetic seed

This seed generator creates **synthetic-only** transaction data for institutional reporting acceptance runs.

## Safety

- Not executed automatically in production or unit tests.
- Requires explicit environment variable: `RUN_REPORTING_ACCEPTANCE=1`.
- Do not point at production databases.

## Sizes

- 1,000
- 5,000
- 10,000
- 20,000
- 50,000

## Usage

```powershell
$env:RUN_REPORTING_ACCEPTANCE = "1"
dotnet test backend/Uqeb.Api.Tests/Uqeb.Api.Tests.csproj `
  --filter "FullyQualifiedName~ReportingSyntheticSeedGeneratorTests"
```

The generator is implemented in `backend/Uqeb.Api.Tests/Reporting/Performance/ReportingSyntheticSeedGeneratorTests.cs` and uses in-memory fixtures only.
