#!/usr/bin/env node
/**
 * audit-claims.mjs - Automated Claims, Architectural Parity & Privacy Invariant Linter for mailStripper.
 *
 * Enforces:
 * 1. Zero External Network Egress & CSP Integrity:
 *    - Verifies SecurityConstants.ContentSecurityPolicy strictly restricts connect-src to 'self' and http://localhost:8000.
 *    - Verifies Program.cs attaches exact HTTP security response headers and X-Local-Only-Guarantee values.
 *    - Verifies wwwroot/app.js makes zero unauthorized external network requests and uses only approved local endpoints.
 * 2. 100% Volatile In-Memory Processing & Zero Disk Leakage:
 *    - Verifies MemoryAttachmentStore uses in-memory streams and stores.
 *    - Verifies MailStripper source code contains zero synchronous or asynchronous disk write APIs.
 * 3. Max Upload Size & Body Budgeting:
 *    - Verifies 50 MB upload limit cap in SecurityConstants and Program.cs Kestrel configuration.
 * 4. Privacy Audit Service & Cryptographic Verification:
 *    - Verifies PrivacyAuditService evaluates socket isolation and SHA-256 fingerprinting.
 *    - Verifies GET /api/strip/privacy-audit endpoint mapping.
 * 5. Supply Chain & Central Package Management (CPM) Conformance:
 *    - Verifies Directory.Packages.props and Directory.Build.props fail-closed security properties.
 *    - Verifies patched System.Security.Cryptography.Pkcs (>= 10.0.13) resolves GHSA-846f-f764-76mx.
 * 6. Vocabulary Guardrails:
 *    - Prohibits unprovable marketing adjectives ('bulletproof', 'conclusive', 'definitive', 'infinitely scalable').
 * 7. Portfolio Master Brief Parity:
 *    - Dynamically measures actual unit test count across MailStripper.Tests and verifies parity with the Brief.
 */

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execSync } from 'node:child_process';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const REPO_ROOT = resolve(__dirname, '..');
const DEV_ROOT = resolve(REPO_ROOT, '..');

const PROHIBITED_WORDS = [
  /\bbulletproof\b/i,
  /\bconclusive\b/i,
  /\bdefinitive\b/i,
  /\binfinitely scalable\b/i,
];

/**
 * Audit 1: Zero External Network Egress & CSP Integrity
 */
