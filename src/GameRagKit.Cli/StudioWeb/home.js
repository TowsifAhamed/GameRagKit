(function () {
  "use strict";

  const npcCountEl = document.getElementById("npc-count");

  fetch(`${window.GAMERAG_SERVER_URL || ""}/studio/api/npcs`)
    .then((response) => (response.ok ? response.json() : null))
    .then((npcs) => {
      if (Array.isArray(npcs) && npcCountEl) {
        npcCountEl.textContent = `${npcs.length} NPC${npcs.length === 1 ? "" : "s"} loaded`;
      }
    })
    .catch(() => {
      // Header count is a nice-to-have; a fetch failure here shouldn't block the page.
    });
})();
