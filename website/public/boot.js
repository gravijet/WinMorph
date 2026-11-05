// Before the first paint: the language, so nothing flickers.
(function () {
  var d = document.documentElement, l;
  try { l = localStorage.getItem("wm-lang"); } catch (e) {}
  var query = new URLSearchParams(location.search).get("lang");
  if (query === "de" || query === "en") l = query;
  if (!l) l = /^de\b/i.test(navigator.language || "") ? "de" : "en";
  d.setAttribute("lang", l === "en" ? "en" : "de");
  d.className += " js";
})();
