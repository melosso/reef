# Reef 

An enterprise-ready data export service for Microsoft SQL Server. 

## Quick Start

### Prerequisites
- .NET 9.0 Runtime
- SQL Server / MySQL / PostgreSQL (for data sources)

### Setup
1. Install .NET Runetime:
```powershell
winget install Microsoft.DotNet.Runtime.9
```

2. Generate encryption key:
```powershell
$bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Environment]::SetEnvironmentVariable("REEF_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

3. Run the appplication and open browser: http://localhost:8085

```sql
Server=localhost;Database=AdventureWorks;User Id=sa;Password=123;Connection Timeout=15;TrustServerCertificate=true;
```

### Default Credentials
- Username: `admin`
- Password: `admin123` (change immediately)

## License
AGPL-3.0-or-later