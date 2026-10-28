/*
 * ANLyrics — playback bridge.
 * Load / PlayProgress / PlayState are the same native callbacks used by the
 * other lyrics plugins; no polling of the DOM is required.
 */
(function () {
    "use strict";

    var ANL = window.ANL = window.ANL || {};

    function playingSong() {
        try {
            var raw = betterncm.ncm.getPlayingSong();
            return raw && raw.data ? raw.data : null;
        } catch (e) {
            return null;
        }
    }

    function Player(opts) {
        this.opts = opts || {};
        this.playing = false;
        this.time = 0;
        this.duration = 0;
        this._songId = "";
        this._timer = null;
        this._bound = [];
        this._started = false;
        this._lastProgressAt = 0;
    }

    Player.prototype.guessTime = function (song) {
        try {
            var bar = document.querySelector(".prg .has");
            if (!bar) return 0;
            var pct = parseFloat(bar.style.width || "0");
            if (!isFinite(pct) || pct <= 0) return 0;
            var dur = this.duration;
            if (!dur && song && song.duration) dur = Number(song.duration) / 1000;
            if (!dur || !isFinite(dur)) return 0;
            return Math.max(0, Math.min(dur, dur * pct / 100));
        } catch (e) {
            return 0;
        }
    };

    Player.prototype.start = function () {
        if (this._started) return;
        this._started = true;
        var self = this;
        try {
            legacyNativeCmder.appendRegisterCall("Load", "audioplayer", function (_, info) {
                if (info && isFinite(info.duration)) self.duration = Number(info.duration);
                // The player object is updated just after the native Load call.
                setTimeout(function () { self._announceSong(); }, 60);
            });
            legacyNativeCmder.appendRegisterCall("PlayProgress", "audioplayer", function (_, time) {
                var t = Number(time);
                if (!isFinite(t)) return;
                self.time = Math.max(0, t);
                self._lastProgressAt = Date.now();
                if (!self.playing) {
                    self.playing = true;
                    if (self.opts.onState) self.opts.onState(true);
                }
                if (self.opts.onProgress) self.opts.onProgress(self.time, self.duration);
            });
            legacyNativeCmder.appendRegisterCall("PlayState", "audioplayer", function (_, __, state) {
                self.playing = Number(state) === 1;
                self._lastProgressAt = Date.now();
                if (self.opts.onState) self.opts.onState(self.playing);
            });
            this._bound.push("Load", "PlayProgress", "PlayState");
        } catch (e) {
            console.warn("[ANLyrics] native playback callbacks unavailable", e);
        }

        this._timer = setInterval(function () {
            self._pollSong();
            if (self.playing && self._lastProgressAt &&
                Date.now() - self._lastProgressAt > 2500) {
                self.playing = false;
                if (self.opts.onState) self.opts.onState(false);
            }
            if (self.opts.onHeartbeat) self.opts.onHeartbeat();
        }, 1500);
    };

    Player.prototype._announceSong = function () {
        var song = playingSong();
        if (!song || !song.id) return;
        this._songId = String(song.id);
        var guess = this.guessTime(song);
        if (guess > 0) this.time = guess;
        if (this.opts.onSong) this.opts.onSong(song, false);
    };

    Player.prototype._pollSong = function () {
        var song = playingSong();
        if (!song || !song.id) return;
        var id = String(song.id);
        if (id !== this._songId) {
            this._songId = id;
            this.time = this.guessTime(song);
            if (this.opts.onSong) this.opts.onSong(song, true);
        }
    };

    Player.prototype.stop = function () {
        if (this._timer) clearInterval(this._timer);
        this._timer = null;
        // BetterNCM has no documented unregister API across versions; the
        // callbacks are harmless once the app object is disposed.
    };

    ANL.Player = Player;
    ANL.playingSong = playingSong;
})();
