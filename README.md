# Reef

[![License](https://img.shields.io/badge/license-AGPL%203.0-blue)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/reef/trignis)](https://github.com/melosso/reef/commits/main)
[![Latest Release](https://img.shields.io/github/v/release/reef/trignis)](https://github.com/melosso/reef/releases/latest)

**Reef** is a web-based platform that orchestrates your data workflows. It lets you run queries, transform results, and deliver them to multiple destinations, all from a single easy-to-use interface. Manage connections, profiles, and scheduled executions centrally. All low code, no scripts unless you need them.

![Screenshot of Reef](https://github.com/melosso/reef/blob/main/.github/images/screenshot.png?raw=true)

## 🌟 What is Reef?

Reef automates data exports for **reporting workflows**, **integration pipelines**, and **data synchronization** with customizable data structures. Keep it low-code, or set-up your own custom templates.

**Some of the key capabilities:**

- **Web-based interface**: Manage everything through the browser
- **Connection management**: Store and reuse database connections across profiles
- **Multiple formats**: JSON, XML, CSV, YAML with optional [Scriban](https://github.com/scriban/scriban) templates
- **Flexible destinations**: Local, FTP/SFTP, AWS S3, Azure Blob, HTTP, SMB
- **Job scheduling**: Cron expressions, intervals, or webhook triggers
- **Execution history**: Track all runs with detailed logging
- **Security first**: JWT auth, encrypted credentials, hash validation

In other words, Reef can assist you in quickly getting data synchronisation going. Use your native database languages, or extend your results with (optional) advanced templating.

---

## 🚀 Quick Start

We've prepared two methods to deploy Reef. It's up to you to choose your preferred method:

### Docker Compose (Recommended)
```yaml
services:
  reef:
    image: ghcr.io/melosso/reef:latest
    ports:
      - "8085:8085"
    volumes:
      - reef_data:/app
      - ./exports:/app/exports
    environment:
      - REEF_ENCRYPTION_KEY=YourKeyHere

volumes:
  reef_data:
```
```bash
docker compose up -d
```

Access at **http://localhost:8085**

Upon starting the first time, you can login with the default credentials `admin` / `admin123`. It's advisable to change these credentials immediately.

### Windows Installation

Download the latest release from Releases.

1. **Set encryption key:**
```powershell
   $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Environment]::SetEnvironmentVariable("REEF_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

2. **Install service:**
```powershell
   .\Reef.bat install
   .\Reef.bat start
```

3. Open browser → **http://localhost:8085**

As mentioned hereabove, upon starting the first time, you can login with the default credentials `admin` / `admin123`. You should change these immediately.

---

## 🧩 Core Features

You can centralize your database credentials easily; create one connection for many profiles. You can tag these profiles by assigning a `Group` to them. Then, create an export definition by creating a `Profile` that'll be assigned a destination:

#### Multiple Destinations
- **Local filesystem**: with date/profile variables
- **FTP/SFTP**: with SSL and passive mode
- **Cloud storage**: AWS S3, Azure Blob
- **HTTP endpoints**: POST to REST APIs
- **SMB shares**: Windows network drives

#### Custom Templates
Use Scriban for advanced transformations:
```scriban
{
  "export_date": "{{ date.now }}",
  "records": [
    {{~ for row in rows ~}}
    { "id": {{ row.ID }}, "name": "{{ row.Name }}" }
    {{~ end ~}}
  ]
}
```

#### Job Scheduling

After creating a job, you can schedule it using various methods such as:

- **Cron:** `0 2 * * *` (daily at 2 AM)
- **Interval:** Every 15 minutes
- **Webhooks:** Trigger via HTTP POST

## 🔐 Security

We've made sure to keep your data safe. Here's a glimpse on how we do this:

- **JWT authentication**: Token-based API access
- **Encrypted credentials**: Stored using sRSA+AES hybrid encryption
- **Transient execution**: Data exists only in memory while being processed, nothing persists.
- **Hash validation**: Detect configuration tampering
- **Audit logging**: Every change is tracked for accountability

> [!IMPORTANT] 
> It's a bad practice to expose Reef to the internet. Make sure to **never expose** Reef outside of your network. If you do, solely do this for the `/api` routes using Nginx or an alternative. We've added an example configuration available for IIS [here](web.config.md), but make sure to make changes that align with your requirements.

## 📊 Monitoring

To keep the application status traceable, we've included:

- Extensive execution history details, showing status status/duration/row counts
- Application logging available both in the UI and the `/logs` folder
- Health endpoint available on `GET /health` for Azure Monitor, Prometheus, Grafana

## 🤝 Credits

Built with [ASP.NET Core](https://github.com/dotnet/aspnetcore), [Dapper](https://github.com/DapperLib/Dapper), [Scriban](https://github.com/scriban/scriban), [Serilog](https://github.com/serilog/serilog), and more.

## 🔮 Lore

> Reef comes from the idea of a coral reef, something that grows slowly from countless small bits until it becomes its own world. Database exports feel the same way. Each customer, each dump, each migration, another layer added to the structure. It's alive in its own way, even if it's just data.

## License

Free for open source projects and personal use under the **AGPL 3.0** license. For more information, please see the [license](LICENSE) file.

## Contributing

Contributions welcome! Please submit issues and pull requests, using the templates we provided.
