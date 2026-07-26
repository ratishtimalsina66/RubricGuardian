# RubricGuardian

An AI-powered assignment compliance agent. Upload your assignment instructions, the grading
rubric, and your completed work — RubricGuardian extracts every requirement, checks your
submission against each one, and gives you a prioritized fix checklist before you submit.

## How it works

```
┌──────────────────────┐        ┌──────────────────────┐        ┌──────────────┐
│  ASP.NET Core MVC    │  HTTP  │  Python FastAPI       │  HTTP  │  OpenAI-      │
│  (RubricGuardian.Web)│ ─────► │  AI service           │ ─────► │  compatible   │
│  UI, auth, workflow  │        │  text extraction +    │        │  LLM API      │
│                      │        │  extraction/eval LLM  │        └──────────────┘
└──────┬───────┬───────┘        └──────────────────────┘
       │       │
       ▼       ▼
 SQL Server   Local file storage (App_Data/uploads)
              (IFileStorageService — swap in Azure Blob later)
```

**Workflow:** upload instructions/rubric → text is extracted → the LLM converts the text
into structured requirements (never invented — everything traces to the document) →
upload your submission → each requirement is checked for evidence → each gets a status
(Complete / Partial / Missing / Unclear), confidence score, risk level, feedback, and a
fix suggestion → the readiness dashboard shows the results, highest-risk items first.
Revised uploads create new versions so you can track progress.

## Project structure

```
RubricGuardian/
├── RubricGuardian.Web/          ASP.NET Core 8 MVC app
│   ├── Controllers/             Account, Dashboard, Courses, Assignments
│   ├── Data/                    AppDbContext (EF Core) + DbSeeder
│   ├── Models/                  Entities + ViewModels
│   ├── Services/                Password hashing, file storage, AI client, workflow
│   ├── Views/                   Razor pages (Bootstrap 5, custom academic theme)
│   └── wwwroot/css/site.css     Design system
├── ai-service/                  Python FastAPI service
│   ├── main.py                  /extract-text, /extract-requirements, /evaluate
│   ├── extraction.py            PDF / DOCX / TXT / MD text extraction
│   ├── llm.py                   OpenAI-compatible client + prompts
│   └── schemas.py               Pydantic models
├── database/
│   ├── schema.sql               Manual SQL Server schema (optional)
│   └── seed.sql                 Manual seed data (optional)
└── docs/prompts.md              Prompt design notes
```

## Prerequisites

- .NET 8 SDK
- Python 3.11+
- SQL Server (LocalDB, full SQL Server, or Docker — see below)
- An API key for any OpenAI-compatible API (or a local model via Ollama/LM Studio)

## Setup

### 1. SQL Server

Easiest option on Mac/Linux — Docker:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name rubricguardian-sql -d mcr.microsoft.com/mssql/server:2022-latest
```

The default connection string in `RubricGuardian.Web/appsettings.json` matches this container.
On Windows with LocalDB, change it to:

```
Server=(localdb)\\MSSQLLocalDB;Database=RubricGuardian;Trusted_Connection=True;
```

The app creates the schema and seeds demo data automatically on first run.
(`database/schema.sql` and `seed.sql` exist if you prefer to do it manually — then set
`"Database:SeedOnStartup": false`.)

### 2. AI service

```bash
cd ai-service
python -m venv .venv
source .venv/bin/activate          # Windows: .venv\Scripts\activate
pip install -r requirements.txt
cp .env.example .env               # add your API key / base URL / model
uvicorn main:app --port 8000
```

`.env` is loaded automatically on startup (via `python-dotenv`) — no manual `export` needed.
If `OPENAI_API_KEY` or `AI_SERVICE_API_KEY` is missing, the service still starts but logs a
warning; the first call to `/extract-requirements` or `/evaluate` will fail with a clear
error until a valid key is set.

Verify: http://localhost:8000/health → `{"status":"ok"}`
Interactive API docs: http://localhost:8000/docs

**Using a local model instead of OpenAI:** run Ollama (`ollama run llama3.1`), then set
`OPENAI_BASE_URL=http://localhost:11434/v1`, `OPENAI_API_KEY=not-needed`,
`OPENAI_MODEL=llama3.1`.

