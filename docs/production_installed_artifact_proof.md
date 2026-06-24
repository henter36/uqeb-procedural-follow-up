# Windows Installed-Artifact Proof (D3)

This document records how the repository proves the offline installer promotes a real Windows artifact layout without touching production hosts.

## Proof runner

The proof runs on `windows-latest` GitHub Actions runners via:

- `scripts/tests/InstalledArtifactProof.Tests.ps1`
- Job `deployment-scripts` in `.github/workflows/deployment-package.yml`

## What is verified

1. `Install-StagedReleaseToProduction` copies staged payload into `releases/<version>/`.
2. Active files land under `current/api` and `current/web`.
3. `rollback-state.json` is written with `currentRelease` and paths.
4. On Windows, `publish/api` and `publish/web` junctions reference `current/`.
5. `install-production-package.ps1` end-to-end (with mocked SQL, health, and Playwright only) writes:
   - `releases/<version>/`
   - `rollback-state.json`
   - `publish/release-manifest.json` with `promotionModel = releases-current-v1`

## Local reproduction (Windows)

```powershell
Import-Module Pester -MinimumVersion 5.0.0
Invoke-Pester -Path .\scripts\tests\InstalledArtifactProof.Tests.ps1 -Output Detailed
```

## Production boundary

This proof does **not** deploy to `10.0.177.17` or mutate production databases. It validates installer behavior on ephemeral CI/local directories only.