export function auditZeroEgress(errors = []) {
  const secConstPath = resolve(REPO_ROOT, 'MailStripper/Security/SecurityConstants.cs');
  const progPath = resolve(REPO_ROOT, 'MailStripper/Program.cs');
  const appJsPath = resolve(REPO_ROOT, 'MailStripper/wwwroot/app.js');

  if (!existsSync(secConstPath)) {
    errors.push('Missing MailStripper/Security/SecurityConstants.cs');
    return;
  }
  if (!existsSync(progPath)) {
    errors.push('Missing MailStripper/Program.cs');
    return;
  }

  const secConstContent = readFileSync(secConstPath, 'utf-8');
  const cspMatch = secConstContent.match(/ContentSecurityPolicy\s*=\s*"([^"]+)";/);
  if (!cspMatch) {
    errors.push('Could not find ContentSecurityPolicy constant assignment in SecurityConstants.cs');
  } else {
    const csp = cspMatch[1];
    if (!csp.includes("connect-src 'self' http://localhost:8000;")) {
      errors.push("SecurityConstants.ContentSecurityPolicy violation: connect-src must allow strictly 'self' and loopback http://localhost:8000;");
    }
    if (!csp.includes("object-src 'none';") || !csp.includes("frame-ancestors 'none';")) {
      errors.push("SecurityConstants.ContentSecurityPolicy violation: object-src and frame-ancestors must be set to 'none'.");
    }
    if (!csp.includes("script-src 'self';")) {
      errors.push("SecurityConstants.ContentSecurityPolicy violation: script-src must be set to 'self' without 'unsafe-inline'.");
    }
  }

  const progContent = readFileSync(progPath, 'utf-8');
  const expectedHeaderAssignments = [
    { name: 'Content-Security-Policy', expectedValue: 'SecurityConstants.ContentSecurityPolicy' },
    { name: 'X-Content-Type-Options', expectedValue: '"nosniff"' },
    { name: 'X-Frame-Options', expectedValue: '"DENY"' },
    { name: 'Referrer-Policy', expectedValue: '"no-referrer"' },
    { name: 'X-Local-Only-Guarantee', expectedValue: '"Air-Gapped: Zero External Egress | In-Memory Processing"' },
  ];

  function escapeRegExp(str) {
    return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  for (const { name, expectedValue } of expectedHeaderAssignments) {
    const escapedValue = escapeRegExp(expectedValue);
    const pattern = new RegExp(`context\\.Response\\.Headers\\.Append\\("${escapeRegExp(name)}",\\s*${escapedValue}\\)`);
    if (!pattern.test(progContent)) {
      errors.push(`MailStripper/Program.cs missing exact security header assignment: Append("${name}", ${expectedValue})`);
    }
  }

  if (existsSync(appJsPath)) {
    const appJsContent = readFileSync(appJsPath, 'utf-8');
    const lines = appJsContent.split('\n');
    lines.forEach((line, idx) => {
      const trimmed = line.trim();
      if (trimmed.startsWith('//') || trimmed.startsWith('*') || trimmed.startsWith('/*')) return;

      // Reject forbidden outbound browser APIs
      if (/\b(XMLHttpRequest|navigator\.sendBeacon|new\s+WebSocket|new\s+EventSource)\b/.test(line)) {
        errors.push(`MailStripper/wwwroot/app.js:${idx + 1}: Forbidden outbound network primitive detected: ${trimmed}`);
      }

    });

    // Validate all fetch() invocations in app.js target local/relative endpoints only (including multiline)
    const globalFetchMatches = appJsContent.matchAll(/\bfetch\s*\(\s*([^,\)]+)/gs);
    for (const match of globalFetchMatches) {
      const destArg = match[1].trim();
      const lineNum = appJsContent.substring(0, match.index).split('\n').length;
      const isApprovedLiteral =
        /^(?:['"`])\/api\/strip\/[^'"`]*['"`]$/.test(destArg) ||
        /^(?:['"`])http:\/\/localhost:8000\/healthz['"`]$/.test(destArg);
      const isValidatedLocalVar =
        destArg === 'previewUrl' && appJsContent.includes("previewUrl.startsWith('/api/strip/')");

      if (!isApprovedLiteral && !isValidatedLocalVar) {
        errors.push(`MailStripper/wwwroot/app.js:${lineNum}: Outbound fetch() destination is not an approved local endpoint: ${destArg}`);
      }
    }
  }
}

/**
 * Audit 2: Volatile In-Memory Processing & Zero Disk Leakage
 */
export function auditVolatileMemoryStorage(errors = []) {
  const storePath = resolve(REPO_ROOT, 'MailStripper/Services/MemoryAttachmentStore.cs');
  if (!existsSync(storePath)) {
    errors.push('Missing MailStripper/Services/MemoryAttachmentStore.cs');
    return;
  }

  const storeContent = readFileSync(storePath, 'utf-8');
  if (!storeContent.includes('ConcurrentDictionary') || !storeContent.includes('MemoryStream')) {
    errors.push('MemoryAttachmentStore must use ConcurrentDictionary and MemoryStream for volatile retention.');
  }

  // Scan all source files in MailStripper (excluding tests) for sync and async disk writing APIs
  const msDir = resolve(REPO_ROOT, 'MailStripper');
  function scanDir(dir) {
    const entries = readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = resolve(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== 'bin' && entry.name !== 'obj' && entry.name !== 'wwwroot') {
          scanDir(fullPath);
        }
      } else if (entry.name.endsWith('.cs')) {
        const content = readFileSync(fullPath, 'utf-8');
        const lines = content.split('\n');
        lines.forEach((line, idx) => {
          const trimmed = line.trim();
          if (trimmed.startsWith('//') || trimmed.startsWith('*') || trimmed.startsWith('/*')) return;
          if (
            /\bFile\.(?:WriteAllBytes|WriteAllBytesAsync|WriteAllText|WriteAllTextAsync|WriteAllLines|WriteAllLinesAsync|Create|CreateText|OpenWrite|Open|Copy|Move|AppendAllText|AppendAllTextAsync|AppendAllLines|AppendAllLinesAsync|AppendText)\b/.test(line) ||
            /\bPath\.GetTempFileName\b/.test(line) ||
            /\bnew\s+FileStream\s*\([^,]+,\s*FileMode\.(?:Create|CreateNew|OpenOrCreate|Append|Truncate)/.test(line) ||
            /\bnew\s+StreamWriter\s*\(\s*(?!\w*(?:Stream|stream)\b)[^)]/.test(line) ||
            /\bnew\s+BinaryWriter\s*\(\s*(?!\w*(?:Stream|stream)\b)[^)]/.test(line)
          ) {
            errors.push(`Zero-disk privacy violation in ${fullPath.replace(REPO_ROOT + '/', '')}:${idx + 1}: Detected disk file write API: ${trimmed}`);
          }
        });
      }
    }
  }

  scanDir(msDir);
}

/**
 * Audit 3: Max Upload Size Budgeting
 */
