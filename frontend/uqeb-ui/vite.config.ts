# .NET
bin/
obj/
.vs/
*.user
*.suo

# Backend build/publish folders
backend/build-*/
backend/**/bin/
backend/**/obj/

# Frontend
node_modules/
dist/
build/
frontend/**/node_modules/
frontend/**/dist/

# Logs
*.log
logs/

# Temporary reports/files
tmp_report.*
*.tmp

# Local database / backups
*.db
*.sqlite
*.bak
*.mdf
*.ldf

# Secrets / local configuration
.env
.env.*
appsettings.Development.json
appsettings.Local.json
backend/Uqeb.Api/appsettings.json

# Keep examples
!backend/Uqeb.Api/appsettings.example.json

# Test outputs
TestResults/
coverage/

# OS
.DS_Store
Thumbs.db