/*
 * ANLyrics — BetterNCM settings page.
 * Only reads/writes settings.json; all visual work stays in the overlay.
 */
(function () {
    "use strict";

    var ANL = window.ANL = window.ANL || {};
    var root = null;
    var fonts = [];
    var saveTimer = 0;
    var pending = {};

    function q(sel, el) { return (el || root).querySelector(sel); }
    function qa(sel, el) { return Array.prototype.slice.call((el || root).querySelectorAll(sel)); }

    function esc(s) {
        return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    }

    function fontOptions(current, filter) {
        var query = String(filter || "").trim().toLowerCase();
        var cjk = /[\u3400-\u9fff]/;
        var list = fonts.filter(function (f) {
            return !query || String(f).toLowerCase().indexOf(query) >= 0;
        });
        list.sort(function (a, b) {
            var ca = cjk.test(a);
            var cb = cjk.test(b);
            if (ca !== cb) return ca ? -1 : 1;
            try { return String(a).localeCompare(String(b), "zh-Hans-CN"); }
            catch (e) { return String(a).localeCompare(String(b)); }
        });
        if (current && list.indexOf(current) < 0) list.unshift(current);
        if (!list.length) list = ["Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimHei", "SimSun"];
        return list.map(function (f) {
            return '<option value="' + esc(f) + '"' + (f === current ? " selected" : "") + ">" + esc(f) + "</option>";
        }).join("");
    }

    function colorValue(c, fallback) { return ANL.hex6(c, fallback); }

    function setStatus(text, bad) {
        if (!root) return;
        var el = q(".anl-status");
        if (!el) return;
        el.textContent = text;
        el.classList.toggle("bad", !!bad);
    }

    function schedule(patch, delay) {
        pending = ANL.merge(pending, patch);
        clearTimeout(saveTimer);
        setStatus("正在保存…");
        saveTimer = setTimeout(flush, typeof delay === "number" ? delay : 180);
    }

    async function flush() {
        var patch = pending;
        pending = {};
        try {
            var next = await ANL.app.applyPatch(patch);
            applyDisabled(next);
            setStatus("已保存");
        } catch (e) {
            console.warn("[ANLyrics] save failed", e);
            setStatus("保存失败", true);
        }
    }

    function applyDisabled(s) {
        if (!root) return;
        var manual = s.colorMode === "manual";
        qa("[data-manual-color]").forEach(function (el) { el.disabled = !manual; });
        qa("[data-manual-row]").forEach(function (el) { el.classList.toggle("is-off", !manual); });
        qa("[data-outline-color]").forEach(function (el) { el.disabled = !!s.outline.auto; });
        q(".anl-mode-note").textContent = s.mode === "taskbar"
            ? "任务栏模式：永久锁定、左对齐，只能从子菜单切回桌面。"
            : "桌面模式：拖动移动，悬停歌词打开子菜单。";
    }

    function bind(s) {
        var enabled = q("#anl-enabled");
        enabled.checked = !!s.enabled;
        enabled.addEventListener("change", async function () {
            setStatus(enabled.checked ? "正在启动…" : "正在关闭…");
            try {
                var next = await ANL.app.setEnabled(enabled.checked);
                applyDisabled(next);
                setStatus(enabled.checked ? "已开启" : "已关闭");
            } catch (e) { setStatus("操作失败", true); }
        });

        ["original", "translation"].forEach(function (key) {
            var sel = q("#anl-font-" + key);
            sel.innerHTML = fontOptions(s.font[key].family);
            sel.addEventListener("change", function () {
                var patch = { font: {} };
                patch.font[key] = { family: sel.value };
                schedule(patch);
            });

            var range = q("#anl-size-" + key);
            range.value = s.font[key].size;
            var out = q("#anl-sizeval-" + key);
            out.textContent = s.font[key].size + " px";
            range.addEventListener("input", function () {
                out.textContent = range.value + " px";
                var p = { font: {} };
                p.font[key] = { size: Number(range.value) };
                schedule(p);
            });
        });

        var fontSearch = q("#anl-font-search");
        if (fontSearch) {
            fontSearch.addEventListener("input", function () {
                ["original", "translation"].forEach(function (key) {
                    var sel = q("#anl-font-" + key);
                    var cur = sel.value;
                    sel.innerHTML = fontOptions(cur, fontSearch.value);
                    sel.value = cur;
                });
            });
        }

        var refreshFonts = q("#anl-refresh-fonts");
        refreshFonts.addEventListener("click", async function () {
            var current = {
                original: q("#anl-font-original").value,
                translation: q("#anl-font-translation").value
            };
            refreshFonts.disabled = true;
            refreshFonts.textContent = "刷新中…";
            setStatus("正在读取系统字体…");
            try {
                await ANL.io.refreshFonts(ANL.app.dir);
                await ANL.io.control(ANL.app.dir, "fonts");
                await new Promise(function (r) { setTimeout(r, 1200); });
                fonts = await ANL.io.readFonts(ANL.app.dir);
                ["original", "translation"].forEach(function (key) {
                    var sel = q("#anl-font-" + key);
                    sel.innerHTML = fontOptions(current[key], fontSearch ? fontSearch.value : "");
                    sel.value = current[key];
                });
                setStatus(fonts.length ? "字体列表已刷新（" + fonts.length + "）" : "没有读取到字体", !fonts.length);
            } catch (e) {
                console.warn("[ANLyrics] refresh fonts failed", e);
                setStatus("刷新字体失败", true);
            } finally {
                refreshFonts.disabled = false;
                refreshFonts.textContent = "刷新系统字体";
            }
        });

        qa("[data-color-mode]").forEach(function (btn) {
            btn.classList.toggle("active", btn.getAttribute("data-color-mode") === s.colorMode);
            btn.addEventListener("click", function () {
                qa("[data-color-mode]").forEach(function (b) { b.classList.remove("active"); });
                btn.classList.add("active");
                var mode = btn.getAttribute("data-color-mode");
                applyDisabled(ANL.merge(s, { colorMode: mode }));
                schedule({ colorMode: mode }, 0);
            });
        });

        var orig = q("#anl-color-original");
        var trans = q("#anl-color-translation");
        orig.value = colorValue(s.colors.original, "#FFFFFF");
        trans.value = colorValue(s.colors.translation, "#D6E8FF");
        orig.addEventListener("input", function () {
            schedule({ colors: { original: ANL.hexAlpha(orig.value, "FF") } });
        });
        trans.addEventListener("input", function () {
            schedule({ colors: { translation: ANL.hexAlpha(trans.value, "FF") } });
        });

        var auto = q("#anl-outline-auto");
        auto.checked = !!s.outline.auto;
        auto.addEventListener("change", function () {
            applyDisabled(ANL.merge(s, { outline: { auto: auto.checked } }));
            schedule({ outline: { auto: auto.checked } }, 0);
        });

        var oo = q("#anl-outline-original");
        var ot = q("#anl-outline-translation");
        oo.value = colorValue(s.outline.original, "#000000");
        ot.value = colorValue(s.outline.translation, "#000000");
        oo.addEventListener("input", function () {
            schedule({ outline: { original: ANL.hexAlpha(oo.value, "D2"), auto: false } });
        });
        ot.addEventListener("input", function () {
            schedule({ outline: { translation: ANL.hexAlpha(ot.value, "D2"), auto: false } });
        });

        var width = q("#anl-outline-width");
        width.value = s.outline.width;
        var widthVal = q("#anl-outline-width-val");
        widthVal.textContent = Number(s.outline.width).toFixed(1);
        width.addEventListener("input", function () {
            widthVal.textContent = Number(width.value).toFixed(1);
            schedule({ outline: { width: Number(width.value) } });
        });

        var opacity = q("#anl-opacity");
        opacity.value = s.opacity;
        var opVal = q("#anl-opacity-val");
        opVal.textContent = Math.round(s.opacity * 100) + "%";
        opacity.addEventListener("input", function () {
            opVal.textContent = Math.round(Number(opacity.value) * 100) + "%";
            schedule({ opacity: Number(opacity.value) });
        });

        var gap = q("#anl-gap");
        gap.value = s.gap;
        var gapVal = q("#anl-gap-val");
        gapVal.textContent = s.gap + " px";
        gap.addEventListener("input", function () {
            gapVal.textContent = gap.value + " px";
            schedule({ gap: Number(gap.value) });
        });

        var mode = q("#anl-mode");
        mode.value = s.mode;
        mode.addEventListener("change", async function () {
            setStatus("正在切换…");
            try {
                var next = await ANL.app.applyPatch({ mode: mode.value });
                applyDisabled(next);
                setStatus("已切换");
            } catch (e) { setStatus("切换失败", true); }
        });

        var offset = q("#anl-offset");
        offset.value = s.offsetMs;
        var offVal = q("#anl-offset-val");
        offVal.textContent = (s.offsetMs > 0 ? "+" : "") + s.offsetMs + " ms";
        offset.addEventListener("input", function () {
            offVal.textContent = (Number(offset.value) > 0 ? "+" : "") + offset.value + " ms";
            schedule({ offsetMs: Number(offset.value) });
        });

        applyDisabled(s);
    }

    async function build() {
        if (!ANL.app.ready) await ANL.app.start();
        var s = await ANL.app.reloadSettings();
        fonts = await ANL.io.readFonts(ANL.app.dir);
        if (fonts.length < 20) {
            try {
                await ANL.io.refreshFonts(ANL.app.dir);
                await new Promise(function (r) { setTimeout(r, 1200); });
                fonts = await ANL.io.readFonts(ANL.app.dir);
            } catch (e) {
                console.warn("[ANLyrics] initial font scan failed", e);
            }
        }
        setStatus("");

        root.innerHTML = '' +
            '<div class="anl-head">' +
            '  <div><div class="anl-title">ANLyrics</div><div class="anl-sub">独立桌面歌词 · 封面取色</div></div>' +
            '  <label class="anl-switch" title="显示 / 隐藏桌面歌词">' +
            '    <input id="anl-enabled" type="checkbox"><span></span>' +
            '  </label>' +
            '</div>' +
            '<div class="anl-grid">' +
            '  <section class="anl-card">' +
            '    <h3>字体</h3>' +
            '    <div class="anl-field anl-font-search"><input id="anl-font-search" type="search" placeholder="搜索字体（中文 / English）"></div>' +
            '    <div class="anl-field"><label>原歌词字体</label><select id="anl-font-original"></select></div>' +
            '    <div class="anl-field"><label>原歌词大小</label><div class="anl-range"><input id="anl-size-original" type="range" min="16" max="96" step="1"><b id="anl-sizeval-original"></b></div></div>' +
            '    <div class="anl-field"><label>翻译字体</label><select id="anl-font-translation"></select></div>' +
            '    <div class="anl-field"><label>翻译大小</label><div class="anl-range"><input id="anl-size-translation" type="range" min="10" max="64" step="1"><b id="anl-sizeval-translation"></b></div></div>' +
            '    <div class="anl-field"><label>两行间距</label><div class="anl-range"><input id="anl-gap" type="range" min="0" max="48" step="1"><b id="anl-gap-val"></b></div></div>' +
            '    <button type="button" id="anl-refresh-fonts" class="anl-refresh">刷新系统字体</button>' +
            '  </section>' +
            '  <section class="anl-card">' +
            '    <h3>颜色</h3>' +
            '    <div class="anl-seg" role="group" aria-label="颜色模式">' +
            '      <button type="button" data-color-mode="cover">封面取色</button>' +
            '      <button type="button" data-color-mode="manual">手动颜色</button>' +
            '    </div>' +
            '    <div class="anl-field anl-color-row" data-manual-row><label>原歌词颜色</label><input id="anl-color-original" data-manual-color type="color"></div>' +
            '    <div class="anl-field anl-color-row" data-manual-row><label>翻译颜色</label><input id="anl-color-translation" data-manual-color type="color"></div>' +
            '    <div class="anl-check"><label><input id="anl-outline-auto" type="checkbox"> 自动描边（随封面明暗）</label></div>' +
            '    <div class="anl-field anl-color-row"><label>原歌词描边</label><input id="anl-outline-original" data-outline-color type="color"></div>' +
            '    <div class="anl-field anl-color-row"><label>翻译描边</label><input id="anl-outline-translation" data-outline-color type="color"></div>' +
            '    <div class="anl-field"><label>描边粗细</label><div class="anl-range"><input id="anl-outline-width" type="range" min="0" max="4" step="0.1"><b id="anl-outline-width-val"></b></div></div>' +
            '  </section>' +
            '  <section class="anl-card">' +
            '    <h3>显示</h3>' +
            '    <div class="anl-field"><label>歌词透明度</label><div class="anl-range"><input id="anl-opacity" type="range" min="0.15" max="1" step="0.01"><b id="anl-opacity-val"></b></div></div>' +
            '    <div class="anl-field"><label>位置模式</label><select id="anl-mode"><option value="desktop">桌面模式</option><option value="taskbar">任务栏模式</option></select></div>' +
            '    <p class="anl-mode-note"></p>' +
            '    <div class="anl-field"><label>同步微调</label><div class="anl-range"><input id="anl-offset" type="range" min="-1000" max="1000" step="10"><b id="anl-offset-val"></b></div></div>' +
            '    <p class="anl-hint">悬停歌词可打开子菜单。桌面模式可拖动；任务栏模式永久锁定并左对齐。</p>' +
            '  </section>' +
            '</div>' +
            '<div class="anl-foot"><span>无原生 DLL · WPF 分层窗口</span><span class="anl-status"></span></div>';

        bind(s);
        ANL.Config.refresh = async function () {
            if (!root || !root.parentNode) return;
            var cur = await ANL.app.reloadSettings();
            var en = q("#anl-enabled");
            if (en) en.checked = !!cur.enabled;
            var md = q("#anl-mode");
            if (md) md.value = cur.mode;
            applyDisabled(cur);
        };
    }

    // Register the entry hooks here as well: this file is always among the
    // injected scripts, so startup/config cannot be skipped by the loader.
    plugin.onLoad(function () {
        return ANL.app.start();
    });
    plugin.onConfig(function () {
        return ANL.Config.render();
    });

    ANL.Config = {
        render: function () {
            var container = document.createElement("div");
            container.className = "anl-config";
            root = container;
            container.innerHTML = '<div class="anl-loading">正在载入 ANLyrics…</div>';

            (async function () {
                try {
                    var dir = ANL.app.dir || await ANL.io.dataDir();
                    var cssText = "";
                    try {
                        var pluginDir = ANL.io.pluginDir();
                        if (pluginDir) cssText = await ANL.io.readText(pluginDir + "\\config.css");
                    } catch (e) { }
                    if (cssText) {
                        var st = document.createElement("style");
                        st.textContent = cssText;
                        document.head.appendChild(st);
                    }
                    await build();
                } catch (e) {
                    console.warn("[ANLyrics] config load failed", e);
                    container.innerHTML = '<div class="anl-loading">配置载入失败：' + esc(e && e.message) + "</div>";
                }
            })();
            return container;
        }
    };
})();
