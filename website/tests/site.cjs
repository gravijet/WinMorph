// Run against the static site: NODE_PATH=/path/to/node_modules node website/tests/site.cjs
const assert = require('node:assert/strict');
const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {}) });
  try {
    for (const options of [
      { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 },
      { viewport: { width: 320, height: 780 }, deviceScaleFactor: 3, isMobile: true, hasTouch: true },
      { viewport: { width: 1280, height: 720 }, reducedMotion: 'reduce' },
    ]) {
      const context = await browser.newContext(options);
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('response', response => { if (response.status() >= 400) errors.push(`${response.status()}: ${response.url()}`); });
      await page.route('**/api/release', route => route.fulfill({ json: { version: '5.3.0', files: { setup: { size: 1572864, url: 'javascript:alert(1)' } } } }));
      await page.goto((process.env.WINMORPH_TEST_URL || 'http://127.0.0.1:18765') + '/?lang=de');
      await page.waitForFunction(() => document.querySelector('#screen img').naturalWidth > 0);
      assert.equal(await page.locator('.n-links').count(), 0);
      assert.equal(await page.getByText('Antivirus blocks the download?', { exact: false }).count(), 0);
      assert.equal(await page.getByText('Virenschutz blockiert den Download?', { exact: false }).count(), 0);
      assert.equal(await page.locator('#keys').count(), 0);
      for (const lang of ['en', 'de', 'en']) {
        await page.locator(`[data-lang=${lang}]`).click();
        await page.waitForTimeout(220);
        assert.equal(await page.locator('html').getAttribute('lang'), lang);
        assert.match(await page.title(), lang === 'de' ? /per Klick/ : /in one click/);
        const translated = await page.evaluate(lang => {
          const matches = (selector, attr, data) => [...document.querySelectorAll(selector)].every(el => lang === 'de' || (attr ? el.getAttribute(attr) : el.textContent) === el.dataset[data]);
          return matches('[data-en]', null, 'en') && matches('[data-en-alt]', 'alt', 'enAlt') && matches('[data-en-aria]', 'aria-label', 'enAria');
        }, lang);
        assert.ok(translated, 'Text, alternative text and accessible names follow the language');
        assert.ok(await page.locator('#chips button').evaluateAll((buttons, lang) => buttons.every(button => button.getAttribute('aria-label').startsWith(lang === 'de' ? 'Design ansehen: ' : 'View look: ')), lang), 'Look buttons keep translated names when their visual labels are hidden');
        assert.ok(await page.locator('#deck img').evaluateAll((images, lang) => images.every(im => im.src.includes(`/shots/${lang}/`)), lang));
        assert.match(await page.locator('#app img').getAttribute('src'), new RegExp(`/shots/${lang}/app.webp`));
        assert.equal(await page.locator('[data-dl]').first().getAttribute('href'), '/download/setup');
      }
      await page.evaluate(() => scrollTo({ top: 950, behavior: 'instant' }));
      await page.waitForTimeout(600);
      assert.ok(await page.locator('#look-name').textContent());
      await page.locator('#chips [data-id=xp] button').click();
      await page.waitForFunction(() => document.querySelector('#screen img.on[src$="/look-xp.webp"]'));
      if (options.viewport.width > 760 && !options.reducedMotion) {
        const transition = await page.waitForFunction(() => {
          const image = document.querySelector('#screen img.on[src$="/look-xp.webp"]');
          if (!image) return false;
          const style = getComputedStyle(image), progress = parseFloat(style.getPropertyValue('--m'));
          return progress > -37 && progress < 149 ? { mask: style.maskImage, transform: style.transform } : false;
        });
        const effect = await transition.jsonValue();
        assert.match(effect.mask, /linear-gradient/, 'Desktop look changes visibly sweep across the previous image');
        assert.notEqual(effect.transform, 'none');
      } else if (options.viewport.width <= 760) {
        assert.match(await page.locator('#screen img.on[src$="/look-xp.webp"]').evaluate(image => getComputedStyle(image).maskImage), /radial-gradient/, 'Mobile look transitions stay intact');
      }
      await page.locator('#walls').scrollIntoViewIfNeeded();
      await page.locator('.wall-picker.in').waitFor();
      await page.locator('.wall-picker').evaluate(picker => Promise.all(picker.getAnimations().map(animation => animation.finished)));
      const wallpaperLayout = await page.evaluate(() => {
        const stage = document.querySelector('#stage').getBoundingClientRect();
        const tiles = document.querySelector('#tiles').getBoundingClientRect();
        return { stage: { top: stage.top, bottom: stage.bottom, right: stage.right }, tiles: { top: tiles.top, bottom: tiles.bottom, left: tiles.left }, scroll: scrollY };
      });
      assert.ok(wallpaperLayout.stage.top >= 0 && wallpaperLayout.stage.bottom <= options.viewport.height, 'The wallpaper preview fits on screen');
      assert.ok(wallpaperLayout.tiles.top >= 0 && wallpaperLayout.tiles.bottom <= options.viewport.height, 'Wallpaper controls fit beside or below the preview');
      if (options.viewport.width > 760) assert.ok(wallpaperLayout.tiles.left >= wallpaperLayout.stage.right, 'Desktop preview and choices are side by side');
      for (const tile of await page.locator('#tiles button[data-pic]').all()) {
        await tile.evaluate(button => button.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' }));
        await tile.click();
        await page.waitForFunction(() => {
          const selected = document.querySelector('#tiles button[aria-pressed=true]');
          const expected = selected.dataset.pic.replace(/\/shots\/(de|en)\//, `/shots/${document.documentElement.lang}/`);
          return [...document.querySelectorAll('#stage-pics img.on')].some(im => im.src.includes(expected) && im.complete && im.naturalWidth > 0);
        });
        assert.ok(Math.abs(await page.evaluate(() => scrollY) - wallpaperLayout.scroll) < 2, `Choosing ${await tile.textContent()} does not move the page away from the preview (start ${wallpaperLayout.scroll}, now ${await page.evaluate(() => scrollY)})`);
      }
      await page.locator('[data-pic="/shots/de/live-zen-desk"]').click();
      for (const lang of ['de', 'en']) {
        await page.locator(`[data-lang=${lang}]`).click();
        await page.waitForFunction(lang => [...document.querySelectorAll('#stage-pics img.on')].some(image => image.src.includes(`/shots/${lang}/live-zen-desk`) && image.complete && image.naturalWidth > 0), lang);
      }
      await page.locator('[data-scene=matrix]').click();
      const sharpCanvas = await page.locator('#live-canvas').evaluate(canvas => canvas.width >= canvas.clientWidth * Math.min(devicePixelRatio, 3) - 1);
      assert.ok(sharpCanvas, 'Live canvas matches display density');
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No horizontal page overflow');
      await page.locator('#deck').scrollIntoViewIfNeeded();
      await page.locator('#deck').evaluate(deck => {
        deck.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
        deck.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      });
      await page.waitForTimeout(320);
      assert.equal(await page.locator('#deck img').nth(1).evaluate(image => image.style.zIndex), '8', 'Rapid key presses advance the deck once');
      await page.locator('#hacker').scrollIntoViewIfNeeded();
      await page.locator('#term-in').fill('help');
      await page.locator('#term-in').press('Enter');
      assert.match(await page.locator('#term-out').textContent(), /commands:/);
      await page.locator('#term-in').fill('__proto__');
      await page.locator('#term-in').press('Enter');
      assert.match(await page.locator('#term-out').textContent(), /command not found/);
      await page.locator('[data-lang=de]').click();
      await page.locator('#term-in').fill('whoami');
      await page.locator('#term-in').press('Enter');
      assert.match(await page.locator('#term-out').textContent(), /gast/);
      await page.locator('#download').scrollIntoViewIfNeeded();
      assert.match(await page.locator('#download').textContent(), /Strg\+Alt\+Umschalt\+F12/);
      await page.reload();
      assert.equal(await page.locator('html').getAttribute('lang'), 'de');
      await page.goto((process.env.WINMORPH_TEST_URL || 'http://127.0.0.1:18765') + '/404.html?lang=en');
      assert.match(await page.locator('main').textContent(), /This page does not exist/);
      assert.deepEqual(errors, []);
      console.log(`PASS ${options.viewport.width}px${options.reducedMotion ? ' reduced motion' : ''}: locales, images, controls, download and layout`);
      await context.close();
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
