(function () {
  "use strict";
  var root = document.documentElement;
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var still = window.matchMedia && matchMedia("(prefers-reduced-motion: reduce)").matches;
  var clamp01 = function (v) { return Math.min(1, Math.max(0, v)); };
  var out = function (t) { return 1 - Math.pow(1 - t, 3); };
  var inout = function (t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; };
  var lang = root.getAttribute("lang") === "en" ? "en" : "de";
  var L = function (de, en) { return lang === "en" ? en : de; };
  var swap = function (el, text) {
    clearTimeout(el.swapTimer);
    var enter = function () {
      if (text != null) el.textContent = text;
      el.classList.remove("leaving", "swap");
      if (!still) { void el.offsetWidth; el.classList.add("swap"); }
    };
    if (!still && text != null && el.textContent && el.textContent !== text) {
      el.classList.add("leaving");
      el.swapTimer = setTimeout(enter, 160);
    } else enter();
  };

  // ---- language ------------------------------------------------------------------------------
  var texts = new Map(), alts = new Map(), arias = new Map();
  $$("[data-en]").forEach(function (el) { texts.set(el, el.textContent); });
  $$("[data-en-alt]").forEach(function (el) { alts.set(el, el.getAttribute("alt")); });
  $$("[data-en-aria]").forEach(function (el) { arias.set(el, el.getAttribute("aria-label")); });
  var meta = {
    de: ["WinMorph – Windows-Looks per Klick", "WinMorph legt ein eigenes Design über Windows 10 und 11: Taskleiste, Startmenü, Alt+Tab, Hintergrund und Sperrbildschirm. 15 Looks, kostenlos, mit einem Klick zurück."],
    en: ["WinMorph – Windows looks in one click", "WinMorph lays a design of its own over Windows 10 and 11: taskbar, Start menu, Alt+Tab, wallpaper and lock screen. 15 looks, free, one click back."],
  };
  var onLang = [];
  function localized(src) { return src.replace(/\/shots\/(?:de|en)\//g, "/shots/" + lang + "/"); }
  function localizePictures() {
    $$("img").forEach(function (im) {
      ["src", "srcset"].forEach(function (attr) {
        var value = im.getAttribute(attr);
        if (value && /\/shots\/(de|en)\//.test(value)) im.setAttribute(attr, localized(value));
      });
    });
  }
  function applyLang(l) {
    lang = l;
    root.setAttribute("lang", l);
    texts.forEach(function (de, el) { el.textContent = l === "en" ? el.dataset.en : de; });
    alts.forEach(function (de, el) { el.setAttribute("alt", l === "en" ? el.dataset.enAlt : de); });
    arias.forEach(function (de, el) { el.setAttribute("aria-label", l === "en" ? el.dataset.enAria : de); });
    $$("[data-src-en]").forEach(function (img) {
      if (!img.dataset.srcDe) img.dataset.srcDe = img.getAttribute("src");
      img.setAttribute("src", l === "en" ? img.dataset.srcEn : img.dataset.srcDe);
    });
    $$(".n-lang [data-lang]").forEach(function (b) { b.setAttribute("aria-pressed", String(b.dataset.lang === l)); });
    document.title = meta[l][0];
    $('meta[name="description"]').content = meta[l][1];
    $('meta[property="og:title"]').content = meta[l][0];
    $('meta[property="og:description"]').content = meta[l][1];
    localizePictures();
    onLang.forEach(function (f) { f(); });
  }
  $$(".n-lang [data-lang]").forEach(function (b) {
    b.addEventListener("click", function () {
      try { localStorage.setItem("wm-lang", b.dataset.lang); } catch (e) {}
      var url = new URL(location.href);
      url.searchParams.set("lang", b.dataset.lang);
      history.replaceState(null, "", url);
      applyLang(b.dataset.lang);
    });
  });

  // ---- newest version -----------------------------------------------------------------------------
  var rel = null;
  function render() {
    if (!rel || !/^\d+\.\d+\.\d+$/.test(rel.version)) return;
    var s = (rel.files || {}).setup;
    var parts = ["Version " + rel.version];
    if (s && s.size) parts.push((s.size / 1048576).toFixed(1).replace(".", lang === "de" ? "," : ".") + " MB");
    $("#dl-info").textContent = parts.join(" · ");
    if (s && s.url === "/download/setup") $$("[data-dl]").forEach(function (a) { a.setAttribute("href", s.url); });
  }
  onLang.push(render);
  try { rel = JSON.parse(localStorage.getItem("wm-rel") || "null"); } catch (e) {}
  fetch("/api/release", { headers: { accept: "application/json" } })
    .then(function (r) { if (!r.ok) throw 0; return r.json(); })
    .then(function (r) { if (!r.version) return; rel = r; render(); try { localStorage.setItem("wm-rel", JSON.stringify(r)); } catch (e) {} })
    .catch(function () {});

  // ---- one frame loop -----------------------------------------------------------------------------
  var view = { w: innerWidth, h: innerHeight }, ticks = [];
  var pointer = { x: 0, y: 0, sx: 0, sy: 0, cx: -1, cy: -1 };
  addEventListener("resize", function () { view.w = innerWidth; view.h = innerHeight; }, { passive: true });
  addEventListener("pointermove", function (e) {
    pointer.cx = e.clientX; pointer.cy = e.clientY;
    if (e.pointerType === "mouse") { pointer.x = e.clientX / view.w - 0.5; pointer.y = e.clientY / view.h - 0.5; }
  }, { passive: true });
  var last = 0;
  function frame(t) {
    var dt = Math.min(0.1, (t - (last || t)) / 1000); last = t;
    var k = 1 - Math.exp(-dt * 3);
    pointer.sx += (pointer.x - pointer.sx) * k; pointer.sy += (pointer.y - pointer.sy) * k;
    for (var i = 0; i < ticks.length; i++) ticks[i](dt);
    requestAnimationFrame(frame);
  }

  // ---- the look: the colours of the page follow it -------------------------------------------------
  var looks = $$("#chips li").map(function (li) {
    return { li: li, id: li.dataset.id, name: $("b", li).textContent, c: li.dataset.c.split(","), desc: $("span", li) };
  });
  var n = looks.length, byId = {};
  looks.forEach(function (l, i) { byId[l.id] = i; });
  var extra = { win11: ["#3b82f6", "#0b1f4a"] };
  var pick = "terminal", wallLook = "terminal", current = null;
  function setLook(id) {
    if (id === current) return;
    current = id;
    var c = byId[id] != null ? looks[byId[id]].c : extra[id];
    root.style.setProperty("--c1", c[0]); root.style.setProperty("--c2", c[1]);
  }

  // ---- the desktop that becomes a screen ----------------------------------------------------------------
  var morph = $("#top"), pin = $(".morph-pin"), screen = $("#screen"), hero = $("#hero"), wordmark = $("#wordmark");
  var nameEl = $("#look-name"), descEl = $("#look-desc");
  var imgs = { terminal: $("img", screen) }, z = 1, shownKey = "terminal", wantKey = "terminal", shownLook = -1, lookAnimation = null;
  var pictureSizes = "max(100vw, 178svh)";
  imgs.terminal.style.zIndex = 1;
  function img(key) {
    if (!imgs[key]) {
      var im = new Image();
      im.decoding = "async"; im.alt = L("Design-Vorschau: ", "Look preview: ") + looks[byId[key.replace("-menu", "")]].name;
      im.sizes = pictureSizes;
      im.srcset = "/shots/" + lang + "/look-" + key + "-sm.webp 800w, /shots/" + lang + "/look-" + key + ".webp 1600w, /shots/" + lang + "/look-" + key + "-2x.webp 3200w";
      im.src = "/shots/" + lang + "/look-" + key + ".webp";
      screen.appendChild(im);
      imgs[key] = im;
    }
    return imgs[key];
  }
  var base = function (key) { return key.replace("-menu", ""); };
  // looks reveal with a soft edge; the Start menu of the same look fades in
  function showKey(key) {
    wantKey = key;
    // The menu must not cover a design before its reveal has finished.
    if (base(key) === base(shownKey) && lookAnimation && (lookAnimation.playState === "running" || lookAnimation.playState === "paused")) return;
    if (key === shownKey) return;
    var im = img(key);
    var go = function () {
      if (wantKey !== key || shownKey === key) return;
      var fade = base(key) === base(shownKey), myZ = ++z;
      im.classList.add("off"); im.classList.toggle("fade", fade); im.classList.remove("on");
      im.style.zIndex = myZ;
      void im.offsetWidth;
      im.classList.remove("off"); im.classList.add("on");
      shownKey = key;
      if (lookAnimation) lookAnimation.cancel();
      screen.classList.remove("changing");
      if (!fade && !still && view.w > 760) {
        lookAnimation = im.animate([
          { clipPath: "polygon(0 0, 0 0, -20% 100%, 0 100%)", transform: "scale(1.05)" },
          { clipPath: "polygon(0 0, 120% 0, 100% 100%, 0 100%)", transform: "none" },
        ], { duration: 1250, easing: "cubic-bezier(0.65, 0, 0.25, 1)" });
        void screen.offsetWidth;
        screen.classList.add("changing");
      }
      // once it covers everything, the pictures below are put away
      function tidy() {
        if (+im.style.zIndex !== myZ || shownKey !== key) return;
        Object.keys(imgs).forEach(function (k) {
          var x = imgs[k];
          if (x !== im && x.classList.contains("on") && +x.style.zIndex < myZ) { x.classList.add("off"); x.classList.remove("on", "fade"); }
        });
        var at = byId[base(shownKey)];
        Object.keys(imgs).forEach(function (k) {
          if (k !== wantKey && Math.abs(byId[base(k)] - at) > 1) { imgs[k].remove(); delete imgs[k]; }
        });
      }
      if (!fade && !still && view.w > 760) {
        lookAnimation.onfinish = function () { screen.classList.remove("changing"); tidy(); };
      } else setTimeout(tidy, still ? 0 : fade ? 650 : 1400);
    };
    if (im.complete && im.naturalWidth) go(); else im.addEventListener("load", go, { once: true });
  }
  function showLook(i) {
    if (i === shownLook) return;
    shownLook = i;
    var l = looks[i];
    looks.forEach(function (x, j) {
      x.li.classList.toggle("on", j === i);
      $("button", x.li).setAttribute("aria-pressed", String(j === i));
    });
    swap(nameEl, l.name);
    swap(descEl, l.desc.textContent);
    var rail = l.li.parentNode;
    rail.scrollTo({ left: l.li.offsetLeft - (rail.clientWidth - l.li.clientWidth) / 2, behavior: still ? "instant" : "smooth" });
  }
  onLang.push(function () {
    if (shownLook >= 0) swap(descEl, looks[shownLook].desc.textContent);
    looks.forEach(function (look) { $("button", look.li).setAttribute("aria-label", L("Design ansehen: ", "View look: ") + look.name); });
    Object.keys(imgs).forEach(function (key) { imgs[key].alt = L("Design-Vorschau: ", "Look preview: ") + looks[byId[key.replace("-menu", "")]].name; });
  });

  // the progress bar: looks before the current one are full, the current one fills up
  var progP = [];
  function progress(i, f) {
    for (var j = 0; j < n; j++) {
      var v = j < i ? 1 : j === i ? Math.round(f * 1000) / 1000 : 0;
      if (progP[j] !== v) { progP[j] = v; looks[j].li.style.setProperty("--p", v); }
    }
  }

  // on the first screen the desktop changes its look every few seconds, the wordmark with it
  var HERO = 3.4, heroAt = 0, heroT = 0, inHero = true;
  function heroGo(i) {
    var id = looks[i].id, im = img(id);
    var go = function () {
      if (!inHero) return;
      heroAt = i; heroT = 0;
      showKey(id);
      wordmark.dataset.look = id;
      swap(wordmark);
      img(looks[(i + 1) % n].id);
    };
    if (im.complete && im.naturalWidth) { go(); return true; }
    im.addEventListener("load", go, { once: true });
    return false;
  }
  img(looks[1].id);

  var hold = function () { return view.h * 0.6; };
  function range() { return morph.offsetHeight - view.h; }
  looks.forEach(function (l, i) {
    $("button", l.li).addEventListener("click", function () {
      if (inHero) { heroGo(i); return; }
      var top = morph.offsetTop + hold() + ((i + 0.3) / n) * (range() - hold());
      // The selected design animates directly, without flashing every design on the way.
      scrollTo({ top: top, behavior: "instant" });
    });
  });

  ticks.push(function (dt) {
    var r = morph.getBoundingClientRect();
    if (r.bottom < 0) return;
    var y = -r.top, k = clamp01(y / hold()), e = inout(k);
    var W = screen.offsetWidth, H = W * 9 / 16;
    // full screen it is zoomed in a little, so the bars of the picture stay out from under the navigation
    var cover = Math.max(view.w / W, view.h / H) * 1.07;
    var s = cover + (1 - cover) * e;
    var sizes = Math.ceil(W * s / 50) * 50 + "px";
    if (sizes !== pictureSizes) {
      pictureSizes = sizes;
      Object.keys(imgs).forEach(function (key) { imgs[key].sizes = sizes; });
    }
    var dy = (view.h * 0.5 - screen.offsetTop) * (1 - e);
    var tilt = e * (still ? 0 : 1);
    screen.style.transform = "translate(-50%,-50%) translate3d(0," + dy.toFixed(1) + "px,0) perspective(1600px) rotateX(" + (-pointer.sy * 5 * tilt).toFixed(2) + "deg) rotateY(" + (pointer.sx * 7 * tilt).toFixed(2) + "deg) scale(" + s.toFixed(4) + ")";
    screen.style.setProperty("--r", (14 * e / s).toFixed(2) + "px");
    screen.style.setProperty("--veil", (1 - e).toFixed(3));
    hero.style.opacity = (1 - clamp01(k * 1.7)).toFixed(3);
    hero.style.transform = "translateY(" + (-k * 70).toFixed(1) + "px)";
    hero.style.pointerEvents = k > 0.4 ? "none" : "";
    pin.style.setProperty("--after", clamp01((k - 0.55) / 0.45).toFixed(3));

    inHero = y < view.h * 0.04;
    if (inHero) {
      if (!still && !document.hidden) {
        heroT = Math.min(HERO, heroT + dt);
        if (heroT >= HERO) heroGo((heroAt + 1) % n);
      }
      showLook(heroAt);
      progress(heroAt, still ? 0 : heroT / HERO);
      return;
    }
    // the looks, one after another, each with its Start menu opening in the second half
    var p = clamp01((y - hold()) / (range() - hold()));
    var f = Math.min(n - 1e-6, p * n), i = Math.floor(f), l = looks[i];
    showLook(i);
    showKey(l.id + (f - i > 0.55 ? "-menu" : ""));
    progress(i, f - i);
    pick = l.id; heroAt = i; heroT = 0;
    if (wordmark.dataset.look !== l.id && k > 0.6) wordmark.dataset.look = l.id;
    [i - 1, i + 1].forEach(function (j) { if (j >= 0 && j < n) { img(looks[j].id); img(looks[j].id + "-menu"); } });
    img(l.id + "-menu");
  });

  // ---- page colours follow the visible section ---------------------------------------------------
  var nav = $("#nav"), parts = $$("main > section");
  ticks.push(function () {
    var mid = view.h / 2, sec = null;
    for (var i = 0; i < parts.length; i++) {
      var r = parts[i].getBoundingClientRect();
      if (r.top < mid && r.bottom > mid) { sec = parts[i]; break; }
    }
    var id = !sec ? null : sec === morph ? (shownLook >= 0 ? looks[shownLook].id : null) : sec.dataset.look;
    if (id === "pick") id = pick;
    if (id === "wall") id = wallLook;
    if (id) setLook(id);
    nav.classList.toggle("solid", scrollY > view.h * 0.3);
  });

  // ---- terminal ----------------------------------------------------------------------------------------
  (function terminal() {
    var term = $("#term"), outEl = $("#term-out"), input = $("#term-in"), section = $("#hacker");
    var colors = { gruen: "#00ff41", green: "#00ff41", cyan: "#00e5ff", bernstein: "#ffb000", amber: "#ffb000", rot: "#ff3b3b", red: "#ff3b3b", violett: "#b48cff", violet: "#b48cff", weiss: "#e8e8e8", white: "#e8e8e8" };
    var color = "#00ff41", history = [], hi = 0;
    var names = ["help", "neofetch", "looks", "look", "color", "matrix", "date", "whoami", "echo", "clear", "download"];
    function line(text, cls) {
      var d = document.createElement("div");
      if (cls) d.className = cls;
      d.textContent = text;
      outEl.appendChild(d);
      outEl.scrollTop = outEl.scrollHeight;
    }
    function setColor(c) { color = c; term.style.setProperty("--tc", c); section.style.setProperty("--tc", c); }
    var run = {
      help: function () {
        line(L("Befehle:", "commands:"), "dim");
        line("  neofetch           " + L("System", "system"));
        line("  looks              " + L("alle Looks", "all looks"));
        line("  look <name>        " + L("zum Look springen", "jump to a look"));
        line("  color <" + L("grün|cyan|bernstein|rot|violett|weiß", "green|cyan|amber|red|violet|white") + ">");
        line("  matrix · date · whoami · echo · clear · download");
      },
      neofetch: function () {
        var v = rel ? rel.version : L("Vorschau", "preview");
        [
          [" __      __ __  __   ", L("gast", "guest") + "@winmorph"],
          [" \\ \\    / /|  \\/  |  ", "─────────────"],
          ["  \\ \\/\\/ / | |\\/| |  ", L("system  WinMorph ", "os      WinMorph ") + v],
          ["   \\_/\\_/  |_|  |_|  ", "shell   winmorph-sh"],
          ["                      ", L("designs ", "looks   ") + looks.length],
          ["                      ", L("design  hacker ", "theme   hacker ") + color],
        ].forEach(function (r) {
          var d = document.createElement("div"), a = document.createElement("span"), b = document.createElement("span");
          d.className = "pre";
          a.textContent = r[0].padEnd(23); b.textContent = r[1]; b.className = "w";
          d.appendChild(a); d.appendChild(b); outEl.appendChild(d);
        });
        outEl.scrollTop = outEl.scrollHeight;
      },
      looks: function () { line(looks.map(function (l) { return l.name; }).join(" · ")); },
      look: function (a) {
        var q = a.join(" ").toLowerCase(), i = looks.findIndex(function (l) { return l.id === q || l.name.toLowerCase() === q || l.name.toLowerCase().replace(/\s+/g, "") === q.replace(/\s+/g, ""); });
        if (i < 0) { line(L("Unbekannter Look. looks zeigt alle.", "unknown look. looks lists them all.")); return; }
        line("→ " + looks[i].name);
        setTimeout(function () { $("button", looks[i].li).click(); }, 350);
      },
      color: function (a) {
        var c = colors[(a[0] || "").toLowerCase().replace("ü", "ue").replace("ß", "ss")] || (/^#[0-9a-f]{6}$/i.test(a[0] || "") ? a[0] : null);
        if (!c) { line(L("color grün | cyan | bernstein | rot | violett | weiß | #rrggbb", "color green | cyan | amber | red | violet | white | #rrggbb")); return; }
        setColor(c); line(L("Farbe: ", "colour: ") + c);
      },
      matrix: function () { line(L("wake up …", "wake up …"), "dim"); setTimeout(function () { walls.select("matrix"); $("#walls").scrollIntoView({ behavior: still ? "auto" : "smooth" }); }, 500); },
      date: function () { line(new Date().toLocaleString(lang === "de" ? "de-AT" : "en-GB")); },
      whoami: function () { line(L("gast", "guest")); },
      echo: function (a) { line(a.join(" ")); },
      clear: function () { outEl.textContent = ""; },
      download: function () { line(L("lade WinMorph …", "downloading WinMorph …"), "dim"); location.href = $("[data-dl]").getAttribute("href"); },
      sudo: function () { line(L("Netter Versuch.", "nice try.")); },
    };
    function exec(cmd) {
      line(input.previousElementSibling.textContent + " " + cmd, "dim");
      var a = cmd.trim().split(/\s+/), c = (a.shift() || "").toLowerCase();
      if (!c) return;
      history.push(cmd); hi = history.length;
      if (Object.prototype.hasOwnProperty.call(run, c)) run[c](a); else line(c + L(": Befehl nicht gefunden. help zeigt alle.", ": command not found. try help."));
    }
    input.addEventListener("keydown", function (e) {
      if (e.key === "Enter") { exec(input.value); input.value = ""; }
      else if (e.key === "ArrowUp" && hi > 0) { input.value = history[--hi]; e.preventDefault(); }
      else if (e.key === "ArrowDown") { hi = Math.min(history.length, hi + 1); input.value = history[hi] || ""; e.preventDefault(); }
      else if (e.key === "Tab") {
        var m = names.filter(function (n) { return n.indexOf(input.value) === 0; });
        if (input.value && m.length === 1) { input.value = m[0] + " "; e.preventDefault(); }
      }
    });
    term.addEventListener("click", function () { if (!getSelection().toString()) input.focus({ preventScroll: true }); });
    function welcome() {
      outEl.textContent = "";
      line("winmorph-sh · " + L("tippe help", "type help"), "dim");
      if (typed) run.neofetch();
    }
    onLang.push(welcome);
    // once it is in view, it types neofetch by itself
    var typed = false;
    new IntersectionObserver(function (es, io) {
      if (!es[0].isIntersecting || typed) return;
      typed = true; io.disconnect();
      var word = "neofetch", i = 0;
      var t = setInterval(function () {
        if (document.activeElement === input) { clearInterval(t); return; }
        input.value = word.slice(0, ++i);
        if (i === word.length) { clearInterval(t); setTimeout(function () { if (document.activeElement !== input) { exec(input.value); input.value = ""; } }, 260); }
      }, still ? 0 : 90);
    }, { threshold: 0.6 }).observe(term);
  })();

  // ---- lock screens: a deck to flick through ----------------------------------------------------------------
  (function deck() {
    var el = $("#deck"), cards = $$("img", el), drag = null, busy = false;
    function layout() {
      cards.forEach(function (c, i) {
        c.style.zIndex = cards.length - i;
        c.style.opacity = i < 4 ? "1" : "0";
        c.style.filter = "brightness(" + (1 - i * 0.16).toFixed(2) + ")";
        c.style.transform = "translate3d(" + (i * 2.4) + "%," + (-i * 3.2) + "%,0) rotate(" + (i * 1.6) + "deg) scale(" + (1 - i * 0.045).toFixed(3) + ")";
      });
    }
    function fling(dir) {
      if (busy) return;
      busy = true;
      var top = cards[0];
      top.classList.remove("drag");
      top.style.transform = "translate3d(" + dir * 115 + "%,-6%,0) rotate(" + dir * 16 + "deg)";
      top.style.opacity = "0";
      setTimeout(function () { cards.push(cards.shift()); layout(); busy = false; }, 280);
    }
    el.addEventListener("pointerdown", function (e) { if (busy) return; drag = { x: e.clientX, dx: 0, id: e.pointerId }; cards[0].classList.add("drag"); el.setPointerCapture(e.pointerId); });
    el.addEventListener("pointermove", function (e) {
      if (!drag) return;
      drag.dx = e.clientX - drag.x;
      cards[0].style.transform = "translate3d(" + drag.dx + "px,0,0) rotate(" + (drag.dx * 0.04).toFixed(2) + "deg)";
    });
    function up() {
      if (!drag) return;
      var dx = drag.dx; drag = null;
      if (Math.abs(dx) > 80) fling(Math.sign(dx));
      else if (Math.abs(dx) < 6) fling(-1);
      else { cards[0].classList.remove("drag"); layout(); }
    }
    el.addEventListener("pointerup", up);
    el.addEventListener("pointercancel", function () { drag = null; cards[0].classList.remove("drag"); layout(); });
    el.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " " || e.key === "ArrowRight") { e.preventDefault(); fling(-1); } });
    layout();
  })();

  // ---- live wallpapers, running right here on the stage ------------------------------------------------------------------
  var live = (function () {
    var cv = $("#live-canvas"), ctx = cv.getContext("2d"), section = $("#stage"), scene = "matrix", on = false, seen = false, active = true, w = 0, h = 0, dpr = 1, st = null;
    var mouse = { x: -1, y: -1, burst: 0 };
    section.addEventListener("pointermove", function (e) { var r = cv.getBoundingClientRect(); mouse.x = e.clientX - r.left; mouse.y = e.clientY - r.top; });
    section.addEventListener("pointerleave", function () { mouse.x = mouse.y = -1; });
    section.addEventListener("pointerdown", function (e) { if (e.target.closest("button")) return; mouse.burst = 1; });
    function size() {
      dpr = Math.min(devicePixelRatio || 1, 3);
      w = cv.clientWidth; h = cv.clientHeight;
      cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      st = init[scene]();
    }
    var glyphs = "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホ0123456789ABCDEF<>/{}$#";
    var init = {
      matrix: function () {
        ctx.fillStyle = "#000"; ctx.fillRect(0, 0, w, h);
        var size = 16, cols = Math.ceil(w / size);
        return { size: size, drops: Array.from({ length: cols }, function () { return Math.random() * h / size; }) };
      },
      stars: function () {
        return { s: Array.from({ length: 420 }, function () { return { x: (Math.random() - 0.5) * 2, y: (Math.random() - 0.5) * 2, z: Math.random() }; }), cx: 0, cy: 0 };
      },
      net: function () {
        return { n: Array.from({ length: Math.round(Math.min(110, w * h / 14000)) }, function () { return { x: Math.random() * w, y: Math.random() * h, vx: (Math.random() - 0.5) * 0.4, vy: (Math.random() - 0.5) * 0.4 }; }) };
      },
    };
    var draw = {
      matrix: function () {
        ctx.fillStyle = "rgba(0,0,0,0.075)"; ctx.fillRect(0, 0, w, h);
        ctx.font = st.size + "px Consolas, ui-monospace, monospace";
        for (var i = 0; i < st.drops.length; i++) {
          var x = i * st.size, y = st.drops[i] * st.size;
          var near = mouse.x >= 0 && Math.abs(x - mouse.x) < 90;
          ctx.fillStyle = near ? "#d8ffe0" : "#00ff41";
          ctx.fillText(glyphs[(Math.random() * glyphs.length) | 0], x, y);
          if (y > h && Math.random() > 0.975) st.drops[i] = 0;
          st.drops[i] += near ? 1.6 : 0.55;
          if (mouse.burst && Math.abs(x - mouse.x) < 220) st.drops[i] = mouse.y / st.size - Math.random() * 6;
        }
        mouse.burst = 0;
      },
      stars: function () {
        ctx.fillStyle = "rgba(0,0,4,0.35)"; ctx.fillRect(0, 0, w, h);
        var tx = mouse.x >= 0 ? (mouse.x / w - 0.5) * 0.6 : 0, ty = mouse.y >= 0 ? (mouse.y / h - 0.5) * 0.6 : 0;
        st.cx += (tx - st.cx) * 0.05; st.cy += (ty - st.cy) * 0.05;
        var speed = mouse.burst > 0 ? 0.05 : 0.006; mouse.burst = Math.max(0, mouse.burst - 0.02);
        ctx.strokeStyle = "#fff";
        for (var i = 0; i < st.s.length; i++) {
          var p = st.s[i], z0 = p.z;
          p.z -= speed;
          if (p.z <= 0.02) { p.x = (Math.random() - 0.5) * 2; p.y = (Math.random() - 0.5) * 2; p.z = 1; continue; }
          var f = Math.min(w, h) * 0.5;
          var x0 = w / 2 + (p.x - st.cx) / z0 * f, y0 = h / 2 + (p.y - st.cy) / z0 * f;
          var x1 = w / 2 + (p.x - st.cx) / p.z * f, y1 = h / 2 + (p.y - st.cy) / p.z * f;
          ctx.globalAlpha = Math.min(1, (1 - p.z) * 1.4);
          ctx.lineWidth = (1 - p.z) * 2.2;
          ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x1, y1); ctx.stroke();
        }
        ctx.globalAlpha = 1;
      },
      net: function () {
        ctx.fillStyle = "#02060a"; ctx.fillRect(0, 0, w, h);
        var n = st.n, i, j;
        for (i = 0; i < n.length; i++) {
          var a = n[i];
          a.x += a.vx; a.y += a.vy;
          if (a.x < 0 || a.x > w) a.vx *= -1;
          if (a.y < 0 || a.y > h) a.vy *= -1;
          if (mouse.burst) { var dx = a.x - mouse.x, dy = a.y - mouse.y, d = Math.hypot(dx, dy) || 1; if (d < 260) { a.vx += dx / d * 3; a.vy += dy / d * 3; } }
          a.vx *= 0.99; a.vy *= 0.99;
          if (Math.hypot(a.vx, a.vy) < 0.15) { a.vx += (Math.random() - 0.5) * 0.1; a.vy += (Math.random() - 0.5) * 0.1; }
        }
        mouse.burst = 0;
        ctx.lineWidth = 1;
        for (i = 0; i < n.length; i++) {
          for (j = i + 1; j < n.length; j++) {
            var dd = Math.hypot(n[i].x - n[j].x, n[i].y - n[j].y);
            if (dd < 130) { ctx.strokeStyle = "rgba(34,211,238," + (1 - dd / 130) * 0.45 + ")"; ctx.beginPath(); ctx.moveTo(n[i].x, n[i].y); ctx.lineTo(n[j].x, n[j].y); ctx.stroke(); }
          }
          if (mouse.x >= 0) {
            var dm = Math.hypot(n[i].x - mouse.x, n[i].y - mouse.y);
            if (dm < 200) { ctx.strokeStyle = "rgba(255,255,255," + (1 - dm / 200) * 0.7 + ")"; ctx.beginPath(); ctx.moveTo(n[i].x, n[i].y); ctx.lineTo(mouse.x, mouse.y); ctx.stroke(); }
          }
          ctx.fillStyle = "#22d3ee"; ctx.fillRect(n[i].x - 1.5, n[i].y - 1.5, 3, 3);
        }
      },
    };
    var acc = 0;
    ticks.push(function (dt) {
      if (!on) return;
      acc += dt;
      if (acc < 1 / 40) return;   // like the real ones: no more frames than needed
      acc = 0;
      draw[scene]();
    });
    function setScene(s) {
      scene = s;
      if (w) { st = init[s](); if (still) draw[s](); }
    }
    function update() {
      on = seen && active && !still;
      if (seen && active && !w) { size(); if (still) draw[scene](); }
    }
    new IntersectionObserver(function (es) { seen = es[0].isIntersecting; update(); }).observe(section);
    new ResizeObserver(function () { if (w) size(); }).observe(section);
    return { scene: setScene, run: function (v) { active = v; update(); } };
  })();

  // ---- wallpapers: a stage, and the tiles that put a wallpaper on it -------------------------------------------
  var walls = (function () {
    var pics = $("#stage-pics"), cap = $("#stage-cap"), capName = $("#cap-name"), capHint = $("#cap-hint");
    var file = $("#own-file"), tiles = $$("#tiles button"), cur = tiles[1], layer = null, own = null, ownUrl = null, ownName = "";
    var pickBtn = document.createElement("button");
    pickBtn.type = "button"; pickBtn.className = "pick-btn";
    pickBtn.addEventListener("click", function () { file.click(); });

    function caption(anim) {
      capName.textContent = $("b", cur).textContent;
      var h = cur.dataset.own != null
        ? (ownUrl ? ownName + " · " : "") + L("Bild oder Video – bleibt auf deinem Gerät.", "Picture or video – stays on your device.")
        : cur.dataset.hint ? (lang === "en" ? cur.dataset.hintEn : cur.dataset.hint)
        : "";
      capHint.textContent = h;
      pickBtn.textContent = ownUrl ? L("Andere Datei", "Another file") : L("Datei wählen", "Choose file");
      if (cur.dataset.own != null) cap.appendChild(pickBtn); else if (pickBtn.parentNode) pickBtn.remove();
      if (anim) swap(cap);
    }
    onLang.push(function () {
      caption(false);
      if (cur.dataset.pic) select(cur, false);
    });

    function show(el) {
      var old = layer;
      layer = el;
      if (el && !el.parentNode) pics.appendChild(el);
      if (el) {
        void el.offsetWidth; el.classList.add("on");
        if (el.tagName === "VIDEO") el.play().catch(function () {});
      }
      if (old && old !== el) {
        if (old.tagName === "VIDEO") old.pause();
        old.classList.remove("on");
        setTimeout(function () { if (old !== layer && old !== own) old.remove(); }, 900);
      }
    }
    function picture(src) {
      var im = new Image();
      im.decoding = "async"; im.alt = "";
      im.sizes = "(max-width: 760px) 100vw, 64vw";
      im.srcset = src + "-sm.webp 480w, " + src + ".webp 1600w, " + src + "-2x.webp " + (src.indexOf("wall-") >= 0 ? "3840w" : "3200w");
      im.src = src + ".webp";
      return im;
    }
    function placeholder() {
      var d = document.createElement("div");
      d.className = "own-ph";
      return d;
    }

    function select(b, anim) {
      cur = b;
      tiles.forEach(function (t) { t.setAttribute("aria-pressed", String(t === b)); });
      if (b.dataset.look) wallLook = b.dataset.look;
      if (b.dataset.scene) { live.scene(b.dataset.scene); live.run(true); show(null); }
      else if (b.dataset.pic) {
        var im = picture(localized(b.dataset.pic)), imageLang = lang;
        var go = function () { if (cur === b && imageLang === lang) { show(im); live.run(false); } };
        if (im.complete && im.naturalWidth) go(); else im.addEventListener("load", go, { once: true });
      } else {
        show(own || placeholder()); live.run(false);
        if (!ownUrl) file.click();
      }
      caption(anim);
    }
    tiles.forEach(function (b) { b.addEventListener("click", function () { if (b !== cur || b.dataset.own != null) select(b, true); }); });

    // your own picture or video, the way the app shows it: muted in a loop, or a picture that follows the mouse
    file.addEventListener("change", function () {
      var f = file.files && file.files[0];
      if (!f) return;
      var video = /^video\//.test(f.type);
      if (!video && !/^image\//.test(f.type)) return;
      if (ownUrl) URL.revokeObjectURL(ownUrl);
      ownUrl = URL.createObjectURL(f); ownName = f.name;
      var el = document.createElement(video ? "video" : "img");
      if (video) { el.muted = true; el.loop = true; el.playsInline = true; el.autoplay = true; }
      else el.alt = "";
      el.src = ownUrl;
      var prev = own;
      own = el;
      if (cur.dataset.own != null) { show(el); caption(true); }
      if (prev && prev !== el) setTimeout(function () { prev.remove(); }, 900);
      file.value = "";
    });

    caption(false);
    return { select: function (id) { var b = tiles.filter(function (t) { return t.dataset.scene === id; })[0]; if (b) select(b, true); } };
  })();

  // ---- buttons that lean towards the pointer, and the app window ------------------------------------------------
  var magnets = $$(".magnet").map(function (el) { return { el: el, x: 0, y: 0 }; });
  ticks.push(function (dt) {
    var k = 1 - Math.exp(-dt * 10);
    magnets.forEach(function (m) {
      var r = m.el.getBoundingClientRect(), tx = 0, ty = 0;
      if (pointer.cx >= 0 && !still) {
        var dx = pointer.cx - (r.left + r.width / 2), dy = pointer.cy - (r.top + r.height / 2);
        if (Math.abs(dx) < r.width * 0.5 + 70 && Math.abs(dy) < r.height * 0.5 + 60) { tx = dx * 0.28; ty = dy * 0.35; }
      }
      m.x += (tx - m.x) * k; m.y += (ty - m.y) * k;
      if (Math.abs(m.x) > 0.05 || Math.abs(m.y) > 0.05 || tx || ty) m.el.style.transform = "translate(" + m.x.toFixed(2) + "px," + m.y.toFixed(2) + "px)";
    });
  });
  (function app() {
    var fig = $("#app"), im = $("img", fig), e = -1;
    ticks.push(function (dt) {
      var r = fig.getBoundingClientRect();
      if ((r.bottom < -100 || r.top > view.h + 100) && e >= 0) return;
      var goal = still ? 1 : out(clamp01((view.h - r.top) / (view.h * 0.8)));
      e = e < 0 ? goal : e + (goal - e) * (1 - Math.exp(-dt * 6));
      im.style.transform = still ? "none" : "rotateX(" + ((1 - e) * 24 - pointer.sy * 4).toFixed(2) + "deg) rotateY(" + (pointer.sx * 6).toFixed(2) + "deg) translateY(" + ((1 - e) * 60).toFixed(1) + "px)";
    });
  })();

  // ---- parts glide in the first time they come into view ---------------------------------------------------------
  var io = new IntersectionObserver(function (es) {
    es.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } });
  }, { rootMargin: "0px 0px -8% 0px", threshold: 0.12 });
  $$(".rv").forEach(function (el) { io.observe(el); });

  applyLang(lang);
  setLook("terminal");
  requestAnimationFrame(frame);
})();