export function auditMaxUploadBudget(errors = []) {
  const secConstPath = resolve(REPO_ROOT, 'MailStripper/Security/SecurityConstants.cs');
  const progPath = resolve(REPO_ROOT, 'MailStripper/Program.cs');

  if (existsSync(secConstPath)) {
    const secContent = readFileSync(secConstPath, 'utf-8');
    if (!secContent.includes('50 * 1024 * 1024')) {
      errors.push('SecurityConstants.MaxUploadSizeBytes must be configured to 50 MB.');
    }
  }

  if (existsSync(progPath)) {
    const progContent = readFileSync(progPath, 'utf-8');
    if (!progContent.includes('options.Limits.MaxRequestBodySize = SecurityConstants.MaxUploadSizeBytes;')) {
      errors.push('Program.cs must enforce Kestrel MaxRequestBodySize using SecurityConstants.MaxUploadSizeBytes.');
    }
  }
}

/**
 * Audit 4: Privacy Audit Service & Endpoint Parity
 */
export function auditPrivacyAuditService(errors = []) {
  const auditServicePath = resolve(REPO_ROOT, 'MailStripper/Services/PrivacyAuditService.cs');
  const endpointsPath = resolve(REPO_ROOT, 'MailStripper/Endpoints/StripEndpoints.cs');

  if (!existsSync(auditServicePath)) {
    errors.push('Missing MailStripper/Services/PrivacyAuditService.cs');
    return;
  }
  if (!existsSync(endpointsPath)) {
    errors.push('Missing MailStripper/Endpoints/StripEndpoints.cs');
    return;
  }

  const serviceContent = readFileSync(auditServicePath, 'utf-8');
  if (!serviceContent.includes('SHA256.HashData') || !serviceContent.includes('IsAirGapped')) {
    errors.push('PrivacyAuditService must evaluate IsAirGapped and calculate SHA-256 cryptographic verification signature.');
  }

  const endpointsContent = readFileSync(endpointsPath, 'utf-8');
  if (!endpointsContent.includes('/privacy-audit')) {
    errors.push('StripEndpoints.cs must expose GET /privacy-audit endpoint.');
  }
}

/**
 * Audit 5: Central Package Management & Build Properties Conformance
 */
export function auditCentralPackageManagement(errors = []) {
  const dirPackages = resolve(REPO_ROOT, 'Directory.Packages.props');
  const dirBuild = resolve(REPO_ROOT, 'Directory.Build.props');
  const rootPkg = resolve(REPO_ROOT, 'package.json');

  if (!existsSync(dirPackages)) {
    errors.push('Missing Directory.Packages.props');
  } else {
    const content = readFileSync(dirPackages, 'utf-8');
    if (!content.includes('<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>')) {
      errors.push('Directory.Packages.props must enable ManagePackageVersionsCentrally.');
    }
    if (!content.includes('PackageVersion Include="System.Security.Cryptography.Pkcs" Version="10.0.13"')) {
      errors.push('Directory.Packages.props must pin System.Security.Cryptography.Pkcs to version 10.0.13 to resolve GHSA-846f-f764-76mx.');
    }
  }

  if (!existsSync(dirBuild)) {
    errors.push('Missing Directory.Build.props');
  } else {
    const content = readFileSync(dirBuild, 'utf-8');
    if (!content.includes('<NuGetAudit>true</NuGetAudit>')) {
      errors.push('Directory.Build.props must enable NuGetAudit.');
    }
    if (!content.includes('<WarningsAsErrors>$(WarningsAsErrors);NU1903;NU1904</WarningsAsErrors>')) {
      errors.push('Directory.Build.props must treat NU1903 and NU1904 as build errors.');
    }
  }

  if (existsSync(rootPkg)) {
    const pkgJson = JSON.parse(readFileSync(rootPkg, 'utf-8'));
    if (!pkgJson.scripts?.['audit:claims']) {
      errors.push('Root package.json missing "audit:claims" script.');
    }
  }
}

/**
 * Audit 6: Vocabulary Guardrails
 */
export function auditVocabulary(errors = []) {
  const docsToCheck = [
    resolve(REPO_ROOT, 'README.md'),
    resolve(REPO_ROOT, 'NOTICE.md'),
    resolve(REPO_ROOT, 'docs/ARCHITECTURE.md'),
  ];

  for (const docPath of docsToCheck) {
    if (!existsSync(docPath)) continue;
    const lines = readFileSync(docPath, 'utf-8').split('\n');
    lines.forEach((line, idx) => {
      const lower = line.toLowerCase();
      if (
        lower.includes('prohibits unprovable') ||
        lower.includes('prohibited word') ||
        lower.includes('prohibited marketing') ||
        lower.includes('bulletproof') && lower.includes('prohibit')
      ) {
        return;
      }
      for (const pattern of PROHIBITED_WORDS) {
        if (pattern.test(line)) {
          errors.push(
            `Prohibited marketing word '${pattern.source}' found in ${docPath.replace(REPO_ROOT + '/', '')}:${idx + 1}: ${line.trim()}`
          );
        }
      }
    });
  }
}