**Securing the service:** set `AI_SERVICE_API_KEY` in `.env` to a random shared secret, and
set the same value as `AiService:ApiKey` in the .NET app (see below). Requests without a
matching `X-API-Key` header get `401 Unauthorized`. Required before exposing port 8000
beyond localhost — otherwise anyone reachable on the network can call the LLM-backed
endpoints directly and consume your OpenAI quota.

### 3. Web app

`appsettings.json` no longer holds real secrets — set them via `dotnet user-secrets`
(auto-loaded in the Development environment) or environment variables (work in any
environment; ASP.NET Core maps double-underscore names to nested config keys):

```bash
cd RubricGuardian.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=RubricGuardian;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;"
dotnet user-secrets set "AiService:ApiKey" "<same value as ai-service's AI_SERVICE_API_KEY>"

# Equivalent environment-variable overrides (e.g. for CI/containers, or when
# running outside the Development environment where user-secrets isn't auto-loaded):
#   ConnectionStrings__Default="Server=...;Password=...;"
#   AiService__ApiKey="<same shared secret>"

dotnet run
```

Open the printed URL (e.g. https://localhost:5001). Sign in with the seeded demo account:

- **Email:** `demo@rubricguardian.dev`
- **Password:** `Demo123!`

The demo account already contains one course, one assignment, an extracted rubric, and a
version-1 submission with a full readiness report — so you can see every screen immediately.

## Accounts & privacy

RubricGuardian is multi-user. Each person registers their own account, and everything
they create — courses, assignments, uploaded documents, submissions, evaluation
reports — is private to them. Isolation is enforced server-side on every query
(ownership is checked in SQL, not hidden in the UI), so one user can never see or
modify another user's data, even by guessing URLs.

- Register at `/register`, sign in at `/login`
- Passwords are hashed with PBKDF2; auth uses secure HTTP-only cookies
- `/account` is each user's personal space: update your display name, change your
  password, and see your workspace stats
- Demo account for quick exploration: `demo@rubricguardian.dev` / `Demo123!`

## Pages

| Route | Purpose |
|---|---|
| `/login`, `/register` | Cookie-based auth |
| `/dashboard` | Overview: stats + recent assignments with readiness bars |
| `/courses`, `/courses/create` | Course management |
| `/assignments/create` | New assignment |
| `/assignments/{id}` | Assignment hub: requirements, documents, versions |
| `/assignments/{id}/upload-instructions` | Upload instructions or rubric → requirements extracted |
| `/assignments/{id}/upload-submission` | Upload work → new version → automatic evaluation |
| `/assignments/{id}/evaluation` | Readiness dashboard: summary, fix checklist, traceability table |

## Design decisions worth knowing

- **Requirements are never invented.** The extraction prompt hard-forbids it, and every
  `Requirement` row stores a `SourceDocumentId` pointing at the exact uploaded document.
- **Weak evidence never counts as done.** The evaluation prompt instructs the model to
  prefer Partial/Unclear over Complete, and readiness scores count Partial as 0.5 and
  Missing/Unclear as 0 (required items only; bonus items never inflate the score).
- **Versioning:** every submission upload creates a new `Submission` row with an
  incremented `VersionNumber`; old evaluations are preserved per version.
- **Storage abstraction:** `IFileStorageService` has one local implementation. To add
  Azure Blob Storage later, implement the same two methods against a blob container and
  swap the DI registration in `Program.cs` — nothing else changes.
- **Re-runs:** the "Re-run check" button re-evaluates an existing version (useful after
  you re-upload a corrected rubric).
- **Failure handling:** if the FastAPI service is down, the web app shows a clear error
  instead of crashing; if the LLM drops a requirement from its response, that item is
  stored as Unclear with a "re-run" suggestion rather than silently vanishing.

## MVP limitations (documented, intentional)

- Submissions longer than ~60k characters are truncated before evaluation.
- Scanned/image-only PDFs are rejected (no OCR yet) with a clear message.
- Local file storage only; no virus scanning of uploads.
- `EnsureCreated()` is used instead of EF migrations — fine for an MVP, switch to
  `dotnet ef migrations` before evolving the schema in production.
