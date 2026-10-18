/* ==========================================================================
   VideoLuwak landing page behavior
   No dependencies. Every query is guarded so the page keeps working when a
   section is absent or the network is unavailable.
   ========================================================================== */
(function () {
  "use strict";

  var REPO = "TraberSoftware/VideoLuwak";
  var RELEASES_LATEST = "https://github.com/" + REPO + "/releases/latest";
  var RELEASE_API = "https://api.github.com/repos/" + REPO + "/releases/latest";
  var THEME_KEY = "videoluwak-theme";
  var RELEASE_CACHE_KEY = "videoluwak-latest-release";
  var RELEASE_CACHE_TTL = 10 * 60 * 1000;
  var RELEASE_TIMEOUT = 8000;

  var root = document.documentElement;
  var schemeQuery = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;

  function readStorage(storage, key) {
    try {
      return storage ? storage.getItem(key) : null;
    } catch (error) {
      return null;
    }
  }

  function writeStorage(storage, key, value) {
    try {
      if (storage) {
        storage.setItem(key, value);
      }
    } catch (error) {
      /* Storage may be unavailable; the in-memory state still works. */
    }
  }

  function storedTheme() {
    var value = readStorage(window.localStorage, THEME_KEY);
    return value === "light" || value === "dark" ? value : null;
  }

  /* ------------------------------------------------------------------------
     Theme
     ------------------------------------------------------------------------ */
  function themeColor(theme) {
    return theme === "dark" ? "#0E1418" : "#F4F6F6";
  }

  function syncThemeColor(theme) {
    var metas = document.querySelectorAll('meta[name="theme-color"]');
    for (var i = 0; i < metas.length; i++) {
      metas[i].setAttribute("content", themeColor(theme));
      metas[i].removeAttribute("media");
    }
  }

  function syncThemeToggle(theme) {
    var toggle = document.querySelector(".theme-toggle");
    if (!toggle) {
      return;
    }

    var isDark = theme === "dark";
    var label = isDark ? "Switch to light theme" : "Switch to dark theme";
    toggle.setAttribute("aria-pressed", isDark ? "true" : "false");
    toggle.setAttribute("aria-label", label);
    toggle.setAttribute("title", label);
  }

  function applyTheme(theme, persist) {
    var next = theme === "dark" ? "dark" : "light";
    root.setAttribute("data-theme", next);
    root.style.colorScheme = next;

    syncThemeColor(next);
    syncThemeToggle(next);

    if (persist) {
      writeStorage(window.localStorage, THEME_KEY, next);
    }
  }

  var explicitTheme = storedTheme() !== null;
  var initialTheme = root.getAttribute("data-theme");

  if (initialTheme !== "light" && initialTheme !== "dark") {
    initialTheme = schemeQuery && schemeQuery.matches ? "dark" : "light";
  }

  applyTheme(initialTheme, false);

  var themeToggle = document.querySelector(".theme-toggle");
  if (themeToggle) {
    themeToggle.addEventListener("click", function () {
      var current = root.getAttribute("data-theme") === "dark" ? "dark" : "light";
      explicitTheme = true;
      applyTheme(current === "dark" ? "light" : "dark", true);
    });
  }

  if (schemeQuery) {
    var onSchemeChange = function (event) {
      if (!explicitTheme) {
        applyTheme(event.matches ? "dark" : "light", false);
      }
    };

    if (typeof schemeQuery.addEventListener === "function") {
      schemeQuery.addEventListener("change", onSchemeChange);
    } else if (typeof schemeQuery.addListener === "function") {
      schemeQuery.addListener(onSchemeChange);
    }
  }

  /* ------------------------------------------------------------------------
     Operating-system detection
     ------------------------------------------------------------------------ */
  function detectOS() {
    var userAgentData = navigator.userAgentData;
    var platform = String((userAgentData && userAgentData.platform) || navigator.platform || "").toLowerCase();
    var userAgent = String(navigator.userAgent || "").toLowerCase();

    if (platform.indexOf("win") !== -1 || userAgent.indexOf("windows") !== -1) {
      return "win";
    }

    if (platform.indexOf("mac") !== -1 || userAgent.indexOf("macintosh") !== -1 || userAgent.indexOf("mac os x") !== -1) {
      return "mac";
    }

    if (platform.indexOf("linux") !== -1 || platform.indexOf("x11") !== -1 || userAgent.indexOf("linux") !== -1) {
      return "linux";
    }

    return "";
  }

  var detectedOS = detectOS();

  function applyDetectedOS() {
    var ctaText = document.querySelector("[data-os-cta] span");
    var labels = {
      win: "Download for Windows",
      mac: "Download for macOS",
      linux: "Download for Linux"
    };

    if (ctaText && labels[detectedOS]) {
      ctaText.textContent = labels[detectedOS];
    }

    if (!detectedOS) {
      return;
    }

    var platformKey = detectedOS === "mac" ? "mac-arm" : detectedOS;
    var card = document.querySelector('.download-card[data-platform="' + platformKey + '"]');
    if (card) {
      card.classList.add("is-recommended");
    }
  }

  applyDetectedOS();

  /* ------------------------------------------------------------------------
     Latest GitHub release
     ------------------------------------------------------------------------ */
  function normalizeVersion(tag) {
    if (!tag) {
      return "";
    }

    var value = String(tag).trim().replace(/^v/i, "");
    return value ? "v" + value : "";
  }

  function readReleaseCache() {
    var raw = readStorage(window.sessionStorage, RELEASE_CACHE_KEY);
    if (!raw) {
      return null;
    }

    try {
      var cached = JSON.parse(raw);
      if (!cached || typeof cached.ts !== "number" || !cached.data) {
        return null;
      }

      if (Date.now() - cached.ts > RELEASE_CACHE_TTL) {
        return null;
      }

      return cached.data;
    } catch (error) {
      return null;
    }
  }

  function writeReleaseCache(release) {
    writeStorage(
      window.sessionStorage,
      RELEASE_CACHE_KEY,
      JSON.stringify({ ts: Date.now(), data: release })
    );
  }

  function fetchLatestRelease() {
    var cached = readReleaseCache();
    if (cached) {
      return Promise.resolve(cached);
    }

    if (typeof window.fetch !== "function") {
      return Promise.resolve(null);
    }

    var controller = null;
    try {
      controller = new AbortController();
    } catch (error) {
      controller = null;
    }

    var timer = window.setTimeout(function () {
      if (controller) {
        controller.abort();
      }
    }, RELEASE_TIMEOUT);

    return window.fetch(RELEASE_API, {
      headers: { Accept: "application/vnd.github+json" },
      signal: controller ? controller.signal : undefined
    }).then(function (response) {
      if (!response.ok) {
        return null;
      }

      return response.json().then(function (data) {
        var assets = Array.isArray(data.assets) ? data.assets : [];
        var release = {
          tag_name: data.tag_name || "",
          html_url: data.html_url || RELEASES_LATEST,
          assets: assets.map(function (asset) {
            return {
              name: asset && asset.name ? asset.name : "",
              url: asset && asset.browser_download_url ? asset.browser_download_url : ""
            };
          })
        };

        writeReleaseCache(release);
        return release;
      });
    }).catch(function () {
      return null;
    }).then(function (release) {
      window.clearTimeout(timer);
      return release;
    });
  }

  function applyRelease(release) {
    if (!release) {
      return;
    }

    var version = normalizeVersion(release.tag_name);
    if (version) {
      var versionNodes = document.querySelectorAll("[data-version]");
      for (var i = 0; i < versionNodes.length; i++) {
        versionNodes[i].textContent = version;
        versionNodes[i].hidden = false;
      }
    }

    if (release.html_url) {
      var releaseLinks = document.querySelectorAll("[data-release-link]");
      for (var j = 0; j < releaseLinks.length; j++) {
        releaseLinks[j].setAttribute("href", release.html_url);
      }
    }

    var suffixes = {
      win: "win-x64.zip",
      linux: "linux-x64.tar.gz",
      "mac-arm": "macos-arm64.dmg",
      "mac-intel": "macos-x64.dmg"
    };

    Object.keys(suffixes).forEach(function (key) {
      var suffix = suffixes[key].toLowerCase();
      var asset = (release.assets || []).filter(function (candidate) {
        return candidate && typeof candidate.name === "string" &&
          candidate.name.toLowerCase().indexOf(suffix, candidate.name.length - suffix.length) !== -1;
      })[0];

      if (!asset) {
        return;
      }

      var links = document.querySelectorAll('[data-download="' + key + '"]');
      for (var k = 0; k < links.length; k++) {
        if (asset.url) {
          links[k].setAttribute("href", asset.url);
        }
      }

      var names = document.querySelectorAll('[data-asset-name="' + key + '"]');
      for (var n = 0; n < names.length; n++) {
        if (asset.name) {
          names[n].textContent = asset.name;
        }
      }
    });
  }

  var hasReleaseHooks = document.querySelector(
    "[data-version], [data-release-link], [data-download], [data-asset-name]"
  );

  if (hasReleaseHooks) {
    fetchLatestRelease().then(applyRelease).catch(function () {
      /* The static /releases/latest links are the fallback. */
    });
  }

  /* ------------------------------------------------------------------------
     CLI copy button
     ------------------------------------------------------------------------ */
  function copyWithSelection(text, button) {
    var textarea = document.createElement("textarea");
    textarea.value = text;
    textarea.setAttribute("readonly", "");
    textarea.style.position = "fixed";
    textarea.style.top = "-1000px";
    textarea.style.left = "-1000px";
    textarea.style.opacity = "0";
    document.body.appendChild(textarea);
    textarea.select();

    var copied = false;
    try {
      copied = document.execCommand("copy");
    } catch (error) {
      copied = false;
    }

    document.body.removeChild(textarea);

    if (button) {
      button.focus({ preventScroll: true });
    }

    if (!copied) {
      throw new Error("Copy command was rejected.");
    }
  }

  function copyText(text, button) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text).catch(function () {
        copyWithSelection(text, button);
      });
    }

    return new Promise(function (resolve, reject) {
      try {
        copyWithSelection(text, button);
        resolve();
      } catch (error) {
        reject(error);
      }
    });
  }

  var copyButton = document.querySelector("[data-copy-cli]");
  var copyStatus = document.querySelector("[data-copy-status]");
  var commandNodes = document.querySelectorAll("#cli-commands .cmd");

  if (copyButton && commandNodes.length) {
    var copyLabel = copyButton.querySelector("[data-copy-label]");
    var resetTimer = null;

    copyButton.addEventListener("click", function () {
      var commands = [];
      for (var i = 0; i < commandNodes.length; i++) {
        commands.push(commandNodes[i].textContent.trim());
      }

      copyText(commands.join("\n"), copyButton).then(function () {
        copyButton.classList.add("is-copied");
        if (copyLabel) {
          copyLabel.textContent = "Copied";
        }
        if (copyStatus) {
          copyStatus.textContent = "CLI commands copied to clipboard.";
        }
      }).catch(function () {
        if (copyStatus) {
          copyStatus.textContent = "Could not copy. Select the commands manually.";
        }
      }).then(function () {
        window.clearTimeout(resetTimer);
        resetTimer = window.setTimeout(function () {
          copyButton.classList.remove("is-copied");
          if (copyLabel) {
            copyLabel.textContent = "Copy";
          }
          if (copyStatus) {
            copyStatus.textContent = "";
          }
        }, 2000);
      });
    });
  }

  /* ------------------------------------------------------------------------
     Current year
     ------------------------------------------------------------------------ */
  var year = String(new Date().getFullYear());
  var yearNodes = document.querySelectorAll("[data-year]");
  for (var y = 0; y < yearNodes.length; y++) {
    yearNodes[y].textContent = year;
  }
})();