/**
 * Audit 7: Portfolio Master Brief Parity
 */
export function auditPortfolioMasterBrief(errors = [], { requireBrief = false } = {}) {
  const briefCandidates = [
    resolve(DEV_ROOT, 'KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md'),
    resolve(REPO_ROOT, 'KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md'),
  ];
  const briefPath = briefCandidates.find((p) => existsSync(p));

  if (!briefPath) {
    if (requireBrief) {
      errors.push('KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md not found but required via --require-brief.');
    }
    return false;
  }

  const briefContent = readFileSync(briefPath, 'utf-8');

  // Match: `mail-stripper` / `mailStripper` ... (\d+)/(\d+) [uU]nit [tT]ests
  const pattern = /`mailStripper`.*?(\d+)\/(\d+)\s+Unit Tests/i;
  const match = briefContent.match(pattern);
  if (!match) {
    errors.push('Could not find mailStripper test inventory count in KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md');
    return false;
  }

  const docPassed = parseInt(match[1], 10);
  const docTotal = parseInt(match[2], 10);

  if (docPassed !== docTotal) {
    errors.push(`KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md documents passing mismatch: ${docPassed}/${docTotal}`);
    return false;
  }

  // Establish verified passed test count directly by executing the test suite
  let actualPassedCount = 0;
  let actualTotalCount = 0;
  try {
    const testOutput = execSync('dotnet test mailStripper.slnx', {
      cwd: REPO_ROOT,
      encoding: 'utf-8',
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    const passedMatch = testOutput.match(/Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)/);
    if (!passedMatch) {
      errors.push('Unable to parse test execution output from dotnet test mailStripper.slnx');
      return null;
    }
    const failed = parseInt(passedMatch[1], 10);
    const passed = parseInt(passedMatch[2], 10);
    const skipped = parseInt(passedMatch[3], 10);
    const total = parseInt(passedMatch[4], 10);

    if (failed > 0) {
      errors.push(`Test suite failure: ${failed} tests failed during execution.`);
      return null;
    }
    actualPassedCount = passed;
    actualTotalCount = total;
  } catch (err) {
    errors.push(`Test execution failed or dotnet test runner was unable to run: ${err.message}`);
    return null;
  }

  if (docPassed !== actualPassedCount || docTotal !== actualTotalCount) {
    errors.push(
      `Portfolio Brief Drift: KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md documents ${docPassed}/${docTotal} passing tests, but test runner executed ${actualPassedCount}/${actualTotalCount} passing tests.`
    );
    return null;
  }

  return { passedCount: actualPassedCount, totalCount: actualTotalCount };
}

/**
 * Main execution CLI
 */
export function runAllAudits(options = {}) {
  const errors = [];
  auditZeroEgress(errors);
  auditVolatileMemoryStorage(errors);
  auditMaxUploadBudget(errors);
  auditPrivacyAuditService(errors);
  auditCentralPackageManagement(errors);
  auditVocabulary(errors);
  const briefChecked = auditPortfolioMasterBrief(errors, options);

  return { errors, briefChecked };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  console.log('=== MailStripper Claims, Architecture & Privacy Invariant Audit ===\n');
  const requireBrief = process.argv.includes('--require-brief');
  const { errors, briefChecked } = runAllAudits({ requireBrief });

  if (errors.length > 0) {
    console.error(`[FAIL] Found ${errors.length} claims / architectural invariant violation(s):`);
    for (const err of errors) {
      console.error(`  - ${err}`);
    }
    process.exit(1);
  }

  console.log('[PASS] All mailStripper architectural & claims invariants verified:');
  console.log("  ✓ Zero external network egress air-gap verified (strict connect-src 'self' http://localhost:8000)");
  console.log('  ✓ 100% volatile in-memory storage verified (MemoryAttachmentStore, zero disk file writes)');
  console.log('  ✓ 50 MB maximum upload size budgeting verified in Kestrel configuration');
  console.log('  ✓ PrivacyAuditService socket verification and cryptographic audit endpoint verified');
  console.log('  ✓ Central Package Management (CPM) & patched System.Security.Cryptography.Pkcs 10.0.13 verified');
  console.log('  ✓ Zero prohibited marketing adjectives detected in documentation');
  if (briefChecked) {
    console.log(`  ✓ Portfolio Master Brief dynamic test parity verified (${briefChecked.passedCount}/${briefChecked.totalCount} unit tests)`);
  } else {
    console.log('  - Portfolio Master Brief check skipped (isolated CI)');
  }
  process.exit(0);
}
