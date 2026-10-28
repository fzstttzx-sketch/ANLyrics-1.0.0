/*
 * ANLyrics — application orchestration.
 * Owns: settings, song changes, lyric preloading, heartbeat, overlay lifecycle.
 * Rendering and mouse interaction live entirely in the C# overlay.
 */
(function () {
    "use strict";

    var ANL = window.ANL = window.ANL || {};

    function artistText(a) {
        if (!a) return "";
        if (Array.isArray(a)) {
            return a.map(function (x) { return x && x.name ? x.name : String(x || ""); })
                .filter(Boolean).join(" / ");
        }
        return String(a);
    }

    function coverOf(song) {
        var al = song && song.album;
        if (!al) return "";
        return al.picUrl || al.blurPicUrl || al.pic || "";
    }

    function App() {
        this.dir = "";
        this.settings = ANL.normalize({});
        this.doc = null;
        this.song = null;
        this.time = 0;
        this.duration = 0;
        this.playing = false;
        this.index = -1;
        this.ready = false;
        this._player = null;
        this._lastWrite = 0;
        this._lastLaunch = 0;
        this._lastEnsure = 0;
        this._lastEventSeq = 0;
        this._started = false;
        this._launching = false;
        this._pendingSong = "";
    }

    App.prototype.start = function () {
        if (this._startPromise) return this._startPromise;
        var self = this;
        this._startPromise = (async function () {
        self._started = true;
        self.dir = await ANL.io.dataDir();
        await ANL.io.ensureDir(self.dir);

        var raw = await ANL.io.readJson(ANL.io.paths(self.dir).settings, null);
        if (!raw) {
            self.settings = ANL.normalize({});
            await ANL.io.saveSettings(self.dir, self.settings);
        } else {
            self.settings = ANL.normalize(raw);
        }
        try { plugin.setConfig("enabled", self.settings.enabled); } catch (e) { }
        try { plugin.setConfig("dataDir", self.dir); } catch (e) { }
        ANL.io.log("data dir", self.dir, "enabled", self.settings.enabled);

        var ev = await ANL.io.readEvents(self.dir);
        self._lastEventSeq = Number(ev.seq || 0);

        self._player = new ANL.Player({
            onSong: function (song) { self.onSong(song); },
            onProgress: function (t, d) { self.onProgress(t, d); },
            onState: function (p) { self.onState(p); },
            onHeartbeat: function () { self.onHeartbeat(); }
        });
        self._player.start();

        if (self.settings.enabled) {
            await self.launch(true);
            // Recover the currently playing song after a plugin reload.
            setTimeout(function () { self.recoverSong(); }, 400);
        }
        self.ready = true;
        })();
        return this._startPromise;
    };

    App.prototype.recoverSong = async function () {
        var song = ANL.playingSong();
        if (!song) return;
        if (this.song && String(song.id) === String(this.song.id)) return;
        await this.onSong(song, true);
    };

    App.prototype.onSong = async function (song, recovered) {
        if (!song || !song.id) return;
        var id = String(song.id);
        if (this.song && String(this.song.id) === id && this.doc && this.doc.songId === id) return;
        if (this._pendingSong === id) return;
        this._pendingSong = id;

        this.song = song;
        this.time = this._player && isFinite(this._player.time) ? Math.max(0, this._player.time) : 0;
        this.index = -1;

        this.doc = {
            songId: id,
            title: song.name || "",
            artist: artistText(song.artists),
            coverUrl: coverOf(song),
            lines: []
        };
        // Clear the previous line immediately; the overlay will fade it out.
        await this.writeNow(true);

        var title = this.doc.title;
        var artist = this.doc.artist;
        var cover = this.doc.coverUrl;
        var doc = await ANL.lyrics.get(id, cover, title, artist);
        if (this._pendingSong !== id) return;

        if (!doc.coverUrl && cover) doc.coverUrl = cover;
        if (!doc.title && title) doc.title = title;
        if (!doc.artist && artist) doc.artist = artist;
        this.doc = doc;

        await ANL.io.writeLyrics(this.dir, doc);
        ANL.io.log("lyrics", id, doc.lines.length, doc.coverUrl ? "cover" : "no-cover");
        this._pendingSong = "";
        this.index = this.findIndex(this.time);
        await this.writeNow(true);
    };

    App.prototype.onProgress = function (t, duration) {
        this.time = Number(t) || 0;
        if (duration && isFinite(duration)) this.duration = Number(duration);
        var idx = this.findIndex(this.time);
        var changed = idx !== this.index;
        this.index = idx;
        this.writeNow(changed);
    };

    App.prototype.onState = function (playing) {
        this.playing = !!playing;
        this.writeNow(true);
    };

    App.prototype.findIndex = function (t) {
        if (!this.doc || !this.doc.lines || !this.doc.lines.length) return -1;
        if (t < this.doc.lines[0].t - 0.25) return -1;
        var found = 0;
        for (var i = 0; i < this.doc.lines.length; i++) {
            if (this.doc.lines[i].t <= t + 0.02) found = i;
            else break;
        }
        return found;
    };

    App.prototype.nowState = function () {
        return {
            songId: this.song ? String(this.song.id) : "",
            time: Math.max(0, this.time || 0),
            playing: !!this.playing,
            index: this.index,
            updatedAt: Date.now()
        };
    };

    App.prototype.writeNow = async function (force) {
        var now = Date.now();
        if (!force && now - this._lastWrite < 110) return;
        this._lastWrite = now;
        await ANL.io.writeNow(this.dir, this.nowState());
    };

    App.prototype.onHeartbeat = async function () {
        this.writeNow(false);
        await this.watchEvents();
        if (!this.settings.enabled) return;
        await this.ensureOverlay();
    };

    App.prototype.ensureOverlay = async function () {
        var now = Date.now();
        if (now - this._lastEnsure < 2500) return;
        this._lastEnsure = now;

        var s = await ANL.io.readSession(this.dir);
        var alive = Number(s.aliveAt || 0);
        var running = s.running === true;
        if (running && now - alive < 9000) return;
        if (now - this._lastLaunch < 6000) return;
        ANL.io.log("overlay not alive; relaunch", { running: running, age: now - alive });
        await this.launch(true);
    };

    App.prototype.watchEvents = async function () {
        var ev = await ANL.io.readEvents(this.dir);
        var seq = Number(ev.seq || 0);
        if (!seq || seq <= this._lastEventSeq) return;
        this._lastEventSeq = seq;
        if (ev.type === "close") {
            this.settings.enabled = false;
            await ANL.io.saveSettings(this.dir, this.settings);
            try { plugin.setConfig("enabled", false); } catch (e) { }
            ANL.io.log("overlay closed by user");
            if (ANL.Config && ANL.Config.refresh) ANL.Config.refresh();
        }
    };

    App.prototype.launch = async function (force) {
        if (!this.settings.enabled) return false;
        if (this._launching) return false;
        var now = Date.now();
        if (!force && now - this._lastLaunch < 2500) return false;
        this._lastLaunch = now;
        this._launching = true;
        try {
            return await ANL.io.launchOverlay(this.dir);
        } finally {
            this._launching = false;
        }
    };

    App.prototype.applyPatch = async function (patch, opts) {
        opts = opts || {};
        var next = ANL.normalize(ANL.merge(await ANL.io.readSettings(this.dir), patch));
        var wasEnabled = this.settings.enabled;
        this.settings = next;
        await ANL.io.saveSettings(this.dir, next);
        try { plugin.setConfig("enabled", next.enabled); } catch (e) { }

        if (patch.mode && next.enabled) {
            await ANL.io.control(this.dir, "mode:" + next.mode);
        }
        if (!next.enabled) {
            await ANL.io.control(this.dir, "shutdown");
        } else if (!wasEnabled || opts.relaunch) {
            await this.launch(true);
        }
        return next;
    };

    App.prototype.setEnabled = async function (on) {
        return await this.applyPatch({ enabled: !!on });
    };

    App.prototype.closeOverlay = async function () {
        await this.applyPatch({ enabled: false });
    };

    App.prototype.reloadSettings = async function () {
        this.settings = await ANL.io.readSettings(this.dir);
        return this.settings;
    };

    ANL.App = App;
    ANL.app = new App();
})();
