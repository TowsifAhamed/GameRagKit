// Where the GameRagKit server lives, for every Studio page and demo scene. Empty = the same
// origin as the page (local `gamerag studio`, or one container serving both). A static-only
// deploy (e.g. Cloudflare Pages in front of a server elsewhere) overwrites this file at build
// time with that server's URL -- see docs/deploy-cloudflare-render.md.
window.GAMERAG_SERVER_URL = window.GAMERAG_SERVER_URL || "";

// A free-tier server sleeps when idle and takes up to a minute to wake. Start waking it the
// moment a page opens, so it's usually ready by the time the player reaches an NPC.
if (window.GAMERAG_SERVER_URL) {
  fetch(window.GAMERAG_SERVER_URL + "/health", { cache: "no-store" }).catch(function () {});
}
