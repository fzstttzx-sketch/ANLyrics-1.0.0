/*
 * ANLyrics — data model.
 * This file is intentionally dependency-free: it only normalizes values.
 * Overlay reads the same shape from settings.json.
 */
(function () {
    "use strict";

    var ANL = window.ANL = window.ANL || {};

    var DEFAULTS = {
        version: 1,
        enabled: true,
        font: {
            original: { family: "Microsoft YaHei UI", size: 42 },
            translation: { family: "Microsoft YaHei UI", size: 22 }
        },
        colorMode: "cover",
        colors: { original: "#FFFFFFFF", translation: "#FFD6E8FF" },
        outline: {
            auto: true,
            original: "#D2000000",
            translation: "#D2000000",
            width: 1.8
        },
        opacity: 1.0,
        gap: 8,
        align: "center",
        mode: "desktop",
        offsetMs: 0,
        maxWidthRatio: 0.92
    };

    function clone(v) {
        return JSON.parse(JSON.stringify(v));
    }

    function num(v, fallback, lo, hi) {
        var n = typeof v === "number" ? v : parseFloat(v);
        if (!isFinite(n)) return fallback;
        if (typeof lo === "number" && n < lo) n = lo;
        if (typeof hi === "number" && n > hi) n = hi;
        return n;
    }

    function color(v, fallback) {
        if (typeof v !== "string") return fallback;
        var s = v.trim();
        if (/^#[0-9a-fA-F]{6}$/.test(s)) return "#FF" + s.slice(1).toUpperCase();
        if (/^#[0-9a-fA-F]{8}$/.test(s)) return s.toUpperCase();
        return fallback;
    }

    function family(v, fallback) {
        if (typeof v !== "string") return fallback;
        var s = v.trim();
        return s ? s : fallback;
    }

    ANL.DEFAULTS = DEFAULTS;
    ANL.clone = clone;

    ANL.normalize = function (raw) {
        raw = raw && typeof raw === "object" ? raw : {};
        var d = DEFAULTS;
        var out = clone(d);

        if (typeof raw.enabled === "boolean") out.enabled = raw.enabled;
        if (raw.font && raw.font.original) {
            out.font.original.family = family(raw.font.original.family, d.font.original.family);
            out.font.original.size = num(raw.font.original.size, d.font.original.size, 10, 160);
        }
        if (raw.font && raw.font.translation) {
            out.font.translation.family = family(raw.font.translation.family, d.font.translation.family);
            out.font.translation.size = num(raw.font.translation.size, d.font.translation.size, 8, 120);
        }
        if (raw.colorMode === "manual" || raw.colorMode === "cover") out.colorMode = raw.colorMode;
        if (raw.colors) {
            out.colors.original = color(raw.colors.original, d.colors.original);
            out.colors.translation = color(raw.colors.translation, d.colors.translation);
        }
        if (raw.outline) {
            if (typeof raw.outline.auto === "boolean") out.outline.auto = raw.outline.auto;
            out.outline.original = color(raw.outline.original, d.outline.original);
            out.outline.translation = color(raw.outline.translation, d.outline.translation);
            out.outline.width = num(raw.outline.width, d.outline.width, 0, 8);
        }
        out.opacity = num(raw.opacity, d.opacity, 0.08, 1);
        out.gap = num(raw.gap, d.gap, 0, 80);
        if (raw.align === "left" || raw.align === "center" || raw.align === "right") out.align = raw.align;
        if (raw.mode === "desktop" || raw.mode === "taskbar") out.mode = raw.mode;
        out.offsetMs = Math.round(num(raw.offsetMs, d.offsetMs, -5000, 5000));
        out.maxWidthRatio = num(raw.maxWidthRatio, d.maxWidthRatio, 0.5, 1);
        return out;
    };

    ANL.merge = function (base, patch) {
        var out = clone(base || {});
        Object.keys(patch || {}).forEach(function (k) {
            var v = patch[k];
            if (v && typeof v === "object" && !Array.isArray(v)) {
                out[k] = ANL.merge(out[k] || {}, v);
            } else {
                out[k] = v;
            }
        });
        return out;
    };

    ANL.hex6 = function (v, fallback) {
        var s = color(v, fallback || "#FFFFFFFF");
        return "#" + s.slice(3);
    };

    ANL.hexAlpha = function (v, fallback) {
        var s = color(v, fallback || "#FFFFFFFF");
        return s;
    };

    ANL.withAlpha = function (hex6, alphaHex) {
        var s = color(hex6, "#FFFFFFFF");
        return "#" + (alphaHex || "D2") + s.slice(3);
    };

    ANL.now = function () { return Date.now(); };
})();
