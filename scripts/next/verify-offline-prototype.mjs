import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';

const prototype = new URL('../../docs/next/prototype/Snoop_Next_Interactive_Prototype_v1.1.html', import.meta.url);
const output = resolve(process.argv[2] ?? 'output/next-prototype-previews');
await mkdir(output, { recursive: true });
const browser = await chromium.launch(process.env.CHROME_PATH
    ? { executablePath: process.env.CHROME_PATH }
    : { channel: 'msedge' });

try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light', offline: true });
    const network = [];
    await context.route(/^https?:\/\//i, route => {
        network.push(route.request().url());
        return route.abort();
    });
    const page = await context.newPage();
    page.setDefaultTimeout(10_000);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const capture = name => page.screenshot({ path: resolve(output, name + '.png'), animations: 'disabled' });

    await page.goto(prototype.href);
    assert.equal(page.url(), prototype.href);
    const processes = page.locator('[data-action="attach"]');
    assert.deepEqual((await processes.evaluateAll(rows => rows.map(row => row.dataset.id))).sort(), ['legacy', 'restricted', 'sample']);
    for (const name of ['SampleApp.exe', 'LegacyOrder.exe', 'AdminConsole.exe']) {
        await page.getByText(name, { exact: true }).waitFor();
    }
    await page.getByText('PID and runtime values are fictional.', { exact: false }).waitFor();

    // P-04. Dismiss the simulated permission error, reset the failure and attach to a different target.
    await page.locator('[data-action="attach"][data-id="restricted"]').press('Enter');
    const errorDialog = page.getByRole('dialog');
    await errorDialog.waitFor();
    assert.match(await errorDialog.innerText(), /access denied/i);
    await capture('attach-error-1440x900');
    await page.keyboard.press('Escape');
    await errorDialog.waitFor({ state: 'hidden' });
    const retry = page.locator('[data-action="reset-connection"]');
    await retry.click();
    await retry.waitFor({ state: 'hidden' });
    await page.locator('[data-action="attach"][data-id="sample"]').click();
    const selected = page.getByRole('treeitem', { selected: true });
    await selected.waitFor();
    assert.equal(await selected.getAttribute('data-id'), 'SaveButton');
    assert.match(await page.locator('footer').innerText(), /SampleApp\.exe.*41920/);
    await page.locator('.toast').waitFor({ state: 'hidden' });
    assert.equal(await page.locator('body').evaluate(el => el.classList.contains('dark')), false);
    await capture('workbench-light-1440x900');

    await page.locator('[data-action="view-settings"]').click();
    await page.locator('[data-field="theme"]').selectOption('dark');
    await page.locator('body.dark').waitFor();
    await page.locator('[data-action="view"][data-view="inspector"]').click();
    await page.setViewportSize({ width: 1280, height: 720 });
    await selected.waitFor();
    assert.equal(await selected.getAttribute('data-id'), 'SaveButton');
    const panes = await page.locator('.workspace > :is(.tree-area, .main-area, .inspector-area)').evaluateAll(elements =>
        elements.map(element => {
            const { left, right, top, bottom } = element.getBoundingClientRect();
            return { left, right, top, bottom };
        }));
    assert.equal(panes.length, 3, 'Expected tree, preview and properties at minimum size');
    panes.forEach((pane, index) => {
        assert.ok(pane.left >= 0 && pane.right <= 1280 && pane.top >= 0 && pane.bottom <= 720
            && pane.right > pane.left && pane.bottom > pane.top, 'Pane outside viewport: ' + JSON.stringify(pane));
        if (index > 0) assert.ok(panes[index - 1].right <= pane.left, 'Workbench panes overlap');
    });
    await capture('workbench-dark-1280x720');

    // D-06. Start from a real field so the Escape check verifies keyboard focus restoration.
    const treeSearch = page.locator('[data-field="treeQuery"]');
    await treeSearch.focus();
    await page.keyboard.press('Control+k');
    const commandQuery = page.locator('[data-field="commandQuery"]');
    await commandQuery.waitFor();
    assert.equal(await commandQuery.evaluate(el => el.matches(':focus')), true);
    async function typeCommandCharacter(character, expectedText, expectedCaret) {
        await page.keyboard.type(character);
        // Allow the prototype's input handler to redraw the command field between keystrokes.
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)));
        const input = await commandQuery.evaluate(el => [el.value, el.selectionStart, el.selectionEnd]);
        assert.deepEqual(input, [expectedText, expectedCaret, expectedCaret], 'Command palette lost text or caret after ' + character);
    }
    await typeCommandCharacter('M', 'M', 1);
    await typeCommandCharacter('P', 'MP', 2);
    await page.keyboard.press('ArrowLeft');
    await typeCommandCharacter('C', 'MCP', 2);
    const results = page.locator('[data-action="command-execute"]');
    await page.waitForFunction(() => document.querySelectorAll('[data-action="command-execute"]').length === 1);
    assert.equal(await results.getAttribute('data-id'), 'mcp');
    await capture('command-keyboard-1280x720');
    await page.keyboard.press('Escape');
    await commandQuery.waitFor({ state: 'hidden' });
    assert.equal(await treeSearch.evaluate(el => el.matches(':focus')), true);

    assert.deepEqual(network, [], 'The offline prototype attempted a network request');
    assert.deepEqual(errors, [], 'The prototype reported a JavaScript error');
    const hash = createHash('sha256').update(await readFile(prototype)).digest('hex');
    console.log('PASS: Chromium ' + browser.version() + ' opened the local HTML offline (SHA-256 ' + hash + ').');
    console.log('PASS: simulated process selection, error recovery, light/dark layouts, resize and keyboard focus.');
} finally {
    await browser.close();
}
