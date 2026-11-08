# Roadmap: mailStripper — Air-Gapped Unsubscribe Forensics & Companion Ecosystem

> **Repository**: [`knowthankyew/mail-stripper`](https://github.com/knowthankyew/mail-stripper)  
> **In-Repo Scope**: .NET 10 / `MimeKit` Air-Gapped Ingestion Plane & Static Unsubscribe Forensic Engine (`:5001`) ⇄ Optional `SmolLM2-135M` Loopback ML Sidecar (`http://localhost:8000`)  
> **Governance Standard**: Zero External Network Egress · Loopback Strictly `http://localhost:8000` Only · 100% Volatile In-Memory Processing · Automated Claims Auditing (`scripts/audit-claims.mjs`)

---

## 1. Delivered: v1.0.0 Foundation ✅

The core `.NET 10` ingestion plane and air-gapped reality engine are built, tested, and enforced via automated CI claims linting:

- [x] **100% Volatile RAM Ingestion Plane**: Powered by Jeffrey Stedfast's [`MimeKit`](https://github.com/jstedfast/MimeKit) and [`MemoryAttachmentStore.cs`](MailStripper/Services/MemoryAttachmentStore.cs) (`ConcurrentDictionary` + `MemoryStream`). Zero email bodies or attachments are ever written to disk or `/tmp`.
- [x] **Provable Air-Gapped Network Boundary**: Enforced via strict Content Security Policy (`default-src 'self'; connect-src 'self' http://localhost:8000; object-src 'none'; frame-ancestors 'none'`) and verified at runtime by [`PrivacyAuditService.cs`](MailStripper/Services/PrivacyAuditService.cs) (`GET /api/strip/privacy-audit`).
- [x] **Zero-Config Local ML Sidecar Pairing**: [`MlHybridSummarizer.cs`](MailStripper/Services/MlHybridSummarizer.cs) runs deterministic extractive summarization out-of-the-box and automatically upgrades to neural one-sentence titles when [`event-driven-ftaas`](https://github.com/knowthankyew/event-driven-ftaas) (`SmolLM2-135M` on `http://localhost:8000`) is online.
- [x] **Attachment Extraction & Inline Preview Gallery**: Categorizes extracted MIME parts, provides safe inline previews, and bundles attachments into an in-memory `.zip` stream.
- [x] **Automated Claims & Privacy Invariant Linter**: [`scripts/audit-claims.mjs`](scripts/audit-claims.mjs) verifies CSP locks, zero-disk-write compliance, outbound `fetch()` restrictions, Central Package Management (`Directory.Packages.props`), and documentation vocabulary guardrails.

---

## 2. Strategic Thesis: Air-Gapped Unsubscribe Forensics (`v1.1` → `v2.0`)

### Why Traditional "Unsubscribe" Workflows Fail Users
Clicking an `"Unsubscribe"` link in an email body—or using a naive tool that blindly fires HTTP requests at every unsubscribe URL—exposes users to two distinct threats:
1. **Active-Inbox Reconnaissance Beacons**: Any outbound request to a sender-controlled URL (even an authenticated bulk sender or a spammer with valid SPF/DKIM on a throwaway domain) contacts the remote server and can confirm that the recipient mailbox is active.
2. **Poisoned Landing Pages & Malicious Code**: Clicking an HTML body `<a href>` opens a browser tab outside `mailStripper`'s Content Security Policy—exposing the user to arbitrary JavaScript, exploit kits, drive-by downloads, WebRTC IP leaks, and credential-harvesting pages (*"Sign in to confirm email preferences"*).

### The Post-2024 Structural Signal (and Its Limits)
Since February 2024, major mailbox providers require bulk senders to include **RFC 8058 (`List-Unsubscribe-Post: List-Unsubscribe=One-Click`)** covered by a **`DKIM-Signature`**.
- **Legitimate ESPs** (SendGrid, Mailchimp, Amazon SES, Postmark, HubSpot, Klaviyo) consistently emit single, unambiguous `List-Unsubscribe` and `List-Unsubscribe-Post` headers bound into `DKIM-Signature` (`h=`).
- **Phishing lures** frequently omit RFC 8058 headers altogether or inject deceptive `<a href="...">Unsubscribe</a>` anchors in the HTML body pointing to unaligned domains, punycode homographs (`xn--`), or raw IP addresses.
- **Crucial Caveat (Authentication ≠ Endpoint Safety)**: Because a spammer can register a domain and configure valid SPF/DKIM + RFC 8058 headers, **header authentication and alignment are structural sender evidence, never proof that contacting an endpoint is safe**. Users must always be warned that executing any unsubscribe action contacts the remote host and may confirm recipient activity.

### Repository Governance Boundary: In-Repo Forensics vs. Out-of-Repo Companion Clients
To uphold `knowthankyew/mail-stripper`'s non-negotiable zero-egress and loopback policies (`connect-src 'self' http://localhost:8000;` only, zero external `fetch()` primitives anywhere in this repository):
1. **Inside `knowthankyew/mail-stripper` (Milestones 1 & 1.5 — 100% Air-Gapped)**:
   - Performs purely static, in-memory MIME header and HTML body forensics (`UnsubscribeForensicAnalyzer.cs`) with **zero external HTTP calls, zero DNS lookups, and zero new loopback targets**.
   - Defangs external `<a href>` links inside the Sanitized HTML preview tab so users cannot accidentally pop open an unguarded external browser tab from `mailStripper`.
   - Provides an inspectable forensic breakdown and—only when an RFC 8058 `https://` candidate passes structural checks—an explicit risk warning alongside a shell-hardened, copyable `curl` command (`-o /dev/null`) for manual user execution outside `mailStripper`.
2. **Outside `knowthankyew/mail-stripper` (Separately Governed Companion Repositories — Horizon)**:
   - Any tool that performs outbound network execution (such as a Universal Thunderbird/Browser WebExtension or an IMAP quarantine sidecar) **must live in a separate, independently governed repository** with its own explicit network permission model and security policy—never placing external `fetch()` calls or non-`:8000` loopback clients inside `knowthankyew/mail-stripper`.

---

## 3. Master Milestone Grid

| Milestone | Target Version | Governance Scope | Core Objective | Status |
| :--- | :---: | :--- | :--- | :---: |
| **Milestone 1** | `v1.1.0` | **In-Repo (`mailStripper`)** | Air-Gapped Unsubscribe Forensic Analyzer (`UnsubscribeForensicAnalyzer.cs`), HTML Preview Link Defanging & Web UI Shield Card | ✅ **Delivered** |
| **Milestone 1.5** | `v1.1.5` | **In-Repo (`mailStripper`)** | Trusted `authserv-id` Optional Configuration & Byte-Preserving Multipart Forensics Verification | 📋 **Scoped (Next)** |
| **Companion A** | `External Repo` | **Separate Repo (`unsubscribe-shield-ext`)** | Standalone Thunderbird MailExtension + Chromium/Firefox WebExtension (Governed Outside `mail-stripper`) | 🔭 **External Companion** |
| **Companion B** | `External Repo` | **Separate Repo (`mail-stripper-imap`)** | Protocol-Level IMAP `@Safe-Unsubscribe` Quarantine Watcher (Governed Outside `mail-stripper`) | 🔭 **External Companion** |

---

## 4. In-Repo Scoped Objectives (`knowthankyew/mail-stripper`)

### Milestone 1 (`v1.1.0`): Air-Gapped Unsubscribe Forensic Engine & HTML Link Defanging ✅

#### Well-Scoped Objectives
- [x] **Define Forensic Models (`MailStripper/Models/UnsubscribeForensics.cs`)**:
  - `UnsubscribeClassification`: Provenance-honest enum (`OneClickPostCandidate`, `MailtoCandidate`, `HttpLandingCandidate`, `PoisonOrReconTrap`, `NoUnsubscribeFound`). Never claims cryptographic verification on untrusted `.eml` uploads.
  - `AuthenticationProvenance`: Enum (`UnverifiedUpload`, `MissingAuthHeaders`, `SyntheticPastedBody`, `AmbiguousOrForgedHeader`) reflecting RFC 8601 trust boundaries on static `.eml` / pasted inputs.
  - `UnsubscribeCandidate`: Captures source (`HeaderListUnsubscribe`, `BodyHtmlAnchor`, `BodyPlainText`), sanitized URI, scheme (`https`, `http`, `mailto`), normalized host (with punycode decoding), visible anchor text, and individual risk flags.
  - `UnsubscribeForensicReport`: Attached to `StrippedEmail.Unsubscribe` containing primary classification, risk score (`0–100`), structural alignment evidence (`SingleUnambiguousHeader`, `DkimCoversUnsubscribeHeaders`, `AuthResultsClaimsDkimPass`, `AlignedWithSenderOrKnownEsp`), explicit recon-beacon warning text, detected poison signals, and a shell-hardened `curl` command string for qualifying RFC 8058 `https://` candidates.
- [x] **Implement `UnsubscribeForensicAnalyzer.cs` (`MailStripper/Services/`)**:
  - Purely in-memory static inspection over `MimeKit.MimeMessage` headers and HTML/text bodies (zero disk I/O, zero network/DNS calls).
  - Enforces RFC 6376 / RFC 8058 / RFC 8601 header integrity rules (duplicate header rejection, exact signature selector/domain/hash-prefix binding, and provenance separation).
  - Scans `message.HtmlBody` using linear-time `RegexOptions.NonBacktracking` (with 250ms match timeout and `MaxUnsubscribeCandidates = 50`) for `<a ...>` elements matching unsubscribe/opt-out/preferences semantics, cross-checking them against header declarations.
- [x] **Render the "🛡️ Unsubscribe Shield" Panel & Defang HTML Preview Links (`MailStripper/wwwroot/index.html` & `app.js`)**:
  - Built strictly with safe DOM node creation (`textContent` / `createElement`—zero `innerHTML` interpolation of untrusted email strings) and zero external asset or `fetch()` calls.
  - **HTML Preview Link Defanging**: Updates `renderSanitizedHtmlPreview()` in `MailStripper/wwwroot/app.js` to strip live external navigation from `<a href>` and `<area>` elements (converting `<a>` tags into inert `<span class="defanged-link">` badges via `Document.parseHTML` / `DOMParser` + `replaceChildren`) so clicking inside the Sanitized HTML preview never opens an untrusted external browser tab.
  - Displays an explicit **Active-Inbox Recon Warning** on every actionable candidate (*"Executing an unsubscribe request contacts the remote host and can confirm to the sender that your mailbox is active. Only proceed for senders or ESPs you recognize."*), paired with a **"Copy Sterile `curl`"** button (`--globoff --proto '=https' --tlsv1.2 --max-redirs 0 -o /dev/null`).
- [x] **Expand Demo & Automated Test Fixtures (`MailStripper.Tests` & Playwright)**:
  - Add xUnit and Playwright test fixtures (updating `KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md` test counts in lockstep) covering:
    1. Conforming RFC 8058 + unambiguously bound `DKIM-Signature` (`s=`, `d=`, `h=`) newsletter.
    2. ESP-hosted unsubscribe (`sendgrid.net` / `list-manage.com`) with domain alignment vs. sender-activity warning separation.
    3. Poisoned phishing email with a fake body `<a href>` (deceptive anchor text + punycode/unaligned domain + missing `List-Unsubscribe` header).
    4. Adversarial header attacks: prepended duplicate `List-Unsubscribe` header, second forged `DKIM-Signature` with matching `d=` but different `s=`, forged `Authentication-Results`, `javascript:`/`http:` downgrades, and shell single-quote injection attempts.
    5. E2E verification that `<a>` tags inside the Sanitized HTML preview are defanged and cannot navigate away.

### Milestone 1.5 (`v1.1.5`): Provenance Hardening & Byte-Exact Ingestion Parity

- [ ] **Trusted `authserv-id` Verification Option**: Allow callers of `/api/strip/file` (or local `appsettings.json` configuration) to optionally declare an expected receiving MX `authserv-id` (per RFC 8601), keeping default static `.eml` uploads strictly labeled `UnverifiedUpload`.
- [ ] **8-Bit Non-UTF-8 MIME Preservation Tests**: Add xUnit tests verifying that `POST /api/strip/file` (`Stream` byte ingestion) preserves raw `ISO-8859-1` / `Windows-1252` 8-bit MIME bodies and raw DKIM header bytes without UTF-8 replacement corruption (`U+FFFD`).

---

## 5. Companion Ecosystem Design Guardrails (Out-of-Repo Boundaries)

When building external companion tools (such as the standalone Thunderbird/Browser extension in its own repository) that consume `mailStripper` or execute RFC 8058 requests:
- **Strict Repository Separation**: All WebExtension background workers executing external `fetch()` requests or inbound API calls to local services live in a separate repository with dedicated governance—keeping `knowthankyew/mail-stripper` 100% free of external network primitives.
- **Byte-Preserving Raw MIME Handling**: Companion Thunderbird adapters calling `browser.messages.getRaw(messageId, { data_format: 'File' })` must check `blob.size <= 10 * 1024 * 1024` *before* reading and keep the message as raw bytes (`Uint8Array` / `Blob` multipart upload) rather than calling `Blob.text()`, which forces UTF-8 decoding and corrupts 8-bit non-UTF-8 MIME parts.
- **Immediate Response Stream Discard on Execution**: Any external companion tool executing an RFC 8058 `POST` must set `credentials: 'omit'`, `redirect: 'manual'`, `referrerPolicy: 'no-referrer'`, and immediately cancel the response body (`await res.body?.cancel()`) after inspecting `res.status`.

---

## 6. Notice of Edge Cases Already Detected (In-Repo Design Guardrails)

The following edge cases have been identified upfront and must be explicitly enforced in Milestone 1:

### Edge Case 1: Air-Gap Invariant Preservation (`audit-claims.mjs` & `.coderabbit.yaml`)
- **Problem**: `MailStripper` and `wwwroot/app.js` operate under a strict zero-egress air-gap (`connect-src 'self' http://localhost:8000;` only). Executing outbound HTTP `POST` requests or live DNS `_domainkey` queries from `.NET` or `wwwroot/app.js` would violate the air-gap invariant.
- **Mitigation**: Perform 100% static in-memory MIME inspection with zero external HTTP or DNS calls, defang HTML preview links in `wwwroot/app.js`, and emit only an inspectable forensic report plus a copyable sterile `curl` command.

### Edge Case 2: Untrusted `Authentication-Results` Provenance in Uploaded `.eml` Files (RFC 8601)
- **Problem**: Any `.eml` file uploaded from disk or pasted as text can contain forged topmost `Received:` and `Authentication-Results: mx.example.com; dkim=pass; spf=pass` headers crafted by an attacker. Matching `header.d=` against an equally attacker-crafted `DKIM-Signature` does not prove cryptographic authenticity without DNS key verification or an authenticated mail-store trust boundary (RFC 8601).
- **Mitigation**: Never label static `.eml` uploads as cryptographically `"Verified"`. Classify qualifying candidates as `OneClickPostCandidate` with `Provenance = UnverifiedUpload` unless ingested with a configured trusted `authserv-id`, and surface a clear provenance badge in the UI.

### Edge Case 3: Ambiguous Multi-Signature `DKIM-Signature` Confusion (`d=`, `s=`, `b=`)
- **Problem**: An email can carry a genuine `DKIM-Signature` (`d=esp.example`, `s=sel1`, `h=from:to:subject`) alongside a second, invalid `DKIM-Signature` injected by an attacker with the *same* `d=esp.example` (`s=fake`, `h=from:list-unsubscribe:list-unsubscribe-post`). Matching `Authentication-Results` to `DKIM-Signature` by `d=` alone falsely credits the forged signature's `h=` coverage.
- **Mitigation**: Bind `Authentication-Results` `dkim=pass` entries to a specific `DKIM-Signature` header using **all available disambiguators**: signing domain (`header.d=` ↔ `d=`), selector (`header.s=` ↔ `s=`), and signature prefix (`header.b=` ↔ `b=` per RFC 6008 when present). If multiple `DKIM-Signature` headers exist and the passing signature cannot be identified unambiguously, **fail closed** and reject `DKIM` unsubscribe-header coverage.

### Edge Case 4: Duplicate `List-Unsubscribe` Header Prepending (RFC 6376 § 5.4.2 Bottom-Up Selection)
- **Problem**: Under RFC 6376 § 5.4.2, DKIM evaluates repeated header fields **from the bottom upward**. If a legitimate message contains one `List-Unsubscribe` header signed once in `h=list-unsubscribe`, an attacker in transit can **prepend a second, unsigned `List-Unsubscribe: <https://evil.example/unsub>` at the top of the message** without breaking the original DKIM signature. A naive parser reading the first `List-Unsubscribe` header would extract the attacker's URL.
- **Mitigation**: Enforce **strict single-instance header hygiene**: if a message contains more than one `List-Unsubscribe` header or more than one `List-Unsubscribe-Post` header, classify immediately as `PoisonOrReconTrap` (`DuplicateUnsubscribeHeaderDetected`) and refuse one-click candidate status. Additionally verify that `h=` in the matched `DKIM-Signature` explicitly includes `list-unsubscribe` (and `list-unsubscribe-post` when present) per RFC 8058 § 3.1.

### Edge Case 5: Authenticated Spammers & Sender-Activity Confirmation (`Auth ≠ Safe`)
- **Problem**: A spammer or recon actor can purchase a fresh domain, publish valid SPF/DKIM records, or send through a shared ESP (`sendgrid.net`, `amazonses.com`) with a valid signed `List-Unsubscribe` URL. Sending an HTTP `POST` or `mailto:` still contacts the sender/ESP and confirms the recipient opened and acted on the message.
- **Mitigation**: Keep **Sender/ESP Alignment Evidence** strictly separate from **Endpoint Trust**. Even when a message qualifies as `OneClickPostCandidate` via organizational domain (`eTLD+1` using Public Suffix List rules so `.co.uk`/`.com.au` are preserved) or Known-ESP alignment, display a mandatory user warning before copying/running `curl`: *"Executing this unsubscribe request contacts `<host>` and can confirm to the sender that your email address is active."*

### Edge Case 6: Multi-URI `List-Unsubscribe` Folding, Comments, `mailto:` Abuse & Scheme Downgrades
- **Problem**: RFC 2369 `List-Unsubscribe` headers often contain multiple comma-separated `<uri>` tokens, folded whitespace, and RFC 5322 comments, or adversarial `javascript:`, `data:`, `file:`, `http://`, or `mailto:?bcc=...&attach=...` parameters.
- **Mitigation**: Extract `<...>` tokens after stripping RFC 5322 comments. Allow only `https:` and sanitized `mailto:` (retaining only `subject` and `body` per RFC 6068) as candidates; downgrade `http:` to `UnencryptedSchemeWarning`; classify `javascript:`/`data:`/`file:`/`vbscript:` immediately as `PoisonOrReconTrap`. Require an `https://` URI whenever `List-Unsubscribe-Post: List-Unsubscribe=One-Click` is claimed (RFC 8058).

### Edge Case 7: Shell Injection, URL Globbing & Terminal Escape Injection in `curl` Snippets
- **Problem**: Generating a copyable `curl` command from an untrusted header URI risks shell injection (`'`, `$()`, backticks), `curl` globbing (`[]`, `{}`), or terminal ANSI escape injection if the remote server returns malicious bytes to stdout.
- **Mitigation**: Emit a `curl` command only when `Uri.TryCreate(..., UriKind.Absolute, out var uri)` succeeds with `uri.Scheme == Uri.UriSchemeHttps` and zero control/whitespace characters, using RFC 3986 percent-encoded `uri.AbsoluteUri`, POSIX single-quote escaping (`'\''`), and discarding response body bytes via `-o /dev/null`:
  ```bash
  curl --globoff --proto '=https' --tlsv1.2 --max-redirs 0 -sS -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/x-www-form-urlencoded' -d 'List-Unsubscribe=One-Click' 'https://...'
  ```

### Edge Case 8: Legacy `GET` Unsubscribe Links vs. RFC 8058 `POST` One-Click
- **Problem**: Mailing lists that emit `List-Unsubscribe: <https://...>` **without** `List-Unsubscribe-Post: List-Unsubscribe=One-Click` expect an interactive `GET` landing page and often fail with `405 Method Not Allowed` on `POST`.
- **Mitigation**: Classify `https://` headers lacking `List-Unsubscribe-Post: List-Unsubscribe=One-Click` strictly as `HttpLandingCandidate` and omit the `POST` `curl` generator.

### Edge Case 9: IDN Homographs (`xn--`), Raw IPs, Userinfo Tricks & Anchor-Text Spoofing
- **Problem**: Phishing body links use `<a href="https://185.199.108.153/unsub">https://apple.com/unsubscribe</a>`, userinfo prefixes (`https://apple.com@evil.example`), or punycode (`https://xn--pple-43d.com/unsubscribe`).
- **Mitigation**: Normalize hostnames via `System.Globalization.IdnMapping`, flag `xn--` punycode, raw IPv4/IPv6 literals, non-empty `Uri.UserInfo`, or non-443 ports, and classify any `<a>` whose visible text contains a domain/URL mismatching the `href` `eTLD+1` as `PoisonOrReconTrap`.

### Edge Case 10: Pasted Snippets (`clipboard@local`), Bounded MIME Recursion & ReDoS Budgets
- **Problem**: Pasted text (`POST /api/strip/text`) receives a synthetic header (`From: Pasted Input <clipboard@local>`) in [`MimeKitEmailParser.ParseTextAsync`](MailStripper/Services/MimeKitEmailParser.cs), while adversarial `.eml` files may contain deeply nested `message/rfc822` MIME bombs or multi-megabyte HTML anchor pathological strings.
- **Mitigation**: Detect `clipboard@local` (`HeaderContextAvailable = false`) so pasted body-only text isn't penalized for missing RFC 8058 headers; cap forwarded `MessagePart` recursion at `MaxForwardedDepth = 3` and candidates at `MaxUnsubscribeCandidates = 50`; enforce `RegexOptions.NonBacktracking` with 250ms match timeouts across all patterns.
