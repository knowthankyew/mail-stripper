<!-- diagram-meta
  last-validated: 2026-10-19
  covers: [MailStripper/**, MailStripper.Tests/**]
  generated-by: manual
-->

# Architecture & Invariants Specification: MailStripper Unsubscribe Engine

> Headless Playwright automation agent, CAN-SPAM Act verifier, and predatory dark-pattern email unsubscriber.

---

## 1. Executive Summary

`mailStripper` is a specialized automation reality engine in the `knowthankyew` portfolio built to dismantle dark patterns in commercial marketing emails. Using .NET 10 and Playwright headless Chromium, it navigates labyrinthine unsubscribe flows, completes multi-step confirmation sequences, validates adherence to federal CAN-SPAM rules (15 U.S.C. § 7704) and FTC Click-to-Cancel standards, captures cryptographic screenshot evidence of completion, and terminates with zero residual cookie or credential storage.

---

## 2. System Architecture & Component Interactions

### A. Execution Sandbox & Isolation Architecture

```mermaid
flowchart TD
    subgraph ClientLayer["1. Ingestion & Trigger"]
        User["👤 Consumer / Email Client"]
        Request["📨 Raw Email / Unsubscribe Link Submission"]
    end

    subgraph ServiceCore["2. .NET 10 Engine Core (:5200)"]
        API[".NET 10 API Gateway<br/><code>Endpoints/UnsubscribeEndpoints.cs</code>"]
        Parser["📧 Email & Header Parser<br/><code>Services/EmailParser.ts</code>"]
        Navigator["🤖 Playwright Flow Orchestrator<br/><code>Services/PlaywrightService.cs</code>"]
        Telemetry["🛡️ KnowThankYew.Privacy.Telemetry<br/><i>(ActivitySource + Ring Buffer)</i>"]
    end

    subgraph HeadlessBrowser["3. Ephemeral Chromium Sandbox"]
        Context["🔒 Isolated Browser Context<br/><i>(Unique User Data Dir)</i>"]
        PageNav["🌐 Page Navigation & Dark Pattern Bypass"]
        FormFiller["📝 One-Click Form / Radio Handler"]
        ScreenshotTaker["📷 Visual Evidence Capturer"]
    end

    subgraph OutputAndPurge["4. Evidence & Amnesiac Wipe"]
        Proof["🖼️ Visual Screenshot Audit Trail"]
        AmnesiaPurge["🔥 Context.DisposeAsync()<br/><i>(Purges Cookies, Cache & Storage)</i>"]
    end

    %% Flow
    User -->|"Submits Unsubscribe Link"| Request
    Request --> API
    API --> Parser
    Parser -->|"Target URL & Method"| Navigator
    Navigator -.-> Telemetry

    Navigator -->|"Spawn Ephemeral Context"| Context
    Context --> PageNav
    PageNav --> FormFiller
    FormFiller --> ScreenshotTaker

    ScreenshotTaker --> Proof
    ScreenshotTaker --> AmnesiaPurge
    Proof -->|"Audit Receipt"| User

    classDef client fill:#0f172a,stroke:#64748b,stroke-width:1px,color:#f8fafc;
    classDef core fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#f8fafc;
    classDef sandbox fill:#14532d,stroke:#22c55e,stroke-width:2px,color:#f8fafc;
    classDef purge fill:#78350f,stroke:#f59e0b,stroke-width:2px,color:#f8fafc;

    class User,Request client;
    class API,Parser,Navigator,Telemetry core;
    class Context,PageNav,FormFiller,ScreenshotTaker sandbox;
    class Proof,AmnesiaPurge purge;
```

### B. Automated Unsubscribe & Verification Lifecycle

```mermaid
sequenceDiagram
    autonumber
    actor Consumer as Consumer / Client
    participant API as .NET 10 API
    participant Nav as Playwright Service
    participant Browser as Ephemeral Chromium
    participant Target as Marketing Unsubscribe Portal

    Consumer->>API: POST /api/v1/unsubscribe (rawEmailOrUrl)
    API->>Nav: ProcessUnsubscribeAsync(targetUrl)
    Nav->>Browser: Launch isolated IBrowserContext (incognito)
    Nav->>Browser: NewPageAsync()
    Browser->>Target: Navigate to unsubscribe portal
    Target-->>Browser: Return HTML form (radio buttons, confirmation inputs)
    Nav->>Browser: Detect & click "Unsubscribe from all", fill email if required
    Browser->>Target: Submit unsubscribe form
    Target-->>Browser: Confirmation message ("You have been unsubscribed")
    Nav->>Browser: Capture FullPageScreenshotAsync()
    Nav->>Browser: Context.CloseAsync() & DisposeAsync()
    Note over Browser: All cookies, local storage & cache physically destroyed
    Nav-->>API: Return status (SUCCESS) & evidence thumbnail
    API-->>Consumer: 200 OK with confirmation receipt & screenshot proof
```

---

## 3. Core Architectural Invariants

| # | Invariant | Enforcement & Guarantee |
|:--|:----------|:------------------------|
| 1 | **Ephemeral Sandboxing** | Every unsubscribe operation executes inside an isolated, newly instantiated browser context destroyed immediately upon completion. |
| 2 | **Zero Credential Persistence** | User email addresses and session cookies are kept exclusively in transient heap variables; never stored in a database or logs. |
| 3 | **Central Package Management (CPM)** | 100% of .NET packages pinned centrally in `Directory.Packages.props` with fail-closed security gating (`NU1903`/`NU1904`). |
| 4 | **Visual Evidence Verification** | An unforgeable visual snapshot of the confirmation page is generated before context disposal, providing legal proof of cancellation under CAN-SPAM. |
| 5 | **Volatile Operational Telemetry** | Telemetry is strictly allowlisted via `KnowThankYew.Privacy.Telemetry`; domain names, recipient emails, and URLs are sanitized. |

---

## 4. Directory Structure

```
mailStripper/
├── Directory.Build.props        # NuGetAudit & security warnings
├── Directory.Packages.props     # Central Package Management (CPM)
├── Dockerfile                   # Multi-stage container with Playwright dependencies
├── mailStripper.slnx            # Solution definition
├── MailStripper/
│   ├── Endpoints/               # .NET 10 Minimal API endpoints
│   ├── Models/                  # Request/response DTOs & status types
│   ├── Security/                # URL sanitization & host allowlisting
│   ├── Services/                # Playwright headless engine & email parsing
│   ├── Program.cs               # Application entrypoint & DI setup
│   └── wwwroot/                 # Web interface assets
├── MailStripper.Tests/          # xUnit test suite with mocked browser flows
└── docs/
    ├── ARCHITECTURE.md          # Architecture, data flow & invariant specification
    └── PRIVACY_TELEMETRY_SCHEMA.md # Telemetry contract specification
```
