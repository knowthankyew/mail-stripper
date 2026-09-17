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
    await page.waitForSelector('#resultsSection', { state: 'visible', timeout: 15000 });

    const oneSentenceTitle = await page.textContent('#resOneSentenceTitle');
    assert(oneSentenceTitle.length > 20, `One-sentence title too short: ${oneSentenceTitle}`);
    assert(oneSentenceTitle.includes('Cassian Brooks'), `Expected Cassian Brooks in title, got: ${oneSentenceTitle}`);
    assert(oneSentenceTitle.trim().endsWith('.'), `Title should end with a period: ${oneSentenceTitle}`);

    // Verify Focus Management: Focus moved to One-Sentence Title
    await page.waitForFunction(() => document.activeElement && document.activeElement.id === 'resOneSentenceTitle', { timeout: 2000 });
    const isTitleFocused = await page.$eval('#resOneSentenceTitle', el => el === document.activeElement);
    assert(isTitleFocused, 'Expected focus to be automatically managed and moved to #resOneSentenceTitle');

    // Verify Live Region Announcement
    const srText = await page.textContent('#srLiveRegion');
    assert(srText && srText.includes('Email stripped successfully'), `Expected live region announcement, got: "${srText}"`);

    // Verify Short Summary bullet points
    const bullets = await page.$$eval('#resBulletList li', lis => lis.map(l => l.textContent));
    assert(bullets.length >= 3, `Expected at least 3 bullet points, found: ${bullets.length}`);

    // Verify Attachments Grid & Accessibility Attributes
    const attCards = await page.$$('.att-card');
    assert.strictEqual(attCards.length, 3, `Expected 3 attachment cards, found ${attCards.length}`);

    const cardRole = await attCards[0].getAttribute('role');
    assert.strictEqual(cardRole, 'region', 'Attachment card should have role="region"');
    const cardTabIndex = await attCards[0].getAttribute('tabindex');
    assert.strictEqual(cardTabIndex, '0', 'Attachment card should be keyboard focusable (tabindex="0")');
    const cardLabel = await attCards[0].getAttribute('aria-label');
    assert(cardLabel && cardLabel.startsWith('Attachment:'), `Attachment card should have aria-label, got: "${cardLabel}"`);

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

  // Test 3: Inline Attachment Preview Modal with Focus Restoration
  await test('Open Attachment Preview Modal, verify accessible names and focus restoration', async () => {
    const previewBtns = await page.$$('.att-btn-prev');
    assert(previewBtns.length > 0, 'Expected at least 1 preview button');

    // Verify accessible name on preview button
    const prevAriaLabel = await previewBtns[0].getAttribute('aria-label');
    assert(prevAriaLabel && prevAriaLabel.startsWith('Preview '), `Expected accessible name on preview button, got: "${prevAriaLabel}"`);

    // Click preview on first previewable attachment
    await previewBtns[0].click();
    await page.waitForSelector('#previewModal[open]', { timeout: 2000 });

    const modalTitle = await page.textContent('#modalFileName');
    assert(modalTitle && modalTitle.length > 0, 'Modal title empty');

    // Verify modal close button has accessible name
    const closeBtnLabel = await page.$eval('#closeModalBtn', el => el.getAttribute('aria-label'));
    assert(closeBtnLabel && closeBtnLabel.includes('Close'), `Expected Close aria-label, got: "${closeBtnLabel}"`);

    // Close preview modal and verify focus is restored to the preview button
    await page.click('#closeModalBtn');
    await page.waitForSelector('#previewModal', { state: 'hidden', timeout: 2000 });

    const isPrevFocused = await previewBtns[0].evaluate(el => el === document.activeElement);
    assert(isPrevFocused, 'Expected focus to be restored to preview button upon closing preview modal');
  });

  // Test 4: Fast Guardrail Error Rejection on Non-Email Files (e.g. MP4)
  await test('Fast Guardrail: Reject non-email files with diagnostic error banner & live alert', async () => {
    // Reset to ingestion view
    await page.click('#stripAnotherBtn');
    await page.waitForSelector('#ingestionSection', { state: 'visible', timeout: 2000 });

    // Verify focus returned to drop zone
    const isDropFocused = await page.$eval('#dropZone', el => el === document.activeElement);
    assert(isDropFocused, 'Expected focus to return to #dropZone after clicking Strip Another Email');

    // Simulate selecting an MP4 file
    const fileInput = await page.$('#fileInput');
    await fileInput.setInputFiles({
      name: 'video_presentation.mp4',
      mimeType: 'video/mp4',
      buffer: Buffer.from('fake mp4 video bytes')
    });

    // Verify error banner appears with role="alert"
    await page.waitForSelector('#errorBanner', { state: 'visible', timeout: 2000 });
    const errorRole = await page.$eval('#errorBanner', el => el.getAttribute('role'));
    assert.strictEqual(errorRole, 'alert', 'Error banner must have role="alert"');

    const errorMsg = await page.textContent('#errorMessage');
    assert(errorMsg.includes('video file') || errorMsg.includes('not an email'), `Expected video file error, got: ${errorMsg}`);

    // Verify live region received the error announcement
    await page.waitForFunction(() => {
      const el = document.getElementById('srLiveRegion');
      return el && el.textContent && el.textContent.includes('Alert:');
    }, { timeout: 2000 });
    const srAlert = await page.textContent('#srLiveRegion');
    assert(srAlert && srAlert.includes('Alert:'), `Expected alert in live region, got: "${srAlert}"`);

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

    await page.waitForSelector('#resultsSection', { state: 'visible', timeout: 15000 });
    const title = await page.textContent('#resOneSentenceTitle');
    assert(title.includes('Elena Rostova'), `Expected Elena Rostova in title, got: ${title}`);

    const fromMeta = await page.textContent('#resFrom');
    assert(fromMeta.includes('Elena Rostova'), `Expected sender Elena Rostova, got: ${fromMeta}`);
  });

  // Test 6: Comprehensive Accessibility Audit: Icon-only Buttons, Drop Zone Keyboard, Modal Focus Restoration
  await test('Comprehensive A11y: Verify accessible names, drop zone keyboard support, and privacy modal focus restoration', async () => {
    // Reset to ingestion
    await page.click('#stripAnotherBtn');
    await page.waitForSelector('#ingestionSection', { state: 'visible', timeout: 2000 });

    // 1. Drop Zone accessibility
    const dropZone = await page.$('#dropZone');
    assert.strictEqual(await dropZone.getAttribute('role'), 'button', 'Drop zone must have role="button"');
    assert.strictEqual(await dropZone.getAttribute('tabindex'), '0', 'Drop zone must have tabindex="0"');
    const dropLabel = await dropZone.getAttribute('aria-label');
    assert(dropLabel && dropLabel.includes('Upload email file'), `Drop zone missing aria-label: ${dropLabel}`);

    // 2. Icon-only buttons accessible names
    const iconButtons = [
      { id: '#copyTitleBtn', expected: 'Copy' },
      { id: '#copySummaryBtn', expected: 'Copy' },
      { id: '#privacyAuditBtn', expected: 'privacy audit' },
      { id: '#closeErrorBtn', expected: 'error notification' },
      { id: '#closeModalBtn', expected: 'Close' },
      { id: '#closePrivacyModalBtn', expected: 'Close' }
    ];

    for (const btnInfo of iconButtons) {
      const el = await page.$(btnInfo.id);
      assert(el, `Button ${btnInfo.id} not found in DOM`);
      const ariaLabel = await el.getAttribute('aria-label');
      assert(ariaLabel && ariaLabel.toLowerCase().includes(btnInfo.expected.toLowerCase()),
        `Button ${btnInfo.id} expected aria-label containing "${btnInfo.expected}", got: "${ariaLabel}"`);
    }

    // 3. Privacy Modal Open & Focus Restoration
    const privacyBtn = await page.$('#privacyAuditBtn');
    await privacyBtn.click();
    await page.waitForSelector('#privacyModal[open]', { timeout: 2000 });

    // Close Privacy Modal
    await page.click('#closePrivacyModalBtn');
    await page.waitForSelector('#privacyModal', { state: 'hidden', timeout: 2000 });

    // Verify focus restored to privacy button
    const isPrivacyFocused = await privacyBtn.evaluate(el => el === document.activeElement);
    assert(isPrivacyFocused, 'Expected focus to be restored to #privacyAuditBtn upon closing privacy modal');
  });

  console.log(`\n🎉 All ${passed}/${total} Playwright E2E Tests Passed Successfully!\n`);
}

module.exports = { runE2ETests };
