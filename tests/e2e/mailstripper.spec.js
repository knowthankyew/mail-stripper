const path = require('path');
const assert = require('assert');

// E2E Test Suite for mailStripper using Playwright
async function runE2ETests(page, baseUrl = 'http://localhost:5001') {
  console.log(`\n🚀 Starting mailStripper Playwright E2E Tests on ${baseUrl}...\n`);
  let passed = 0;
  let total = 0;

  async function test(name, fn) {
    total++;
    process.stdout.write(`[Test ${total}] ${name}... `);
    try {
      await fn();
      console.log('✅ PASS');
      passed++;
    } catch (err) {
      console.log('❌ FAIL');
      console.error(`  Error: ${err.message}`);
      throw err;
    }
  }

  // Test 1: Page Load & Local-Only Air-Gapped Verification
  await test('Verify Page Branding and Local-Only Privacy Guarantee', async () => {
    await page.goto(baseUrl, { waitUntil: 'networkidle' });
    const title = await page.title();
    assert(title.includes('mailStripper'), `Expected page title to contain 'mailStripper', got '${title}'`);

    // Verify Local-Only Privacy Button exists
    const privacyBtn = await page.$('#privacyAuditBtn');
    assert(privacyBtn, 'Privacy audit badge not found');

    // Click to open Privacy Audit Modal
    await privacyBtn.click();
    await page.waitForSelector('#privacyModal[open]', { timeout: 2000 });

    // Verify Audit Content
    const extConn = await page.textContent('#auditExtConn');
    assert(extConn.includes('0') || extConn.includes('Air-Gapped'), `Expected zero external connections, got: ${extConn}`);

    // Close Modal
    await page.click('#closePrivacyModalBtn');
    await page.waitForSelector('#privacyModal', { state: 'hidden', timeout: 2000 });
  });

  // Test 2: Ingest Sample Email with 3 Attachments
  await test('Load Sample Email, verify One-Sentence Title & 3 Attachments', async () => {
    await page.click('#loadSampleBtn');
    await page.waitForSelector('#resultsSection', { state: 'visible', timeout: 5000 });

    const oneSentenceTitle = await page.textContent('#resOneSentenceTitle');
    assert(oneSentenceTitle.length > 20, `One-sentence title too short: ${oneSentenceTitle}`);
    assert(oneSentenceTitle.includes('Cassian Brooks'), `Expected Cassian Brooks in title, got: ${oneSentenceTitle}`);
    assert(oneSentenceTitle.trim().endsWith('.'), `Title should end with a period: ${oneSentenceTitle}`);

    // Verify Short Summary bullet points
    const bullets = await page.$$eval('#resBulletList li', lis => lis.map(l => l.textContent));
    assert(bullets.length >= 3, `Expected at least 3 bullet points, found: ${bullets.length}`);

    // Verify Attachments Grid
    const attCards = await page.$$('.att-card');
    assert.strictEqual(attCards.length, 3, `Expected 3 attachment cards, found ${attCards.length}`);

    const attFiles = await page.$$eval('.att-filename', els => els.map(e => e.textContent.trim()));
    assert(attFiles.includes('q3_budget_breakdown.csv'), 'Missing q3_budget_breakdown.csv');
    assert(attFiles.includes('security_compliance_signoff.txt'), 'Missing security_compliance_signoff.txt');
    assert(attFiles.includes('pipeline_architecture_diagram.png'), 'Missing pipeline_architecture_diagram.png');

    // Verify Download All ZIP button is visible with download attribute
    const zipBtn = await page.$('#downloadAllZipBtn');
    assert(zipBtn, 'Download All ZIP button missing');
    const zipHref = await zipBtn.getAttribute('href');
    assert(zipHref && zipHref.includes('/attachments/download-all'), `Invalid zip href: ${zipHref}`);
  });

  // Test 3: Inline Attachment Preview Modal
  await test('Open Attachment Preview Modal and verify content display', async () => {
    const previewBtns = await page.$$('.att-btn-prev');
    assert(previewBtns.length > 0, 'Expected at least 1 preview button');

    // Click preview on first previewable attachment
    await previewBtns[0].click();
    await page.waitForSelector('#previewModal[open]', { timeout: 2000 });

    const modalTitle = await page.textContent('#modalFileName');
    assert(modalTitle && modalTitle.length > 0, 'Modal title empty');

    // Close preview modal
    await page.click('#closeModalBtn');
    await page.waitForSelector('#previewModal', { state: 'hidden', timeout: 2000 });
  });

  // Test 4: Fast Guardrail Error Rejection on Non-Email Files (e.g. MP4)
  await test('Fast Guardrail: Reject non-email files with diagnostic error banner', async () => {
    // Reset to ingestion view
    await page.click('#stripAnotherBtn');
    await page.waitForSelector('#ingestionSection', { state: 'visible', timeout: 2000 });

    // Simulate selecting an MP4 file
    const fileInput = await page.$('#fileInput');
    await fileInput.setInputFiles({
      name: 'video_presentation.mp4',
      mimeType: 'video/mp4',
      buffer: Buffer.from('fake mp4 video bytes')
    });

    // Verify error banner appears
    await page.waitForSelector('#errorBanner', { state: 'visible', timeout: 2000 });
    const errorMsg = await page.textContent('#errorMessage');
    assert(errorMsg.includes('video file') || errorMsg.includes('not an email'), `Expected video file error, got: ${errorMsg}`);

    // Dismiss error
    await page.click('#closeErrorBtn');
    await page.waitForSelector('#errorBanner', { state: 'hidden', timeout: 2000 });
  });

  // Test 5: Paste Raw Email Text Ingestion
  await test('Paste raw RFC 822 email text and verify stripped output', async () => {
    await page.click('#tabPasteBtn');
    await page.waitForSelector('#panelPaste', { state: 'visible', timeout: 2000 });

    const testEmail = `From: Elena Rostova <elena@ciso.gov>
Subject: Final System Security Certification
Date: Thu, 17 Sep 2026 12:00:00 +0000

Dear Architecture Board,

Please confirm receipt of the verified security clearance audit. All staging nodes have been approved for immediate deployment.

Best regards,
Elena Rostova
Lead Security Auditor`;

    await page.fill('#pasteContent', testEmail);
    await page.click('#submitPasteBtn');

    await page.waitForSelector('#resultsSection', { state: 'visible', timeout: 5000 });
    const title = await page.textContent('#resOneSentenceTitle');
    assert(title.includes('Elena Rostova'), `Expected Elena Rostova in title, got: ${title}`);

    const fromMeta = await page.textContent('#resFrom');
    assert(fromMeta.includes('Elena Rostova'), `Expected sender Elena Rostova, got: ${fromMeta}`);
  });

  console.log(`\n🎉 All ${passed}/${total} Playwright E2E Tests Passed Successfully!\n`);
}

module.exports = { runE2ETests };
