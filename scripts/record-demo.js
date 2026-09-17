const path = require('path');
const fs = require('fs');
const { execSync } = require('child_process');
const { chromium } = require('playwright-core');

async function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

(async () => {
  const repoRoot = path.resolve(__dirname, '..');
  const tempVideoDir = path.join(repoRoot, '.temp_demo_videos');
  if (!fs.existsSync(tempVideoDir)) fs.mkdirSync(tempVideoDir, { recursive: true });

  const browserPath = fs.existsSync('/Applications/Brave Browser.app/Contents/MacOS/Brave Browser')
    ? '/Applications/Brave Browser.app/Contents/MacOS/Brave Browser'
    : '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';

  console.log(`🎬 Launching browser for automated demo recording: ${browserPath}...`);
  const browser = await chromium.launch({
    executablePath: browserPath,
    headless: true,
    args: ['--no-sandbox', '--disable-gpu', '--window-size=1366,860']
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 860 },
    recordVideo: {
      dir: tempVideoDir,
      size: { width: 1366, height: 860 }
    }
  });

  const page = await context.newPage();

  console.log('Step 1: Navigating to mailStripper UI...');
  await page.goto('http://localhost:5001/', { waitUntil: 'networkidle' });
  await sleep(1800);

  // Step 2: Open Local-Only & Air-Gapped Proof Modal
  console.log('Step 2: Inspecting Local-Only & Air-Gapped Privacy Proof...');
  await page.click('#privacyAuditBtn');
  await sleep(2200);

  // Run Live Audit
  console.log('Step 3: Running Live Privacy Audit...');
  await page.click('#runAuditBtn');
  await sleep(2500);

  // Close Privacy Modal
  await page.click('#closePrivacyModalBtn');
  await sleep(1500);

  // Step 4: Demonstrate Guardrail Rejection on Non-Email Files (e.g. MP4)
  console.log('Step 4: Demonstrating Guardrail on Invalid Binary File...');
  const fileInput = await page.$('#fileInput');
  await fileInput.setInputFiles({
    name: 'presentation_meeting.mp4',
    mimeType: 'video/mp4',
    buffer: Buffer.from('fake mp4 video bytes')
  });
  await sleep(2800);

  // Dismiss Error
  await page.click('#closeErrorBtn');
  await sleep(1200);

  // Step 5: Ingest Sample Email with 3 Attachments
  console.log('Step 5: Ingesting Sample Enterprise Email with 3 Attachments...');
  await page.click('#loadSampleBtn');
  await page.waitForSelector('#resultsSection', { state: 'visible', timeout: 5000 });
  await sleep(2200);

  // Scroll to show Hero One-Sentence Title and Summary
  console.log('Step 6: Showcasing One-Sentence Title and Highlights...');
  await page.evaluate(() => window.scrollBy({ top: 120, behavior: 'smooth' }));
  await sleep(2500);

  // Step 7: Open Attachment Preview Modal
  console.log('Step 7: Previewing Attachment Inline...');
  const previewBtns = await page.$$('.att-btn-prev');
  if (previewBtns.length > 0) {
    await previewBtns[0].click();
    await page.waitForSelector('#previewModal[open]', { timeout: 2000 });
    await sleep(2500);
    await page.click('#closeModalBtn');
    await sleep(1200);
  }

  // Step 8: Peek Inside Cleaned Email Body Inspector
  console.log('Step 8: Peeking Inside Cleaned Email Inspector...');
  await page.evaluate(() => window.scrollBy({ top: 350, behavior: 'smooth' }));
  await sleep(1200);
  await page.click('#bodyInspector summary');
  await sleep(2200);

  // Switch to Sanitized HTML view
  await page.click('#tabBodyHtml');
  await sleep(2200);

  // Step 9: Reset and Demonstrate Raw Text Ingestion
  console.log('Step 9: Demonstrating Raw Text Paste Ingestion...');
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'smooth' }));
  await sleep(1000);
  await page.click('#stripAnotherBtn');
  await sleep(1200);

  await page.click('#tabPasteBtn');
  await sleep(1000);

  const pastedEmail = `From: Elena Rostova <elena.rostova@security-audit.io>
Subject: Security Clearance Signed: Production Deployment Authorized
Date: Thu, 17 Sep 2026 12:45:00 +0000

Hi Infrastructure Leads,

I am pleased to confirm that the security clearance audit for the Q3 pipeline has been verified and fully approved. All staging clusters satisfy TLS 1.3 encryption and PII data sanitization requirements.

Please proceed with the production rollout at your convenience.

Best regards,
Elena Rostova
Lead Security Auditor`;

  await page.fill('#pasteContent', pastedEmail);
  await sleep(1500);
  await page.click('#submitPasteBtn');
  await page.waitForSelector('#resultsSection', { state: 'visible', timeout: 5000 });
  await sleep(3500);

  console.log('Finalizing video recording...');
  await page.close();
  await context.close();
  await browser.close();

  // Convert recorded video to demo.mp4
  const videoFiles = fs.readdirSync(tempVideoDir).filter(f => f.endsWith('.webm'));
  if (videoFiles.length > 0) {
    const latestVideo = path.join(tempVideoDir, videoFiles[videoFiles.length - 1]);
    const destMp4 = path.join(repoRoot, 'demo.mp4');

    let ffmpegPath = 'ffmpeg';
    const candidatePaths = [
      '/opt/homebrew/bin/ffmpeg',
      '/usr/local/bin/ffmpeg',
      '/Users/cl0rkster/Dev/ml/src/FtaaSService.Worker/.venv/lib/python3.12/site-packages/imageio_ffmpeg/binaries/ffmpeg-macos-x86_64-v7.1'
    ];
    for (const p of candidatePaths) {
      if (fs.existsSync(p)) {
        ffmpegPath = p;
        break;
      }
    }

    try {
      console.log(`Converting recording to web-optimized MP4 (H.264)...`);
      execSync(`"${ffmpegPath}" -y -i "${latestVideo}" -c:v libx264 -pix_fmt yuv420p -movflags +faststart "${destMp4}"`, { stdio: 'inherit' });
      const stats = fs.statSync(destMp4);
      console.log(`\n🎉 Demo video recorded and saved to: ${destMp4} (${(stats.size / (1024 * 1024)).toFixed(2)} MB)\n`);
    } catch (e) {
      console.warn('FFmpeg conversion error or not found. Kept raw recording:', e.message);
      fs.copyFileSync(latestVideo, destMp4.replace('.mp4', '.webm'));
    }

    fs.rmSync(tempVideoDir, { recursive: true, force: true });
  }
})();
