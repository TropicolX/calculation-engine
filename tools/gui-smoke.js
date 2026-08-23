const { chromium } = require('playwright');

const OUT = process.env.SHOTS || '/tmp/shots';
const URL = 'http://127.0.0.1:5088/';

const cell = (a1) => `td.cell:has(input[aria-label="${a1}"]), td.cell`;

(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const page = await browser.newPage({ viewport: { width: 1600, height: 950 } });
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
  page.on('response', r => { if (r.status() >= 400) errors.push('http ' + r.status() + ' ' + r.url()); });

  await page.goto(URL, { waitUntil: 'networkidle' });
  await page.waitForSelector('table.grid', { timeout: 30000 });
  // Blazor Server needs its circuit before interactivity works.
  await page.waitForFunction(() => !!document.querySelector('td.cell input.cell-editor'), null, { timeout: 30000 });

  const readCell = (a1) => page.evaluate((addr) => {
    const tds = [...document.querySelectorAll('td.cell')];
    const td = tds.find(t => ((t.getAttribute("title") || "") === addr || (t.getAttribute("title") || "").startsWith(addr + " ")));
    if (!td) return null;
    const input = td.querySelector('input.cell-editor');
    return input ? input.value : td.innerText.trim();
  }, a1);

  const clickCell = async (a1) => {
    await page.evaluate((addr) => {
      const tds = [...document.querySelectorAll('td.cell')];
      const td = tds.find(t => ((t.getAttribute("title") || "") === addr || (t.getAttribute("title") || "").startsWith(addr + " ")));
      if (td) td.click();
    }, a1);
    await page.waitForTimeout(250);
  };

  const report = {};

  // ---- 0. the header row survived the first click --------------------------
  report.A1_header = await readCell('A1');
  await clickCell('C5');
  report.A1_after_first_click = await readCell('A1');

  // ---- 1. the sample sheet loaded and computed -----------------------------
  report.E2_total = await readCell('E2');
  report.F2_grade = await readCell('F2');
  report.B16_average = await readCell('B16');
  await page.screenshot({ path: `${OUT}/01-loaded.png` });

  // ---- 2. edit a mark and watch dependents move ---------------------------
  await clickCell('D2');
  await page.locator('input.cell-editor').fill('35');
  await page.locator('input.cell-editor').press('Enter');
  await page.waitForTimeout(700);
  report.after_edit_E2 = await readCell('E2');
  report.after_edit_F2 = await readCell('F2');
  report.after_edit_B16 = await readCell('B16');
  report.diagnostics = await page.locator('.stats').innerText().catch(() => '');
  await page.screenshot({ path: `${OUT}/02-edited.png` });

  // ---- 3. duplicates -------------------------------------------------------
  await page.getByRole('button', { name: 'Find duplicates' }).click();
  await page.waitForTimeout(600);
  report.duplicate_note = await page.locator('.panel:has-text("Duplicates") .panel-note').first().innerText().catch(() => '');
  report.duplicate_cells = await page.locator('td.duplicate').count();
  await page.screenshot({ path: `${OUT}/03-duplicates.png` });

  // ---- 4. circular reference ----------------------------------------------
  await page.getByRole('button', { name: 'Insert a circular reference' }).click();
  await page.waitForTimeout(600);
  report.circular_banner = await page.locator('.circular-banner').innerText().catch(() => '');
  report.circular_cells = await page.locator('td.circular').count();
  await page.screenshot({ path: `${OUT}/04-circular.png` });

  // ---- 5. a bad formula ----------------------------------------------------
  await clickCell('L6');
  await page.locator('input.cell-editor').fill('=SUM(B2:B13');
  await page.locator('input.cell-editor').press('Enter');
  await page.waitForTimeout(700);
  report.status_after_bad_formula = await page.locator('.status-text').innerText().catch(() => '');
  await page.screenshot({ path: `${OUT}/05-syntax-error.png` });

  // ---- 6. find & replace ---------------------------------------------------
  await page.locator('.panel:has-text("Find & Replace") input').first().fill('Adeyemi');
  await page.locator('.panel:has-text("Find & Replace") input').nth(1).fill('Adeyemi-Cole');
  await page.getByRole('button', { name: 'Find all' }).click();
  await page.waitForTimeout(500);
  report.find_note = await page.locator('.panel:has-text("Find & Replace") .panel-note').first().innerText().catch(() => '');
  await page.getByRole('button', { name: 'Replace all' }).click();
  await page.waitForTimeout(600);
  report.replace_note = await page.locator('.panel:has-text("Find & Replace") .panel-note').first().innerText().catch(() => '');
  report.B3_after_replace = await readCell('B3');
  await page.screenshot({ path: `${OUT}/06-find-replace.png` });

  // ---- 7. undo -------------------------------------------------------------
  await page.getByRole('button', { name: '↶ Undo' }).click();
  await page.waitForTimeout(600);
  report.B3_after_undo = await readCell('B3');

  report.errors = errors;
  console.log(JSON.stringify(report, null, 2));
  await browser.close();
})().catch(e => { console.error('FAILED', e); process.exit(1); });
