const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright-core');
const { runE2ETests } = require('./mailstripper.spec');

async function main() {
  const browserPath = fs.existsSync('/Applications/Brave Browser.app/Contents/MacOS/Brave Browser')
    ? '/Applications/Brave Browser.app/Contents/MacOS/Brave Browser'
    : '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';

  if (!fs.existsSync(browserPath)) {
    console.error(`Error: Browser not found at ${browserPath}`);
    process.exit(1);
  }

  console.log(`Using browser: ${browserPath}`);
  const browser = await chromium.launch({
    executablePath: browserPath,
    headless: true,
    args: ['--no-sandbox', '--disable-gpu']
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 860 }
  });

  const page = await context.newPage();

  try {
    await runE2ETests(page, 'http://localhost:5001');
  } catch (err) {
    console.error('\n❌ E2E Test execution failed:', err);
    process.exitCode = 1;
  } finally {
    await page.close();
    await context.close();
    await browser.close();
  }
}

main();
