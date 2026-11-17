// Before the first paint: the language, so nothing flickers.
(function () {
  var d = document.documentElement, l;
  try { l = localStorage.getItem("wm-lang"); } catch (e) {}
  var query = new URLSearchParams(location.search).get("lang");
  if (query === "de" || query === "en") l = query;
  // /en/ is always English and / without a choice German, which is also what search engines see
  if (/^\/en(\/|$)/.test(location.pathname)) l = "en";
  else if (!l) l = location.pathname === "/" ? "de" : /^de\b/i.test(navigator.language || "") ? "de" : "en";
  d.setAttribute("lang", l === "en" ? "en" : "de");
  d.className += " js";
})();
