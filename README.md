# Fávoa

[![CI](https://github.com/FelipeKBorges/favoa/actions/workflows/ci.yml/badge.svg)](https://github.com/FelipeKBorges/favoa/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Blazor Server](https://img.shields.io/badge/Blazor-Server-512BD4)
![SQLite](https://img.shields.io/badge/SQLite-EF%20Core-003B57)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

🇧🇷 [Leia em português](README.pt-BR.md)

**A local-first life dashboard: finances, tasks, habits, workouts and diet in one place, running on my own PC.** Each area of life is a cell in a honeycomb that fills up as the day gets done. You log things by typing plain sentences (`uber 23`, `squat 5x5 100kg`, `drank 500 ml`), and a hand-written MCP server exposes the data for reading and updating.

Built for my own household: one process, one SQLite file, no cloud dependency, usable from the PC and from the phone.

<!-- Screenshots: add images to docs/img/ and uncomment
![Honeycomb dashboard](docs/img/colmeia.png)
![Quick capture](docs/img/captura.gif)
-->

## Highlights

| Area | What's in it |
|---|---|
| **Architecture** | Layered .NET 10 solution: `Core` (domain and pure rules), `Data` (EF Core services), `Web` (Blazor Server), `Mcp` (stdio server). Services split by area behind narrow interfaces |
| **Multi-tenancy** | Data isolated per household and per person with EF Core global query filters. Owners are filled in automatically on `SaveChanges`. Fails closed: with no logged-in user the context uses an id that matches nothing. Every new personal entity gets an isolation test |
| **Security** | E-mail and password login with PBKDF2-SHA256 (210k iterations), progressive lockout per account and per origin, HttpOnly + SameSite=Strict cookie revalidated on every request (changing the password signs out every device), open-redirect protection, invite codes stored only as hashes |
| **MCP server** | Model Context Protocol server written from scratch (JSON-RPC 2.0 over stdin/stdout, no SDK) with 13 tools. Short text replies to save context, and every write tool requires the user's approval |
| **Natural-language capture** | Rule-based sentence parser (offline, deterministic) for expenses, income, water, tasks, habits, strength and cardio sessions, meals and weight, with live preview and one-click undo |
| **Web Push** | Phone notifications with no third-party package: message encryption (RFC 8291, aes128gcm) and VAPID signing (RFC 8292) |
| **Data safety** | Versioned EF Core migrations (including adoption of pre-migration databases), daily consistent SQLite backups via the backup API, versioned JSON export/import validated before a transactional replace, CSV export ready for Excel in pt-BR, optional OneDrive mirror |
| **Domain care** | Money stored as `long` cents, `DateOnly` for business dates, UTC timestamps, accent-insensitive name matching, Brazilian TACO food table embedded for offline use |
| **Testing** | 220 xUnit tests running the real services on in-memory SQLite, not mocks |
| **Decisions** | Every architectural choice is recorded with its reason in [docs/DECISOES.md](docs/DECISOES.md) (ADR-style log) |

## Architecture

```
Browser (PC or phone via Tailscale, installable PWA)
   │  SignalR (Blazor Server)
   ▼
Favoa.Web ── Razor components, login, cookie auth
   │
   ▼
Favoa.Data ── EF Core services, migrations, backups, Web Push ──► SQLite (one local file)
   ▲
   │
Favoa.Mcp ── MCP server (stdio)

Favoa.Core ── entities, sentence parser, honeycomb calculation (no dependencies)
```

More detail in [docs/ARQUITETURA.md](docs/ARQUITETURA.md).

## Tech stack

C# · .NET 10 · ASP.NET Core · Blazor Server · Entity Framework Core · SQLite · xUnit · Model Context Protocol · Web Push / PWA · PowerShell (install and update scripts)

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/FelipeKBorges/favoa.git
cd favoa
dotnet test
dotnet run --project src\Favoa.Web
```

Open http://localhost:5080, create your household on first access, then press **Ctrl + K** and type what happened.

## Documentation

- [docs/MANUAL.md](docs/MANUAL.md): user guide (Portuguese): capture sentences, every screen, phone access, MCP setup, backups
- [docs/ARQUITETURA.md](docs/ARQUITETURA.md): architecture and data model
- [docs/DECISOES.md](docs/DECISOES.md): decision log
- [docs/MVP.md](docs/MVP.md): scope and roadmap

The domain and the UI are in Portuguese because the app was built for my own use in Brazil.

## License

[MIT](LICENSE). Bundled third-party data and fonts keep their own licenses: the TACO food table via taco-api (MIT), and the Bricolage Grotesque and Manrope fonts (SIL OFL).
