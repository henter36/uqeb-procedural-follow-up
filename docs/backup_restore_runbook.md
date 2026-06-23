# Backup & Restore Runbook — Uqeb

---

## What to back up

| Asset | Location | Frequency |
|-------|----------|-----------|
| SQL Server database | `UqebDb` (per connection string) | Daily + before deploy |
| API publish folder | `C:\Uqeb\publish\api` | Before each deploy (script does this) |
| Web publish folder | `C:\Uqeb\publish\web` | Before each deploy |
| Production settings | `C:\Uqeb\publish\api\appsettings.Production.json` | Before each deploy |
| Attachments | `FileStorage:Path` (e.g. `C:\Uqeb\Attachments`) | Daily |
| Logs | `C:\Uqeb\logs` | Optional archive |

Deploy script creates `C:\Uqeb\backup\before-<timestamp>\` with api, web, and settings.

---

## Database backup (SQL Server)

```sql
BACKUP DATABASE [UqebDb]
TO DISK = N'C:\Uqeb\backup\db\UqebDb-full-YYYYMMDD-HHMM.bak'
WITH INIT, COMPRESSION, CHECKSUM, STATS = 10;
```

Verify:

```sql
RESTORE VERIFYONLY FROM DISK = N'C:\Uqeb\backup\db\UqebDb-full-YYYYMMDD-HHMM.bak';
```

---

## Database restore (acceptance / disaster recovery drill)

**Never restore over production without maintenance window and explicit approval.**

1. Stop API: `schtasks /End /TN "UqebApi"`
2. Restore to a **new** database name for drills:

```sql
RESTORE DATABASE [UqebDb_RestoreTest]
FROM DISK = N'C:\Uqeb\backup\db\UqebDb-full-YYYYMMDD-HHMM.bak'
WITH MOVE N'UqebDb' TO N'C:\SQLData\UqebDb_RestoreTest.mdf',
     MOVE N'UqebDb_log' TO N'C:\SQLData\UqebDb_RestoreTest_log.ldf',
     REPLACE, STATS = 10;
```

3. Point acceptance API connection string to `UqebDb_RestoreTest`.
4. Run health checks + smoke login.
5. Document row counts / sample transactions verified.

---

## File backup (attachments)

```powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmm"
robocopy C:\Uqeb\Attachments "C:\Uqeb\backup\attachments-$stamp" /E /R:2 /W:2
```

---

## Acceptance drill checklist

- [ ] Full backup taken
- [ ] New data added after backup
- [ ] Restore to separate DB successful
- [ ] API connects to restored DB
- [ ] Sample transaction readable
- [ ] Attachments intact (if included)
- [ ] Drill logged in `docs/post_merge_operational_acceptance.md`

**Status on current host:** **BLOCKED** — no SQL Server access.
