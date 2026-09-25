// Touch input for the WebGL demos: a virtual joystick (movement), a drag-to-look zone
// (camera), and a landscape-orientation prompt. Desktop mouse/keyboard (game-engine.js's
// own pointer-lock + keydown handling) is untouched -- this module only activates on
// devices that report touch support, and is purely additive on top of the existing
// keysDown/yaw/pitch state via the setMoveKey/applyLookDelta hooks on the game object.
(function (global) {
  "use strict";

  function isTouchDevice() {
    return "ontouchstart" in window || navigator.maxTouchPoints > 0;
  }

  // Must run before dialogue-controller.js reads document.body.classList (it checks this
  // synchronously when attachDialogueController() is called), so this module has to be
  // included, and executed, before that call -- see the <script> order in each demo's HTML.
  if (isTouchDevice()) {
    document.body.classList.add("touch-mode");
  }

  function attachTouchControls(game) {
    if (!isTouchDevice()) {
      return;
    }

    injectStyles();
    injectRotateOverlay();
    injectJoystick(game);
    injectLookZone(game);
  }

  function injectStyles() {
    const style = document.createElement("style");
    style.textContent = `
      #rotate-overlay {
        position: fixed;
        inset: 0;
        z-index: 1000;
        display: none;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 16px;
        background: #0f1115;
        color: #e6e8ec;
        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Inter, sans-serif;
        text-align: center;
        padding: 24px;
      }
      #rotate-overlay .icon {
        font-size: 48px;
        animation: rotate-hint 1.6s ease-in-out infinite;
      }
      @keyframes rotate-hint {
        0%, 100% { transform: rotate(0deg); }
        50% { transform: rotate(90deg); }
      }
      body.touch-mode.portrait-blocked #rotate-overlay {
        display: flex;
      }
      body.touch-mode #joystick-zone,
      body.touch-mode #look-zone {
        position: fixed;
        bottom: 0;
        z-index: 30;
        touch-action: none;
      }
      #joystick-zone {
        left: 0;
        width: 42%;
        height: 46%;
      }
      #look-zone {
        right: 0;
        width: 58%;
        height: 100%;
        top: 0;
      }
      #joystick-base {
        position: fixed;
        width: 108px;
        height: 108px;
        border-radius: 50%;
        background: rgba(230, 232, 236, 0.08);
        border: 1px solid rgba(230, 232, 236, 0.25);
        display: none;
        z-index: 31;
        pointer-events: none;
      }
      #joystick-thumb {
        position: fixed;
        width: 48px;
        height: 48px;
        margin: -24px 0 0 -24px;
        border-radius: 50%;
        background: rgba(110, 168, 254, 0.55);
        border: 1px solid rgba(230, 232, 236, 0.4);
        display: none;
        z-index: 32;
        pointer-events: none;
      }
      body.touch-mode #interact-prompt {
        pointer-events: auto;
        cursor: pointer;
        /* Above #joystick-zone/#look-zone (z-index 30/31) -- the look zone in particular
           covers the right 58% of the full screen height, which overlaps this
           bottom-center-anchored prompt, so without a higher z-index here taps land on
           the invisible look zone instead of the button underneath it (confirmed via
           document.elementFromPoint at the prompt's own coordinates returning #look-zone,
           not #interact-prompt). */
        z-index: 40;
      }
      body.touch-mode #dialogue-hint {
        display: none;
      }
      body.touch-mode #hud .hint-text {
        display: none;
      }
      body.touch-mode #hud .hint-text-touch {
        display: inline;
      }
      /* While a conversation is open the joystick/look zones are hidden so their
         full-screen touch targets don't sit over the dialogue panel and swallow taps
         meant for its input, send button, or close button. */
      body.touch-mode.dialogue-open #joystick-zone,
      body.touch-mode.dialogue-open #look-zone {
        display: none;
      }
    `;
    document.head.appendChild(style);
  }

  function injectRotateOverlay() {
    const overlay = document.createElement("div");
    overlay.id = "rotate-overlay";
    overlay.innerHTML = `
      <div class="icon">&#8635;</div>
      <div>Rotate your device to landscape<br/>for the full experience.</div>
    `;
    document.body.appendChild(overlay);

    function checkOrientation() {
      const isPortrait = window.innerHeight > window.innerWidth;
      document.body.classList.toggle("portrait-blocked", isPortrait);
    }

    checkOrientation();
    window.addEventListener("resize", checkOrientation);
    window.addEventListener("orientationchange", checkOrientation);
  }

  // Movement joystick: touching anywhere in the bottom-left zone drops a base at that
  // point and reports the drag offset as WASD keys via game.setMoveKey, reusing the exact
  // same movement code tick() already runs for the keyboard -- this only ever synthesizes
  // the same key codes a keyboard press would.
  function injectJoystick(game) {
    const zone = document.createElement("div");
    zone.id = "joystick-zone";
    document.body.appendChild(zone);

    const base = document.createElement("div");
    base.id = "joystick-base";
    document.body.appendChild(base);

    const thumb = document.createElement("div");
    thumb.id = "joystick-thumb";
    document.body.appendChild(thumb);

    const MAX_RADIUS = 46;
    let activeTouchId = null;
    let originX = 0;
    let originY = 0;
    let activeKeys = new Set();

    function setKeys(nextKeys) {
      for (const code of activeKeys) {
        if (!nextKeys.has(code)) game.setMoveKey(code, false);
      }
      for (const code of nextKeys) {
        if (!activeKeys.has(code)) game.setMoveKey(code, true);
      }
      activeKeys = nextKeys;
    }

    function updateFromDelta(dx, dy) {
      const dist = Math.hypot(dx, dy);
      const clamped = Math.min(dist, MAX_RADIUS);
      const angle = Math.atan2(dy, dx);
      const tx = originX + Math.cos(angle) * clamped;
      const ty = originY + Math.sin(angle) * clamped;
      thumb.style.left = `${tx}px`;
      thumb.style.top = `${ty}px`;

      // Dead zone before any movement registers, so a light/imprecise tap near the base
      // doesn't drift the player.
      const deadZone = 10;
      const nextKeys = new Set();
      if (dist > deadZone) {
        if (dx > deadZone) nextKeys.add("KeyD");
        if (dx < -deadZone) nextKeys.add("KeyA");
        if (dy > deadZone) nextKeys.add("KeyS");
        if (dy < -deadZone) nextKeys.add("KeyW");
      }
      setKeys(nextKeys);
    }

    function start(touch) {
      activeTouchId = touch.identifier;
      originX = touch.clientX;
      originY = touch.clientY;
      base.style.left = `${originX - 54}px`;
      base.style.top = `${originY - 54}px`;
      thumb.style.left = `${originX}px`;
      thumb.style.top = `${originY}px`;
      base.style.display = "block";
      thumb.style.display = "block";
    }

    function end() {
      activeTouchId = null;
      base.style.display = "none";
      thumb.style.display = "none";
      setKeys(new Set());
    }

    zone.addEventListener("touchstart", (event) => {
      if (activeTouchId !== null) return;
      event.preventDefault();
      start(event.changedTouches[0]);
    }, { passive: false });

    zone.addEventListener("touchmove", (event) => {
      for (const touch of event.changedTouches) {
        if (touch.identifier === activeTouchId) {
          event.preventDefault();
          updateFromDelta(touch.clientX - originX, touch.clientY - originY);
        }
      }
    }, { passive: false });

    function handleEnd(event) {
      for (const touch of event.changedTouches) {
        if (touch.identifier === activeTouchId) {
          end();
        }
      }
    }

    zone.addEventListener("touchend", handleEnd);
    zone.addEventListener("touchcancel", handleEnd);
  }

  // Look zone: dragging anywhere on the right side of the screen turns the camera,
  // reusing the same yaw/pitch state the desktop pointer-lock mousemove path updates
  // (see game.applyLookDelta in game-engine.js).
  function injectLookZone(game) {
    const zone = document.createElement("div");
    zone.id = "look-zone";
    document.body.appendChild(zone);

    let activeTouchId = null;
    let lastX = 0;
    let lastY = 0;

    zone.addEventListener("touchstart", (event) => {
      if (activeTouchId !== null) return;
      const touch = event.changedTouches[0];
      activeTouchId = touch.identifier;
      lastX = touch.clientX;
      lastY = touch.clientY;
    }, { passive: true });

    zone.addEventListener("touchmove", (event) => {
      for (const touch of event.changedTouches) {
        if (touch.identifier === activeTouchId) {
          const dx = touch.clientX - lastX;
          const dy = touch.clientY - lastY;
          lastX = touch.clientX;
          lastY = touch.clientY;
          game.applyLookDelta(dx, dy);
        }
      }
    }, { passive: true });

    function handleEnd(event) {
      for (const touch of event.changedTouches) {
        if (touch.identifier === activeTouchId) {
          activeTouchId = null;
        }
      }
    }

    zone.addEventListener("touchend", handleEnd);
    zone.addEventListener("touchcancel", handleEnd);
  }

  global.GameRagDemo = global.GameRagDemo || {};
  global.GameRagDemo.attachTouchControls = attachTouchControls;
})(window);
