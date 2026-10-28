/*
 * ANLyrics — file / process boundary.
 * The plugin and the WPF overlay never share a port: they exchange small JSON files.
 */
(function () {
    "use strict";

    var ANL = window.ANL = window.ANL || {};

    function fsApi() {
        if (typeof betterncm === "undefined" || !betterncm.fs) {
            throw new Error("betterncm.fs is unavailable");
        }
        return betterncm.fs;
    }

    function join(a, b) {
        if (!a) return b;
        if (!b) return a;
        return a.replace(/[\\\/]+$/, "") + "\\" + b.replace(/^[\\\/]+/, "");
    }

    var io = ANL.io = {};
    var seq = Date.now();

    io.dataDir = async function () {
        var base = "";
        try { base = await betterncm.app.getDataPath(); } catch (e) { }
        if (!base || typeof base !== "string") throw new Error("cannot resolve BetterNCM data path");
        return join(base, "ANLyrics");
    };

    io.pluginDir = function () {
        try {
            if (typeof pluginPath !== "undefined" && pluginPath) return String(pluginPath);
        } catch (e) { }
        try {
            if (typeof plugin !== "undefined" && plugin && plugin.pluginPath) {
                return String(plugin.pluginPath);
            }
        } catch (e) { }
        return "";
    };

    io.paths = function (dir) {
        return {
            settings: join(dir, "settings.json"),
            session: join(dir, "session.json"),
            now: join(dir, "now.json"),
            lyrics: join(dir, "lyrics.json"),
            events: join(dir, "events.json"),
            control: join(dir, "control.json"),
            fonts: join(dir, "fonts.json"),
            log: join(dir, "overlay.log")
        };
    };

    io.ensureDir = async function (dir) {
        try {
            await fsApi().mkdir(dir);
            return true;
        } catch (e) {
            console.warn("[ANLyrics] mkdir failed", dir, e);
            return false;
        }
    };

    io.readText = async function (path) {
        try {
            return await fsApi().readFileText(path);
        } catch (e) {
            return null;
        }
    };

    io.writeText = async function (path, text) {
        try {
            var ok = await fsApi().writeFileText(path, String(text));
            if (!ok) console.warn("[ANLyrics] writeFileText returned false", path);
            return !!ok;
        } catch (e) {
            console.warn("[ANLyrics] writeFileText failed", path, e);
            return false;
        }
    };

    io.readJson = async function (path, fallback) {
        var text = await io.readText(path);
        if (!text) return fallback;
        try { return JSON.parse(text); } catch (e) { return fallback; }
    };

    io.writeJson = function (path, obj) {
        return io.writeText(path, JSON.stringify(obj, null, 2));
    };

    io.readSettings = async function (dir) {
        var raw = await io.readJson(io.paths(dir).settings, null);
        return ANL.normalize(raw || {});
    };

    io.saveSettings = function (dir, settings) {
        return io.writeJson(io.paths(dir).settings, ANL.normalize(settings));
    };

    io.savePatch = async function (dir, patch) {
        var current = await io.readSettings(dir);
        var next = ANL.normalize(ANL.merge(current, patch));
        await io.saveSettings(dir, next);
        return next;
    };

    io.readSession = async function (dir) {
        return (await io.readJson(io.paths(dir).session, null)) || {};
    };

    io.writeNow = function (dir, state) {
        return io.writeJson(io.paths(dir).now, state);
    };

    io.writeLyrics = function (dir, doc) {
        return io.writeJson(io.paths(dir).lyrics, doc);
    };

    io.control = async function (dir, cmd) {
        seq = Math.max(Date.now(), seq + 1);
        return io.writeJson(io.paths(dir).control, {
            seq: seq,
            cmd: cmd,
            at: Date.now()
        });
    };

    io.readEvents = async function (dir) {
        return (await io.readJson(io.paths(dir).events, null)) || {};
    };

    io.readFonts = async function (dir) {
        var doc = await io.readJson(io.paths(dir).fonts, null);
        if (!doc || !Array.isArray(doc.fonts)) return [];
        return doc.fonts.filter(function (x) { return typeof x === "string" && x.trim(); });
    };

    // Re-enumerate installed fonts even when the overlay is not running /
    // WPF has cached its font collection. No DLL: PowerShell + WPF in memory.
    io.refreshFonts = async function (dir) {
        var target = io.paths(dir).fonts.replace(/'/g, "''");
        var script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "Add-Type -AssemblyName System.Drawing;" +
            "$names=@((New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name } | Where-Object { $_ -and ($_ -notlike '@*') } | Sort-Object -Unique);" +
            "$json=ConvertTo-Json -InputObject ([pscustomobject]@{fonts=$names}) -Compress;" +
            "[IO.File]::WriteAllText('" + target + "',$json,[Text.UTF8Encoding]::new($false));";
        try {
            return await betterncm.app.exec(
                "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " +
                utf16leBase64(script), false, false);
        } catch (e) {
            console.warn("[ANLyrics] refreshFonts failed", e);
            return false;
        }
    };

    function utf16leBase64(text) {
        var bytes = [];
        for (var i = 0; i < text.length; i++) {
            var code = text.charCodeAt(i);
            bytes.push(code & 0xff, (code >> 8) & 0xff);
        }
        return btoa(String.fromCharCode.apply(null, bytes));
    }

    io.launchOverlay = async function (dir) {
        var pdir = io.pluginDir();
        if (!pdir) {
            console.warn("[ANLyrics] cannot locate plugin directory");
            return false;
        }
        var script = join(pdir, "overlay\\launch.ps1");
        if (!script) {
            console.warn("[ANLyrics] cannot locate overlay launch.ps1");
            return false;
        }
        // BetterNCM's exec() parses and re-joins arguments without quoting.
        // A path containing spaces would therefore be split. EncodedCommand
        // is a single token and keeps arbitrary paths intact.
        var inner = "& '" + script.replace(/'/g, "''") + "' -DataDir '" +
            String(dir).replace(/'/g, "''") + "'";
        var encoded = utf16leBase64(inner);
        var cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded;
        try {
            var ok = await betterncm.app.exec(cmd, false, false);
            console.log("[ANLyrics] overlay launch ->", ok);
            return ok !== false;
        } catch (e) {
            console.warn("[ANLyrics] overlay launch failed", e);
            return false;
        }
    };

    io.log = function () {
        try { console.log.apply(console, ["[ANLyrics]"].concat(Array.prototype.slice.call(arguments))); } catch (e) { }
    };
})();
