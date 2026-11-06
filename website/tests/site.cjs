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
      assert.doesNotMatch(await page.locator('.foot').textContent(), /Benjamin Berger/);
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
        assert.match(await page.locator('#app img').getAttribute('src'), new RegExp(`/shots/${lang}/app.webp`));
        assert.equal(await page.locator('[data-dl]').first().getAttribute('href'), '/download/setup');
      }
      await page.evaluate(() => scrollTo({ top: 950, behavior: 'instant' }));
      await page.waitForTimeout(600);
      assert.ok(await page.locator('#look-name').textContent());
      await page.locator('#chips [data-id=xp] button').click();
      await page.waitForFunction(() => document.querySelector('#screen img.on[src$="/look-xp.webp"]'));
      if (options.viewport.width > 760 && !options.reducedMotion) {
        await page.waitForFunction(() => document.querySelector('#screen img.on[src$="/look-xp.webp"]').getAnimations().some(a => a.playState === 'running'));
        const effect = await page.locator('#screen img.on[src$="/look-xp.webp"]').evaluate(image => {
          const animation = image.getAnimations()[0];
          animation.pause(); animation.currentTime = 625;
          const style = getComputedStyle(image);
          return { clip: style.clipPath, transform: style.transform };
        });
        assert.match(effect.clip, /polygon/, 'Desktop reveals part of the new image over the previous design');
        assert.notEqual(effect.transform, 'none');
        await page.evaluate(() => {
          const morph = document.querySelector('#top'), hold = innerHeight * 0.6;
          scrollTo({ top: morph.offsetTop + hold + ((2 + 0.8) / 15) * (morph.offsetHeight - innerHeight - hold), behavior: 'instant' });
        });
        await page.waitForTimeout(200);
        assert.equal(await page.locator('#screen img.on[src$="/look-xp-menu.webp"]').count(), 0, 'The Start menu does not cover an unfinished design transition');
        await page.locator('#screen img.on[src$="/look-xp.webp"]').evaluate(image => image.getAnimations()[0].play());
        await page.waitForFunction(() => document.querySelector('#screen img.on[src$="/look-xp-menu.webp"]'));
      } else if (options.viewport.width <= 760) {
        assert.match(await page.locator('#screen img.on[src$="/look-xp.webp"]').evaluate(image => getComputedStyle(image).maskImage), /radial-gradient/, 'Mobile look transitions stay intact');
      }
      for (const id of ['win95', 'ubuntu', 'win10']) await page.locator(`#chips [data-id=${id}] button`).click();
      await page.waitForFunction(() => document.querySelector('#screen img.on[src$="/look-win10.webp"]'));
      assert.equal(await page.locator('#chips [data-id=win10] button').getAttribute('aria-pressed'), 'true', 'Rapid choices keep the last requested look');
      await page.locator('#walls').scrollIntoViewIfNeeded();
      await page.locator('.wall-picker.in').waitFor();
      await page.locator('.wall-picker').evaluate(picker => Promise.all(picker.getAnimations().map(animation => animation.finished)));
      const wallpaperLayout = await page.evaluate(() => {
        const stage = document.querySelector('#stage').getBoundingClientRect();
        const tiles = document.querySelector('#tiles').getBoundingClientRect();
        const caption = document.querySelector('#stage-cap').getBoundingClientRect();
        return { stage: { top: stage.top, bottom: stage.bottom, right: stage.right }, tiles: { top: tiles.top, bottom: tiles.bottom, left: tiles.left }, caption: { top: caption.top, bottom: caption.bottom }, scroll: scrollY };
      });
      assert.ok(wallpaperLayout.stage.top >= 0 && wallpaperLayout.stage.bottom <= options.viewport.height, 'The wallpaper preview fits on screen');
      assert.ok(wallpaperLayout.tiles.top >= 0 && wallpaperLayout.tiles.bottom <= options.viewport.height, 'Wallpaper controls fit beside or below the preview');
      assert.ok(wallpaperLayout.caption.top >= wallpaperLayout.stage.bottom && wallpaperLayout.caption.bottom <= options.viewport.height, 'The wallpaper caption stays visible without covering the picture');
      if (options.viewport.width > 760) assert.ok(wallpaperLayout.tiles.left >= wallpaperLayout.stage.right, 'Desktop preview and choices are side by side');
      for (const [filter, count] of [['live', 7], ['designs', 16]]) {
        await page.locator(`[data-wall-filter=${filter}]`).click();
        assert.equal(await page.locator('#tiles button:visible').count(), count, 'Wallpaper categories show only their own choices');
        for (const tile of await page.locator('#tiles button[data-pic]:visible').all()) {
          await tile.evaluate(button => button.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' }));
          await tile.click();
          await page.waitForFunction(() => {
            const selected = document.querySelector('#tiles button[aria-pressed=true]');
            const expected = selected.dataset.pic.replace(/\/shots\/(de|en)\//, `/shots/${document.documentElement.lang}/`);
            return [...document.querySelectorAll('#stage-pics img.on')].some(im => im.src.includes(expected) && im.complete && im.naturalWidth > 0);
          });
          assert.ok(Math.abs(await page.evaluate(() => scrollY) - wallpaperLayout.scroll) < 2, `Choosing ${await tile.textContent()} does not move the page away from the preview (start ${wallpaperLayout.scroll}, now ${await page.evaluate(() => scrollY)})`);
        }
      }
      await page.locator('[data-wall-filter=live]').click();
      await page.locator('[data-pic="/shots/de/live-zen-desk"]').click();
      for (const lang of ['de', 'en']) {
        await page.locator(`[data-lang=${lang}]`).click();
        await page.waitForFunction(lang => [...document.querySelectorAll('#stage-pics img.on')].some(image => image.src.includes(`/shots/${lang}/live-zen-desk`) && image.complete && image.naturalWidth > 0), lang);
      }
      const chooserPromise = page.waitForEvent('filechooser');
      await page.locator('#wall-own').click();
      const chooser = await chooserPromise;
      await chooser.setFiles([]);
      assert.match(await page.locator('#stage-cap').textContent(), /stays on your device/);
      await page.locator('[data-scene=matrix]').click();
      const sharpCanvas = await page.locator('#live-canvas').evaluate(canvas => canvas.width >= canvas.clientWidth * Math.min(devicePixelRatio, 3) - 1);
      assert.ok(sharpCanvas, 'Live canvas matches display density');
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No horizontal page overflow');
      assert.equal(await page.locator('#lock, #deck').count(), 0);
      await page.locator('[data-wall-filter=designs]').click();
      for (const direction of [1, -1]) {
        await page.evaluate(top => scrollTo({ top, behavior: 'instant' }), wallpaperLayout.scroll);
        await page.locator('#tiles').evaluate((tiles, direction) => {
          tiles.scrollTop = direction > 0 ? tiles.scrollHeight : 0;
          tiles.scrollLeft = direction > 0 ? tiles.scrollWidth : 0;
        }, direction);
        const panel = await page.locator('#tiles').boundingBox();
        await page.mouse.move(panel.x + panel.width / 2, panel.y + panel.height / 2);
        const before = await page.evaluate(() => scrollY);
        await page.mouse.wheel(0, direction * 360);
        await page.waitForTimeout(100);
        await page.mouse.wheel(0, direction * 360);
        await page.waitForFunction(({ before, direction }) => direction * (scrollY - before) > 20, { before, direction });
      }
      await page.locator('#playground').scrollIntoViewIfNeeded();
      assert.ok(await page.locator('#term').evaluate(el => Math.abs(el.getBoundingClientRect().left + el.clientWidth / 2 - innerWidth / 2) < 2), 'The terminal is centered');
      await page.locator('#term-in').fill('help');
      await page.locator('#term-in').press('Enter');
      assert.match(await page.locator('#term-out').textContent(), /commands:/);
      await page.locator('#term-in').fill('__proto__');
      await page.locator('#term-in').press('Enter');
      assert.match(await page.locator('#term-out').textContent(), /command not found/);
      const text = await page.locator('.play-hint').textContent();
      await page.locator('.play-hint').evaluate(el => {
        document.activeElement.blur();
        const range = document.createRange(); range.selectNodeContents(el);
        getSelection().removeAllRanges(); getSelection().addRange(range);
      });
      if (options.isMobile) await page.locator('.selection-move').tap();
      else await page.locator('.selection-move').click();
      const note = page.locator('.play-text');
      assert.equal(await note.locator('span').textContent(), text);
      const start = await note.boundingBox();
      if (options.isMobile) {
        const touch = await context.newCDPSession(page);
        await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: start.x + 20, y: start.y + 20 }] });
        await touch.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: start.x + 70, y: start.y + 50 }] });
        await touch.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        await touch.detach();
      } else {
        await page.mouse.move(start.x + 20, start.y + 20);
        await page.mouse.down();
        await page.mouse.move(start.x + 70, start.y + 50, { steps: 8 });
        await page.mouse.up();
      }
      const moved = await note.boundingBox();
      assert.ok(Math.abs(moved.y - start.y) > 20, 'Text stays where it was dragged');
      await note.focus();
      const oldTop = await note.evaluate(el => el.offsetTop);
      await note.press('ArrowDown');
      await page.waitForFunction(top => document.querySelector('.play-text').offsetTop === top + 5, oldTop);
      await page.locator('[data-lang=de]').click();
      assert.equal(await note.locator('button').getAttribute('aria-label'), 'Text entfernen');
      assert.equal(await note.locator('span').textContent(), text, 'User text is preserved when changing language');
      await note.locator('button').click();
      assert.equal(await page.locator('.play-text').count(), 0);
      await page.locator('.play-hint').evaluate(el => {
        const data = new DataTransfer(); data.setData('text/plain', '<img src=x onerror=alert(1)>');
        el.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: data, clientX: 80, clientY: 180 }));
      });
      assert.equal(await page.locator('.play-text span').textContent(), '<img src=x onerror=alert(1)>');
      assert.equal(await page.locator('.play-text img').count(), 0, 'Dropped text never becomes executable HTML');
      await page.locator('.play-text').press('Delete');
      assert.equal(await page.locator('.play-text').count(), 0);
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
    for (const [width, height] of [[1440, 900], [1280, 720], [1024, 768], [768, 1024], [390, 844], [844, 390], [568, 320], [320, 480], [320, 568]]) {
      const context = await browser.newContext({ viewport: { width, height }, reducedMotion: 'reduce' });
      const page = await context.newPage();
      await page.route('**/api/release', route => route.fulfill({ json: { version: '5.3.0' } }));
      await page.goto((process.env.WINMORPH_TEST_URL || 'http://127.0.0.1:18765') + '/?lang=de');
      await page.evaluate(() => scrollTo({ top: innerHeight, behavior: 'instant' }));
      for (const lang of ['de', 'en']) {
        await page.locator(`[data-lang=${lang}]`).click();
        for (const button of await page.locator('#chips button').all()) {
          await button.click();
          const name = await button.textContent();
          await page.waitForFunction(name => document.querySelector('#look-name').textContent === name, name);
          const fits = await page.evaluate(() => {
            const rect = selector => document.querySelector(selector).getBoundingClientRect();
            const image = rect('#screen'), title = rect('#look-name'), desc = rect('#look-desc'), rail = rect('#chips');
            return title.bottom <= image.top + 1 &&
              (title.right <= desc.left || title.bottom <= desc.top || title.top >= desc.bottom) &&
              (desc.bottom <= image.top || desc.top >= image.bottom) &&
              image.bottom < rail.top && rail.bottom <= innerHeight;
          });
          assert.ok(fits, `${width}x${height} ${lang} ${name}: title, description, preview and controls do not overlap`);
        }
      }
      for (const id of ['walls', 'playground', 'download']) {
        await page.locator('#' + id).scrollIntoViewIfNeeded();
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth && [...document.querySelectorAll('h2')].every(h => h.getBoundingClientRect().right <= innerWidth + 1 && h.scrollWidth <= h.clientWidth + 1)), `${width}px: sections and headings stay within the screen`);
      }
      console.log(`PASS ${width}x${height}: all 15 looks in DE/EN, no overlapping or overflowing sections`);
      await context.close();
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
