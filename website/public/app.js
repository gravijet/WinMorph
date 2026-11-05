(function () {
  "use strict";
  var root = document.documentElement;
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var still = window.matchMedia && matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (still) root.classList.add("still");
  var clamp01 = function (v) { return Math.min(1, Math.max(0, v)); };
  var out = function (t) { return 1 - Math.pow(1 - t, 3); };
  var smooth = function (t) { return t * t * (3 - 2 * t); };

  // ---- splitting text into words and letters (again after every language change) --------
  function wrapWords(el) {
    var walk = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, null), nodes = [], n;
    while ((n = walk.nextNode())) if (!n.parentNode.closest(".u") && n.nodeValue.trim()) nodes.push(n);
    nodes.forEach(function (t) {
      var frag = document.createDocumentFragment();
      t.nodeValue.split(/(\s+)/).forEach(function (part) {
        if (!part) return;
        if (/^\s+$/.test(part)) { frag.appendChild(document.createTextNode(part)); return; }
        var s = document.createElement("span"); s.className = "u"; s.textContent = part; frag.appendChild(s);
      });
      t.parentNode.replaceChild(frag, t);
    });
    el._units = $$(".u", el);
    el._shown = [];
    el._lit = null;
  }
  function wrapChars(el) {
    var text = el.textContent.trim(), i = 0;
    el.textContent = "";
    if (!el.closest("[aria-hidden]")) el.setAttribute("aria-label", text);
    var box = document.createElement("span"); box.setAttribute("aria-hidden", "true");
    text.split(/(\s+)/).forEach(function (word) {
      if (/^\s+$/.test(word)) { box.appendChild(document.createTextNode(" ")); return; }
      var w = document.createElement("span"); w.className = "w";
      Array.prototype.forEach.call(word, function (ch) {
        var c = document.createElement("span"); c.className = "c"; c.textContent = ch;
        c.style.setProperty("--i", i++); w.appendChild(c);
      });
      box.appendChild(w);
    });
    el.appendChild(box);
  }
  function split() {
    $$(".rise").forEach(function (el) { var on = el.classList.contains("in"); wrapChars(el); if (on) { el.classList.remove("in"); void el.offsetWidth; el.classList.add("in"); } });
    $$(".blur, .scrub").forEach(wrapWords);
  }

  // ---- language -----------------------------------------------------------------------
  var texts = new Map(), alts = new Map();
  $$("[data-en]").forEach(function (el) { texts.set(el, el.textContent); });
  $$("[data-en-alt]").forEach(function (el) { alts.set(el, el.getAttribute("alt")); });
  var lang = root.getAttribute("lang") === "en" ? "en" : "de";
  var rel = null;
  var meta = {
    de: ["WinMorph – Windows-Looks per Klick", "WinMorph legt ein komplett eigenes Design über Windows 10 und 11: Taskleiste, Startmenü, Alt+Tab, Hintergrund und Sperrbildschirm. 16 Looks, kostenlos, mit einem Klick zurück."],
    en: ["WinMorph – Windows looks in one click", "WinMorph lays a complete design of its own over Windows 10 and 11: taskbar, Start menu, Alt+Tab, wallpaper and lock screen. 16 looks, free, one click back."],
  };

  function applyLang(l) {
    lang = l;
    root.setAttribute("lang", l);
    texts.forEach(function (de, el) { el.textContent = l === "en" ? el.getAttribute("data-en") : de; });
    alts.forEach(function (de, el) { el.setAttribute("alt", l === "en" ? el.getAttribute("data-en-alt") : de); });
    $$("[data-src-en]").forEach(function (img) {
      if (!img.dataset.srcDe) img.dataset.srcDe = img.getAttribute("src");
      img.setAttribute("src", l === "en" ? img.dataset.srcEn : img.dataset.srcDe);
    });
    $$(".lang button").forEach(function (b) { b.setAttribute("aria-pressed", String(b.dataset.lang === l)); });
    document.title = meta[l][0];
    var d = $('meta[name="description"]'); if (d) d.content = meta[l][1];
    split();
    if (rel) render(rel);
    if (looks) looks.relabel();
  }
  $$(".lang button").forEach(function (b) {
    b.addEventListener("click", function () {
      if (b.dataset.lang === lang) return;
      try { localStorage.setItem("wm-lang", b.dataset.lang); } catch (e) {}
      applyLang(b.dataset.lang);
    });
  });

  // ---- newest version ----------------------------------------------------------------------
  function mb(n) {
    var v = n / 1048576;
    return (v >= 100 ? Math.round(v) : v.toFixed(1)).toString().replace(".", lang === "de" ? "," : ".") + " MB";
  }
  function render(r) {
    var s = (r.files || {}).setup, info = $("#dl-info");
    if (info) {
      var parts = ["Version " + r.version];
      if (r.published) {
        var d = new Date(r.published);
        if (!isNaN(d)) parts.push(d.toLocaleDateString(lang === "de" ? "de-AT" : "en-GB", { day: "numeric", month: "long", year: "numeric" }));
      }
      if (s && s.size) parts.push(mb(s.size));
      info.textContent = parts.join(" · ");
    }
    if (s && s.url) $$("[data-dl]").forEach(function (a) { a.setAttribute("href", s.url); });
  }
  try { var c = JSON.parse(localStorage.getItem("wm-rel") || "null"); if (c && c.version) { rel = c; render(c); } } catch (e) {}
  fetch("/api/release", { headers: { accept: "application/json" } })
    .then(function (r) { if (!r.ok) throw 0; return r.json(); })
    .then(function (r) { if (!r.version) return; rel = r; render(r); try { localStorage.setItem("wm-rel", JSON.stringify(r)); } catch (e) {} })
    .catch(function () {});

  // ---- one frame loop for everything that follows the scroll --------------------------------
  var view = { w: innerWidth, h: innerHeight }, ticks = [];
  var pointer = { x: 0, y: 0, sx: 0, sy: 0, on: false };
  addEventListener("resize", function () { view.w = innerWidth; view.h = innerHeight; }, { passive: true });
  addEventListener("pointermove", function (e) { if (e.pointerType !== "mouse") return; pointer.on = true; pointer.x = e.clientX / view.w - 0.5; pointer.y = e.clientY / view.h - 0.5; }, { passive: true });
  document.addEventListener("pointerleave", function () { pointer.on = false; pointer.x = pointer.y = 0; });
  var last = 0;
  function frame(t) {
    var dt = Math.min(0.1, (t - (last || t)) / 1000); last = t;
    var k = 1 - Math.exp(-dt * 3);
    pointer.sx += (pointer.x - pointer.sx) * k; pointer.sy += (pointer.y - pointer.sy) * k;
    for (var i = 0; i < ticks.length; i++) ticks[i](dt);
    requestAnimationFrame(frame);
  }
  // a stage only does work while it is near the screen
  function near(r, m) { return r.bottom > -view.h * (m || 0.3) && r.top < view.h * (1 + (m || 0.3)); }

  // headings rise and tiles pop in once they come into view
  var io = new IntersectionObserver(function (es) {
    es.forEach(function (e) { if (e.isIntersecting || e.boundingClientRect.top < 0) { e.target.classList.add("in"); io.unobserve(e.target); } });
  }, { rootMargin: "0px 0px -10% 0px" });
  $$(".rise:not(.hero-title .rise), .pop").forEach(function (el) { io.observe(el); });

  // words come out of a soft blur, tied to the scroll so nothing pops
  var blurs = $$(".blur");
  ticks.push(function (dt) {
    blurs.forEach(function (el) {
      if (!el._units || !el._units.length) return;
      var r = el.getBoundingClientRect();
      if (el._p >= 1 && r.top < view.h * 0.5) return;
      if (!near(r)) return;
      var goal = still ? 1 : clamp01((view.h * 0.96 - r.top) / (view.h * 0.42));
      el._p = el._p == null ? goal : el._p + (goal - el._p) * (1 - Math.exp(-dt * 7));
      if (Math.abs(goal - el._p) < 1e-3) el._p = goal;
      var n = el._units.length;
      el._units.forEach(function (u, i) {
        var l = smooth(clamp01(el._p * 1.9 - (i / n) * 0.9)), key = l.toFixed(3);
        if (key === el._shown[i]) return;
        el._shown[i] = key;
        u.style.opacity = key;
        u.style.transform = l < 1 ? "translateY(" + ((1 - l) * 0.35).toFixed(3) + "em)" : "none";
        u.style.filter = l < 1 ? "blur(" + ((1 - l) * 0.14).toFixed(3) + "em)" : "none";
      });
    });
  });

  // the statement lights up word by word while it is held
  var statement = $("#statement");
  ticks.push(function () {
    var el = $(".scrub", statement), r = statement.getBoundingClientRect();
    if (!el._units || !near(r)) return;
    var p = still ? 1 : clamp01((-r.top + view.h * 0.35) / (r.height - view.h * 0.6));
    var lit = Math.round(p * el._units.length);
    if (lit === el._lit) return;
    el._lit = lit;
    el._units.forEach(function (u, i) { u.style.opacity = i < lit ? "1" : ""; });
  });

  // ---- the looks, read once from the list ---------------------------------------------------
  var list = $$("#looks-list li").map(function (li) {
    return { li: li, id: li.dataset.id, name: $("b", li).textContent, accent: li.dataset.accent, group: li.dataset.group, desc: $("span", li) };
  });
  var groupName = function (key) { var g = $('#looks-groups [data-key="' + key + '"]'); return g ? g.textContent : ""; };
  function pic(src, srcset, sizes) {
    var img = new Image();
    img.decoding = "async"; img.alt = "";
    if (srcset) { img.srcset = srcset; img.sizes = sizes; }
    img.src = src;
    return img;
  }

  // ---- hero: the screen keeps changing its look ----------------------------------------------
  (function hero() {
    var screen = $("#hero-screen"), tilt = $("#hero-tilt"), name = $("#hero-name"), count = $("#hero-count");
    var order = ["terminal", "macos", "xp", "nexus", "win95", "ubuntu", "zen", "win7", "arch", "gaming", "nova", "win11", "chromeos", "mint", "debian", "win10"];
    var looksById = {}; list.forEach(function (l, i) { looksById[l.id] = { l: l, n: i }; });
    order = order.filter(function (id) { return looksById[id]; });
    var imgs = { terminal: $("img", screen) }, at = 0, timer = 0;
    function get(id) {
      if (!imgs[id]) { imgs[id] = pic("/shots/look-" + id + "-sm.webp"); screen.appendChild(imgs[id]); }
      return imgs[id];
    }
    function label() {
      var x = looksById[order[at]];
      name.textContent = x.l.name; name.classList.remove("flip"); void name.offsetWidth; name.classList.add("flip");
      count.textContent = String(x.n + 1).padStart(2, "0") + " / " + String(list.length).padStart(2, "0");
    }
    label();
    get(order[1]);
    function next() {
      var r = screen.getBoundingClientRect();
      if (document.hidden || r.bottom < 0) return;
      var n = (at + 1) % order.length, img = get(order[n]);
      if (!img.complete) return;   // not loaded yet: try again on the next beat
      imgs[order[at]].classList.remove("on");
      img.classList.add("on");
      at = n; label();
      get(order[(n + 1) % order.length]);
    }
    timer = setInterval(next, 2600);
    ticks.push(function () {
      var y = scrollY;
      if (y > view.h * 1.2) return;
      var p = clamp01(y / view.h);
      tilt.style.transform = "translate3d(0," + (-p * 12).toFixed(2) + "svh,0) scale(" + (1 + p * 0.06).toFixed(4) + ") rotateX(" + (-pointer.sy * 5).toFixed(2) + "deg) rotateY(" + (pointer.sx * 7).toFixed(2) + "deg)";
      tilt.style.opacity = (1 - p * 0.7).toFixed(3);
    });
    // the name rises on the first paint, once the font is there (or after a moment at the latest)
    var h = $(".hero-title .rise"), go = function () { h.classList.add("in"); };
    setTimeout(go, 700);
    if (document.fonts && document.fonts.load) document.fonts.load('500 1em "Inter Tight"').then(go, go);
  })();

  // ---- the app window comes up tilted back and straightens ----------------------------------
  (function app() {
    var stage = $("#app-stage"), win = $("#app-window"), e = -1;
    ticks.push(function (dt) {
      var r = stage.getBoundingClientRect();
      if (!near(r, 0.4) && e >= 0) return;
      var goal = still ? 1 : out(clamp01((view.h - r.top) / (view.h * 0.85)));
      e = e < 0 ? goal : e + (goal - e) * (1 - Math.exp(-dt * 6));
      win.style.transform = "translateY(" + ((1 - e) * 14).toFixed(2) + "vh) rotateX(" + ((1 - e) * 26 - pointer.sy * 4).toFixed(2) + "deg) rotateY(" + (pointer.sx * 5).toFixed(2) + "deg)";
      win.style.opacity = clamp01(e * 2).toFixed(3);
    });
  })();

  // ---- the looks: one screen held while the page scrolls through all of them ----------------
  var looks = (function () {
    var track = $("#looks-track"), sticky = $(".looks-sticky", track), screen = $("#looks-screen");
    var names = $("#looks-names"), group = $("#looks-group"), desc = $("#looks-desc"), bar = $("#looks-bar");
    var shown = {}, cur = -1, curMenu = null, pos = -1;
    list.forEach(function (l) { var d = document.createElement("div"); d.textContent = l.name; names.appendChild(d); });
    function img(i, menu) {
      var id = list[i].id + (menu ? "-menu" : ""), k = id;
      if (!shown[k]) {
        shown[k] = pic("/shots/look-" + id + ".webp", "/shots/look-" + id + "-sm.webp 800w, /shots/look-" + id + ".webp 1600w", "(max-width: 760px) 92vw, 56vw");
        screen.appendChild(shown[k]);
      }
      return shown[k];
    }
    function relabel() {
      if (cur < 0) return;
      var l = list[cur];
      group.textContent = groupName(l.group);
      desc.textContent = l.desc.textContent;
    }
    function show(i, menu) {
      var el = img(i, menu);
      if (curMenu && curMenu !== el) curMenu.classList.remove("on");
      el.classList.add("on"); curMenu = el;
      if (i !== cur) {
        cur = i;
        sticky.style.setProperty("--accent", list[i].accent);
        list.forEach(function (l, j) { l.li.classList.toggle("on", j === i); });
        relabel();
        desc.classList.remove("flip"); void desc.offsetWidth; desc.classList.add("flip");
        // the neighbours load ahead
        [i - 1, i + 1].forEach(function (j) { if (j >= 0 && j < list.length) { img(j, false); img(j, true); } });
        img(i, !menu);
      }
    }
    list.forEach(function (l, i) {
      l.li.addEventListener("click", function () {
        var r = track.getBoundingClientRect(), span = r.height - view.h;
        scrollTo({ top: scrollY + r.top + ((i + 0.2) / list.length) * span, behavior: still ? "auto" : "smooth" });
      });
    });
    ticks.push(function (dt) {
      var r = track.getBoundingClientRect();
      if (!near(r) && pos >= 0) return;
      var p = clamp01(-r.top / (r.height - view.h)), n = list.length;
      var f = Math.min(n - 1e-6, p * n), i = Math.floor(f);
      show(i, f - i > 0.55);
      pos = pos < 0 ? i : pos + (i - pos) * (still ? 1 : 1 - Math.exp(-dt * 9));
      names.style.transform = "translateY(" + (-pos * 1.12).toFixed(4) + "em)";
      bar.style.transform = "scaleX(" + p.toFixed(4) + ")";
    });
    return { relabel: relabel };
  })();

  // ---- hacker: three windows open out into a fan ------------------------------------------------
  (function fan() {
    var stage = $("#fan"), tilt = $(".fan-tilt", stage), items = $$("figure", stage), f = -1;
    ticks.push(function (dt) {
      var r = stage.getBoundingClientRect();
      if (!near(r, 0.4) && f >= 0) return;
      var small = view.w < 760, cw = r.width * (small ? 0.92 : 0.5);
      var goal = still ? 1 : clamp01((view.h * 0.95 - r.top) / (view.h * 0.7));
      f = f < 0 ? goal : f + (goal - f) * (1 - Math.exp(-dt * 6));
      tilt.style.transform = "rotateY(" + (pointer.sx * 7).toFixed(2) + "deg) rotateX(" + (-pointer.sy * 5).toFixed(2) + "deg)";
      items.forEach(function (it) {
        var side = +it.dataset.side, a = Math.abs(side), e = out(clamp01(f * 1.3 - a * 0.25));
        var x = small ? side * view.w * 0.06 : side * cw * 0.44, y = small ? -a * cw * 0.12 : -a * cw * 0.03, z = -a * cw * 0.2;
        var ry = small ? 0 : -side * 16, rz = side * (small ? 3 : 1.5);
        it.style.transform = "translate(-50%,-50%) translate3d(" + (x * e).toFixed(1) + "px," + (y * e + (1 - e) * view.h * 0.2).toFixed(1) + "px," + (z * e).toFixed(1) + "px) rotateX(" + ((1 - e) * 28).toFixed(2) + "deg) rotateY(" + (ry * e).toFixed(2) + "deg) rotate(" + (rz * e - (1 - e) * side * 4).toFixed(2) + "deg)";
        it.style.opacity = clamp01(e * 2.5).toFixed(3);
        $("i", it).style.opacity = (a * 0.38 * e).toFixed(3);
      });
    });
  })();

  // ---- lock screens run past sideways --------------------------------------------------------------
  (function locks() {
    var track = $("#locks-track"), row = $("#locks-row"), x = -1;
    ticks.push(function (dt) {
      var r = track.getBoundingClientRect();
      if (!near(r) && x >= 0) return;
      var p = clamp01(-r.top / (r.height - view.h)), goal = p * Math.max(0, row.scrollWidth - view.w);
      x = x < 0 || still ? goal : x + (goal - x) * (1 - Math.exp(-dt * 8));
      row.style.transform = "translate3d(" + (-x).toFixed(1) + "px,0,0)";
    });
  })();

  applyLang(lang);
  requestAnimationFrame(frame);
})();
