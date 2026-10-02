(function () {
  var match = document.cookie.match(/(?:^|; )locale=([^;]*)/);
  var ru = match ? match[1] === "ru" : false;
  document.documentElement.lang = ru ? "ru" : "uk";
  document.title = ru ? "Нет сети · Налоги ФОП 3 группы" : "Немає мережі · Податки ФОП 3 групи";
  document.getElementById("offline-uk").hidden = ru;
  document.getElementById("offline-ru").hidden = !ru;
  var buttons = document.querySelectorAll("[data-retry]");
  for (var i = 0; i < buttons.length; i++) buttons[i].addEventListener("click", function () { location.reload(); });
})();
