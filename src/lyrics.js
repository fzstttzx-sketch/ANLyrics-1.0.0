/*
 * ANLyrics — lyrics acquisition and parsing.
 * Primary source: installed liblyric. Fallback: NetEase public lyric endpoint.
 */
(function () {
    "use strict";

    var ANL = window.ANL = window.ANL || {};
    var cache = new Map();
    var CACHE_LIMIT = 12;

    function trim(v) { return typeof v === "string" ? v.trim() : ""; }

    function parseTimestamp(tag) {
        var m = tag.match(/^\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]$/);
        if (!m) return null;
        var min = parseInt(m[1], 10);
        var sec = parseInt(m[2], 10);
        var frac = m[3] || "0";
        if (frac.length === 1) frac += "00";
        else if (frac.length === 2) frac += "0";
        return min * 60 + sec + parseInt(frac.slice(0, 3), 10) / 1000;
    }

    function parseLrc(text, translatedText) {
        if (!text) return [];
        var offset = 0;
        var rows = String(text).replace(/\r/g, "").split("\n");
        var out = [];
        for (var i = 0; i < rows.length; i++) {
            var line = rows[i].trim();
            if (!line) continue;
            var om = line.match(/^\[offset:([+-]?\d+)\]$/i);
            if (om) { offset = parseInt(om[1], 10) / 1000; continue; }
            var tags = [];
            var rest = line;
            var tm;
            while ((tm = rest.match(/^\[[^\]]+\]/))) {
                var ts = parseTimestamp(tm[0]);
                if (ts !== null) tags.push(ts);
                rest = rest.slice(tm[0].length).trim();
            }
            if (!tags.length || !rest) continue;
            for (var j = 0; j < tags.length; j++) {
                out.push({ t: Math.max(0, tags[j] + offset), o: rest, tr: "" });
            }
        }
        out.sort(function (a, b) { return a.t - b.t; });

        if (translatedText) {
            var trs = parseLrc(translatedText, "");
            var byKey = {};
            for (var k = 0; k < trs.length; k++) {
                byKey[trs[k].t.toFixed(3)] = trs[k].o;
            }
            for (var q = 0; q < out.length; q++) {
                var hit = byKey[out[q].t.toFixed(3)];
                if (hit) out[q].tr = hit;
            }
        }

        // Collapse duplicate timestamps (NetEase occasionally repeats a chorus tag).
        var merged = [];
        for (var n = 0; n < out.length; n++) {
            var prev = merged[merged.length - 1];
            if (prev && Math.abs(prev.t - out[n].t) < 0.001) {
                if (!prev.tr && out[n].tr) prev.tr = out[n].tr;
                if (out[n].o && prev.o.indexOf(out[n].o) < 0) prev.o += " " + out[n].o;
            } else {
                merged.push(out[n]);
            }
        }
        return merged.filter(function (x) { return x.o || x.tr; });
    }

    function fromLibLyric(raw) {
        var lp = (typeof loadedPlugins !== "undefined" && loadedPlugins) ? loadedPlugins.liblyric : null;
        if (!lp || !raw) return [];
        var lrc = raw.lrc && raw.lrc.lyric ? raw.lrc.lyric : "";
        var tlyric = raw.tlyric && raw.tlyric.lyric ? raw.tlyric.lyric : "";
        var roma = raw.romalrc && raw.romalrc.lyric ? raw.romalrc.lyric : "";
        var lines = [];
        try {
            if (typeof lp.parseLyric === "function") {
                lines = lp.parseLyric(lrc, tlyric, roma) || [];
            }
        } catch (e) {
            console.warn("[ANLyrics] liblyric.parseLyric failed", e);
        }
        var out = [];
        for (var i = 0; i < lines.length; i++) {
            var x = lines[i];
            var o = trim(x.originalLyric);
            var tr = trim(x.translatedLyric);
            if (!o && !tr) continue;
            out.push({ t: Math.max(0, Number(x.time || 0) / 1000), o: o, tr: tr });
        }
        return out;
    }

    async function fetchText(url) {
        var res = await fetch(url, { credentials: "omit" });
        if (!res.ok) throw new Error("HTTP " + res.status);
        return await res.json();
    }

    async function fetchPublic(songId) {
        var lyricUrl = "https://music.163.com/api/song/lyric?id=" + encodeURIComponent(songId) +
            "&lv=-1&kv=-1&tv=-1";
        var detailUrl = "https://music.163.com/api/song/detail?ids=[" + encodeURIComponent(songId) + "]";
        var lyr = null, detail = null;
        try { lyr = await fetchText(lyricUrl); } catch (e) { console.warn("[ANLyrics] lyric api", e); }
        try { detail = await fetchText(detailUrl); } catch (e) { console.warn("[ANLyrics] detail api", e); }
        var song = detail && detail.songs && detail.songs[0] ? detail.songs[0] : null;
        var lines = [];
        if (lyr) {
            lines = parseLrc(lyr.lrc && lyr.lrc.lyric ? lyr.lrc.lyric : "",
                lyr.tlyric && lyr.tlyric.lyric ? lyr.tlyric.lyric : "");
        }
        return {
            lines: lines,
            cover: song && song.album ? (song.album.picUrl || song.album.blurPicUrl || "") : "",
            title: song ? song.name : "",
            artist: song && song.artists ? song.artists.map(function (a) { return a.name; }).join(" / ") : ""
        };
    }

    var lyrics = ANL.lyrics = {};

    lyrics.parseLrc = parseLrc;

    lyrics.get = async function (songId, coverFallback, titleFallback, artistFallback) {
        songId = String(songId);
        if (cache.has(songId)) return cache.get(songId);

        var doc = {
            songId: songId,
            title: titleFallback || "",
            artist: artistFallback || "",
            coverUrl: coverFallback || "",
            lines: []
        };

        var lp = (typeof loadedPlugins !== "undefined" && loadedPlugins) ? loadedPlugins.liblyric : null;
        if (lp && typeof lp.getLyricData === "function") {
            try {
                var raw = await lp.getLyricData(songId);
                doc.lines = fromLibLyric(raw);
            } catch (e) {
                console.warn("[ANLyrics] liblyric.getLyricData failed", e);
            }
        }

        if (!doc.lines.length) {
            try {
                var pub = await fetchPublic(songId);
                doc.lines = pub.lines;
                if (!doc.coverUrl) doc.coverUrl = pub.cover;
                if (!doc.title) doc.title = pub.title;
                if (!doc.artist) doc.artist = pub.artist;
            } catch (e) {
                console.warn("[ANLyrics] public lyric fallback failed", e);
            }
        }

        doc.lines.sort(function (a, b) { return a.t - b.t; });
        if (cache.size >= CACHE_LIMIT) cache.delete(cache.keys().next().value);
        cache.set(songId, doc);
        return doc;
    };

    lyrics.preload = function (songId, cover, title, artist) {
        if (!songId) return;
        lyrics.get(songId, cover, title, artist).catch(function () { });
    };

    lyrics.clear = function () { cache.clear(); };
})();
