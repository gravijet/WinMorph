// the 404 page only needs the language (app.js expects the whole home page)
if (document.documentElement.lang === "en") document.querySelectorAll("[data-en]").forEach(function (el) { el.textContent = el.getAttribute("data-en"); });
