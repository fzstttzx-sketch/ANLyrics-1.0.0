using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ANLyrics
{
    // ---------------------------------------------------------------------
    // Small utilities
    // ---------------------------------------------------------------------

    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static void Init(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "overlay.log");
                if (File.Exists(_path) && new FileInfo(_path).Length > 512 * 1024)
                {
                    try { File.Delete(_path + ".old"); } catch { }
                    try { File.Move(_path, _path + ".old"); } catch { }
                }
            }
            catch { _path = null; }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message + (ex == null ? "" : "\r\n" + ex));
        }

        private static void Write(string level, string message)
        {
            if (_path == null) return;
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(_path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        + " [" + level + "] " + message + "\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    public static class Json
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        static Json()
        {
            Ser.MaxJsonLength = int.MaxValue;
            Ser.RecursionLimit = 100;
        }

        public static Dictionary<string, object> Read(string path)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (!File.Exists(path)) return null;
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(text)) return null;
                    object obj = Ser.DeserializeObject(text);
                    return obj as Dictionary<string, object>;
                }
                catch (IOException)
                {
                    Thread.Sleep(8);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(8);
                }
                catch (Exception)
                {
                    Thread.Sleep(8);
                }
            }
            return null;
        }

        public static void Write(string path, object value)
        {
            string text = Ser.Serialize(value);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                try { File.Copy(tmp, path, true); } catch { }
                try { File.Delete(tmp); } catch { }
            }
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v;
            if (!d.TryGetValue(key, out v)) return null;
            return v as Dictionary<string, object>;
        }

        public static List<object> Arr(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            List<object> list = v as List<object>;
            if (list != null) return list;
            object[] array = v as object[];
            if (array != null) return new List<object>(array);
            IList any = v as IList;
            if (any != null)
            {
                list = new List<object>(any.Count);
                foreach (object item in any) list.Add(item);
                return list;
            }
            return null;
        }

        public static string Str(Dictionary<string, object> d, string key, string def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            string s = v as string;
            return s ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static double Num(Dictionary<string, object> d, string key, double def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        public static long Long(Dictionary<string, object> d, string key, long def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            try { return Convert.ToBoolean(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }
    }

    public static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_LAYERED = 0x00080000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        public static void MakeToolWindow(Window w, bool clickThrough, bool noActivate)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int style = GetWindowLong(h, GWL_EXSTYLE);
            style |= WS_EX_TOOLWINDOW;
            if (clickThrough) style |= WS_EX_TRANSPARENT;
            if (noActivate) style |= WS_EX_NOACTIVATE;
            SetWindowLong(h, GWL_EXSTYLE, style);
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static Rect GetWorkArea(Window w)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                IntPtr mon = MonitorFromWindow(h, 2); // MONITOR_DEFAULTTONEAREST
                if (mon != IntPtr.Zero)
                {
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(mon, ref mi))
                    {
                        double scale = VisualTreeHelper.GetDpi(w).DpiScaleX;
                        if (scale <= 0) scale = 1;
                        return new Rect(
                            mi.rcWork.Left / scale,
                            mi.rcWork.Top / scale,
                            (mi.rcWork.Right - mi.rcWork.Left) / scale,
                            (mi.rcWork.Bottom - mi.rcWork.Top) / scale);
                    }
                }
            }
            catch { }
            return SystemParameters.WorkArea;
        }

        public static Rect GetTaskbarRect(Window w)
        {
            try
            {
                APPBARDATA data = new APPBARDATA();
                data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
                IntPtr ok = SHAppBarMessage(0x00000005, ref data); // ABM_GETTASKBARPOS
                if (ok != IntPtr.Zero && data.rc.Right > data.rc.Left && data.rc.Bottom > data.rc.Top)
                {
                    double scale = VisualTreeHelper.GetDpi(w).DpiScaleX;
                    if (scale <= 0) scale = 1;
                    return new Rect(
                        data.rc.Left / scale,
                        data.rc.Top / scale,
                        (data.rc.Right - data.rc.Left) / scale,
                        (data.rc.Bottom - data.rc.Top) / scale);
                }
            }
            catch { }
            Rect work = GetWorkArea(w);
            return new Rect(0, work.Bottom, work.Width, 48);
        }
    }

    // ---------------------------------------------------------------------
    // Configuration / state
    // ---------------------------------------------------------------------

    public class FontSetting
    {
        public string Family;
        public double Size;

        public FontSetting(string family, double size)
        {
            Family = family;
            Size = size;
        }
    }

    public class Settings
    {
        public bool Enabled = true;
        public FontSetting OriginalFont = new FontSetting("Microsoft YaHei UI", 42);
        public FontSetting TranslationFont = new FontSetting("Microsoft YaHei UI", 22);
        public string ColorMode = "cover";
        public Color ManualOriginal = Colors.White;
        public Color ManualTranslation = Color.FromRgb(214, 232, 255);
        public bool OutlineAuto = true;
        public Color OutlineOriginal = Color.FromArgb(210, 0, 0, 0);
        public Color OutlineTranslation = Color.FromArgb(210, 0, 0, 0);
        public double OutlineWidth = 1.8;
        public double Opacity = 1.0;
        public double Gap = 8;
        public string Align = "center";
        public string Mode = "desktop";
        public int OffsetMs = 0;
        public double MaxWidthRatio = 0.92;

        public static Settings Load(string path)
        {
            Settings s = new Settings();
            Dictionary<string, object> d = Json.Read(path);
            if (d == null) return s;

            s.Enabled = Json.Bool(d, "enabled", s.Enabled);

            Dictionary<string, object> font = Json.Obj(d, "font");
            Dictionary<string, object> of = Json.Obj(font, "original");
            Dictionary<string, object> tf = Json.Obj(font, "translation");
            s.OriginalFont.Family = Json.Str(of, "family", s.OriginalFont.Family);
            s.OriginalFont.Size = Clamp(Json.Num(of, "size", s.OriginalFont.Size), 10, 160);
            s.TranslationFont.Family = Json.Str(tf, "family", s.TranslationFont.Family);
            s.TranslationFont.Size = Clamp(Json.Num(tf, "size", s.TranslationFont.Size), 8, 120);

            s.ColorMode = Json.Str(d, "colorMode", s.ColorMode);
            Dictionary<string, object> colors = Json.Obj(d, "colors");
            s.ManualOriginal = ParseColor(Json.Str(colors, "original", null), s.ManualOriginal);
            s.ManualTranslation = ParseColor(Json.Str(colors, "translation", null), s.ManualTranslation);

            Dictionary<string, object> outline = Json.Obj(d, "outline");
            s.OutlineAuto = Json.Bool(outline, "auto", s.OutlineAuto);
            s.OutlineOriginal = ParseColor(Json.Str(outline, "original", null), s.OutlineOriginal);
            s.OutlineTranslation = ParseColor(Json.Str(outline, "translation", null), s.OutlineTranslation);
            s.OutlineWidth = Clamp(Json.Num(outline, "width", s.OutlineWidth), 0, 8);

            s.Opacity = Clamp(Json.Num(d, "opacity", s.Opacity), 0.08, 1.0);
            s.Gap = Clamp(Json.Num(d, "gap", s.Gap), 0, 80);
            s.Align = NormalizeAlign(Json.Str(d, "align", s.Align));
            s.Mode = NormalizeMode(Json.Str(d, "mode", s.Mode));
            s.OffsetMs = (int)Clamp(Json.Num(d, "offsetMs", s.OffsetMs), -5000, 5000);
            s.MaxWidthRatio = Clamp(Json.Num(d, "maxWidthRatio", s.MaxWidthRatio), 0.5, 1.0);
            return s;
        }

        public Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> font = new Dictionary<string, object>();
            font["original"] = new Dictionary<string, object> {
                { "family", OriginalFont.Family }, { "size", OriginalFont.Size } };
            font["translation"] = new Dictionary<string, object> {
                { "family", TranslationFont.Family }, { "size", TranslationFont.Size } };

            Dictionary<string, object> colors = new Dictionary<string, object>();
            colors["original"] = ToHex(ManualOriginal);
            colors["translation"] = ToHex(ManualTranslation);

            Dictionary<string, object> outline = new Dictionary<string, object>();
            outline["auto"] = OutlineAuto;
            outline["original"] = ToHex(OutlineOriginal);
            outline["translation"] = ToHex(OutlineTranslation);
            outline["width"] = OutlineWidth;

            Dictionary<string, object> d = new Dictionary<string, object>();
            d["version"] = 1;
            d["enabled"] = Enabled;
            d["font"] = font;
            d["colorMode"] = ColorMode;
            d["colors"] = colors;
            d["outline"] = outline;
            d["opacity"] = Opacity;
            d["gap"] = Gap;
            d["align"] = Align;
            d["mode"] = Mode;
            d["offsetMs"] = OffsetMs;
            d["maxWidthRatio"] = MaxWidthRatio;
            return d;
        }

        public static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public static string NormalizeAlign(string a)
        {
            if (a == "left" || a == "right" || a == "center") return a;
            return "center";
        }

        public static string NormalizeMode(string m)
        {
            return m == "taskbar" ? "taskbar" : "desktop";
        }

        public static Color ParseColor(string text, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            try
            {
                object o = ColorConverter.ConvertFromString(text);
                if (o is Color) return (Color)o;
            }
            catch { }
            return fallback;
        }

        public static string ToHex(Color c)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}",
                c.A, c.R, c.G, c.B);
        }
    }

    public class Session
    {
        public string Mode = "desktop";
        public bool Locked = false;
        public double DesktopX = double.NaN;
        public double DesktopY = double.NaN;
        public long AliveAt = 0;
        public bool Running = true;
        public bool HasDesktop = false;

        public static Session Load(string path)
        {
            Session s = new Session();
            Dictionary<string, object> d = Json.Read(path);
            if (d == null) return s;
            s.Mode = Settings.NormalizeMode(Json.Str(d, "mode", "desktop"));
            s.Locked = Json.Bool(d, "locked", false);
            s.Running = Json.Bool(d, "running", true);
            s.AliveAt = Json.Long(d, "aliveAt", 0);
            Dictionary<string, object> desktop = Json.Obj(d, "desktop");
            if (desktop != null)
            {
                double x = Json.Num(desktop, "x", double.NaN);
                double y = Json.Num(desktop, "y", double.NaN);
                if (!double.IsNaN(x) && !double.IsNaN(y))
                {
                    s.DesktopX = x;
                    s.DesktopY = y;
                    s.HasDesktop = true;
                }
            }
            return s;
        }

        public void Save(string path)
        {
            AliveAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Dictionary<string, object> desktop = new Dictionary<string, object>();
            if (HasDesktop)
            {
                desktop["x"] = DesktopX;
                desktop["y"] = DesktopY;
            }
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["version"] = 1;
            d["mode"] = Mode;
            d["locked"] = Locked;
            d["desktop"] = desktop;
            d["aliveAt"] = AliveAt;
            d["running"] = Running;
            Json.Write(path, d);
        }
    }

    public class LyricLine
    {
        public double T;
        public string Original;
        public string Translation;

        public LyricLine(double t, string o, string tr)
        {
            T = t;
            Original = o ?? "";
            Translation = tr ?? "";
        }

        public string Key
        {
            get { return T.ToString("F3", CultureInfo.InvariantCulture) + "\n" + Original + "\n" + Translation; }
        }
    }

    public class LyricsDoc
    {
        public string SongId = "";
        public string Title = "";
        public string Artist = "";
        public string CoverUrl = "";
        public string Kind = "normal";
        public List<LyricLine> Lines = new List<LyricLine>();

        public static LyricsDoc Load(string path)
        {
            LyricsDoc doc = new LyricsDoc();
            Dictionary<string, object> d = Json.Read(path);
            if (d == null) return doc;
            doc.SongId = Json.Str(d, "songId", "");
            doc.Title = Json.Str(d, "title", "");
            doc.Artist = Json.Str(d, "artist", "");
            doc.CoverUrl = Json.Str(d, "coverUrl", "");
            doc.Kind = (Json.Str(d, "kind", "normal") ?? "normal").ToLowerInvariant();
            if (doc.Kind != "none" && doc.Kind != "instrumental") doc.Kind = "normal";
            List<object> lines = Json.Arr(d, "lines");
            if (lines != null)
            {
                foreach (object item in lines)
                {
                    Dictionary<string, object> ld = item as Dictionary<string, object>;
                    if (ld == null) continue;
                    double t = Json.Num(ld, "t", 0);
                    string o = Json.Str(ld, "o", "");
                    string tr = Json.Str(ld, "tr", "");
                    if (string.IsNullOrWhiteSpace(o) && string.IsNullOrWhiteSpace(tr)) continue;
                    doc.Lines.Add(new LyricLine(t, o, tr));
                }
            }
            doc.Lines.Sort(delegate (LyricLine a, LyricLine b) { return a.T.CompareTo(b.T); });
            return doc;
        }
    }

    public class NowState
    {
        public string SongId = "";
        public double Time = 0;
        public bool Playing = false;
        public int Index = -1;
        public long UpdatedAt = 0;
    }

    // ---------------------------------------------------------------------
    // Theme
    // ---------------------------------------------------------------------

    public class Palette
    {
        public Color Primary;
        public Color Secondary;
        public double Luminance;
        public bool IsDark;
        public bool IsVeryDark;
        public bool IsGray;
        public bool HasAccent;
    }

    public static class ColorUtil
    {
        public static double Lum(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        public static Color WithAlpha(Color c, byte a)
        {
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Lighten(Color c, double amount)
        {
            return Mix(c, Colors.White, amount);
        }

        public static Color Darken(Color c, double amount)
        {
            return Mix(c, Colors.Black, amount);
        }

        public static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            if (Math.Abs(max - min) < 0.0001)
            {
                h = 0; s = 0;
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6.0 : 0.0);
                else if (max == g) h = (b - r) / d + 2.0;
                else h = (r - g) / d + 4.0;
                h /= 6.0;
            }
        }

        public static Color FromHsl(double h, double s, double l)
        {
            double r, g, b;
            if (s < 0.0001) { r = g = b = l; }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = Hue(p, q, h + 1.0 / 3.0);
                g = Hue(p, q, h);
                b = Hue(p, q, h - 1.0 / 3.0);
            }
            return Color.FromRgb(
                (byte)Math.Round(Clamp01(r) * 255),
                (byte)Math.Round(Clamp01(g) * 255),
                (byte)Math.Round(Clamp01(b) * 255));
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        public static double Clamp01(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        public static Color Adjust(Color c, double satAdd, double light)
        {
            double h, s, l;
            ToHsl(c, out h, out s, out l);
            s = Clamp01(s + satAdd);
            l = Clamp01(light);
            return FromHsl(h, s, l);
        }

        public static Color Contrast(Color c)
        {
            return Lum(c) > 0.55 ? Color.FromArgb(220, 0, 0, 0) : Color.FromArgb(230, 255, 255, 255);
        }

        public static Color Readable(Color c)
        {
            double h, s, l;
            ToHsl(c, out h, out s, out l);
            if (l < 0.48) l = 0.62;
            if (l > 0.82) l = 0.78;
            if (s < 0.28) s = 0.28;
            return FromHsl(h, s, l);
        }

        public static double HueDistance(double a, double b)
        {
            double d = Math.Abs(a - b);
            return d > 0.5 ? 1.0 - d : d;
        }
    }

    public class Theme
    {
        public Brush OriginalFill;
        public Brush TranslationFill;
        public Pen OriginalPen;
        public Pen TranslationPen;
        public Color OriginalColor;
        public Color TranslationColor;
        public bool DarkFallback;
    }

    public class ThemeEngine
    {
        private readonly Settings _settings;
        private readonly Dispatcher _dispatcher;
        private readonly string _cacheDir;
        private readonly object _gate = new object();
        private readonly Dictionary<string, DateTime> _failed = new Dictionary<string, DateTime>();
        private readonly HashSet<string> _loading = new HashSet<string>();
        private Palette _palette;
        private string _paletteSongId = "";
        public event Action Changed;

        public ThemeEngine(Settings settings, Dispatcher dispatcher, string cacheDir)
        {
            _settings = settings;
            _dispatcher = dispatcher;
            _cacheDir = cacheDir;
            try { Directory.CreateDirectory(_cacheDir); } catch { }
        }

        public void RequestCover(string url, string songId)
        {
            if (string.IsNullOrWhiteSpace(songId)) return;
            if (_paletteSongId == songId && _palette != null) return;
            lock (_gate)
            {
                if (_loading.Contains(songId)) return;
                DateTime failedAt;
                if (_failed.TryGetValue(songId, out failedAt) &&
                    (DateTime.UtcNow - failedAt).TotalMinutes < 5) return;
                _loading.Add(songId);
            }

            string local = Path.Combine(_cacheDir, SafeName(songId) + ".img");
            string urlCopy = url;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Palette p = null;
                try
                {
                    if (File.Exists(local) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(local)).TotalDays < 30)
                    {
                        p = PaletteExtractor.FromFile(local);
                    }
                    if (p == null && !string.IsNullOrWhiteSpace(urlCopy))
                    {
                        byte[] bytes = Download(urlCopy);
                        if (bytes != null && bytes.Length > 100)
                        {
                            try { File.WriteAllBytes(local, bytes); } catch { }
                            p = PaletteExtractor.FromBytes(bytes);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("cover load failed: " + songId, ex);
                }

                lock (_gate)
                {
                    _loading.Remove(songId);
                    if (p == null) _failed[songId] = DateTime.UtcNow;
                }

                if (p != null)
                {
                    _dispatcher.BeginInvoke(new Action(delegate
                    {
                        _palette = p;
                        _paletteSongId = songId;
                        Log.Info("theme ready: " + songId + " dark=" + p.IsDark + " veryDark=" + p.IsVeryDark
                            + " gray=" + p.IsGray + " accent=" + p.HasAccent
                            + " primary=" + Settings.ToHex(p.Primary) + " secondary=" + Settings.ToHex(p.Secondary));
                        if (Changed != null) Changed();
                    }));
                }
            });
        }

        public Theme Build()
        {
            Theme t = new Theme();
            Palette p = _palette;
            bool useCover = _settings.ColorMode == "cover" && p != null;

            if (useCover)
            {
                t.DarkFallback = p.IsDark || p.IsVeryDark;
                if (p.IsVeryDark)
                {
                    // Genuinely black/near-black covers keep the guaranteed
                    // white + black-outline fallback.
                    t.OriginalFill = Gradient(
                        Color.FromRgb(255, 255, 255),
                        Color.FromRgb(232, 243, 255),
                        Color.FromRgb(203, 226, 255));
                    t.TranslationFill = Gradient(
                        Color.FromRgb(255, 255, 255),
                        Color.FromRgb(221, 237, 255),
                        Color.FromRgb(190, 216, 250));
                    t.OriginalColor = Colors.White;
                    t.TranslationColor = Color.FromRgb(221, 237, 255);
                }
                else if (p.IsGray)
                {
                    t.OriginalFill = Gradient(
                        Color.FromRgb(255, 255, 255),
                        Color.FromRgb(240, 244, 250),
                        Color.FromRgb(214, 224, 236));
                    t.TranslationFill = Gradient(
                        Color.FromRgb(250, 252, 255),
                        Color.FromRgb(226, 234, 244),
                        Color.FromRgb(200, 212, 228));
                    t.OriginalColor = Color.FromRgb(246, 249, 255);
                    t.TranslationColor = Color.FromRgb(226, 234, 244);
                }
                else if (p.IsDark)
                {
                    // Dark but still chromatic: use the dominant cover hue as
                    // the anchor, not whatever small bright object won the
                    // accent score. This keeps a blue cover blue.
                    Color anchor = ColorUtil.Readable(p.Primary);
                    Color second = p.HasAccent
                        ? ColorUtil.Readable(p.Secondary)
                        : ColorUtil.Lighten(anchor, 0.16);
                    Color baseColor = ColorUtil.Mix(anchor, Colors.White, 0.26);
                    t.OriginalFill = Gradient(
                        ColorUtil.Lighten(baseColor, 0.44),
                        baseColor,
                        ColorUtil.Mix(baseColor, second, 0.52),
                        second);
                    Color translationBase = ColorUtil.Mix(anchor, Colors.White, 0.80);
                    t.TranslationFill = Gradient(
                        ColorUtil.Lighten(translationBase, 0.16),
                        translationBase,
                        ColorUtil.Mix(translationBase, second, 0.16),
                        ColorUtil.Mix(translationBase, second, 0.32));
                    t.OriginalColor = baseColor;
                    t.TranslationColor = translationBase;
                }
                else
                {
                    Color a = ColorUtil.Readable(p.Primary);
                    Color b = ColorUtil.Readable(p.Secondary);
                    t.OriginalFill = Gradient(
                        ColorUtil.Lighten(a, 0.58),
                        a,
                        ColorUtil.Mix(a, b, 0.88),
                        ColorUtil.Lighten(b, 0.16));
                    // Translation follows the same diagonal light, but stays
                    // closer to white so it is calmer than the main line.
                    Color translationBase = ColorUtil.Mix(a, Colors.White, 0.62);
                    Color translationBottom = ColorUtil.Mix(translationBase, b, 0.44);
                    t.TranslationFill = Gradient(
                        ColorUtil.Lighten(translationBase, 0.26),
                        translationBase,
                        ColorUtil.Mix(translationBase, b, 0.24),
                        translationBottom);
                    t.OriginalColor = a;
                    t.TranslationColor = translationBase;
                }
            }
            else
            {
                t.DarkFallback = false;
                t.OriginalFill = new SolidColorBrush(_settings.ManualOriginal);
                t.TranslationFill = new SolidColorBrush(_settings.ManualTranslation);
                t.OriginalColor = _settings.ManualOriginal;
                t.TranslationColor = _settings.ManualTranslation;
            }

            t.OriginalFill.Freeze();
            t.TranslationFill.Freeze();

            Color oc = _settings.OutlineAuto
                ? ColorUtil.Contrast(t.OriginalColor)
                : _settings.OutlineOriginal;
            Color tc = _settings.OutlineAuto
                ? ColorUtil.Contrast(t.TranslationColor)
                : _settings.OutlineTranslation;

            if (useCover && (p.IsDark || p.IsVeryDark))
            {
                oc = Color.FromArgb(235, 0, 0, 0);
                tc = Color.FromArgb(235, 0, 0, 0);
            }

            t.OriginalPen = MakePen(oc);
            t.TranslationPen = MakePen(tc);
            return t;
        }

        private Pen MakePen(Color c)
        {
            double w = _settings.OutlineWidth;
            if (w <= 0.01) return null;
            Pen pen = new Pen(new SolidColorBrush(c), w);
            pen.LineJoin = PenLineJoin.Round;
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.Freeze();
            return pen;
        }

        private static Brush Gradient(Color top, Color mid, Color bottom)
        {
            return Gradient(top, ColorUtil.Mix(top, mid, 0.5), mid, bottom);
        }

        private static Brush Gradient(Color top, Color upper, Color lower, Color bottom)
        {
            // A diagonal ramp reads as an actual gradient on a single line;
            // a pure top-to-bottom ramp is almost invisible at lyric sizes.
            LinearGradientBrush b = new LinearGradientBrush();
            b.StartPoint = new Point(0, 0);
            b.EndPoint = new Point(1, 1);
            b.GradientStops.Add(new GradientStop(top, 0));
            b.GradientStops.Add(new GradientStop(upper, 0.34));
            b.GradientStops.Add(new GradientStop(lower, 0.68));
            b.GradientStops.Add(new GradientStop(bottom, 1));
            return b;
        }

        private static string SafeName(string s)
        {
            StringBuilder b = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') b.Append(c);
                else b.Append('_');
            }
            return b.Length == 0 ? "cover" : b.ToString();
        }

        private static byte[] Download(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "Mozilla/5.0 ANLyrics";
                req.AllowAutoRedirect = true;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream stream = resp.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int n;
                    while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) ms.Write(buffer, 0, n);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log.Error("cover download failed", ex);
                return null;
            }
        }
    }

    public static class PaletteExtractor
    {
        private class Bucket
        {
            public long Count;
            public double R;
            public double G;
            public double B;
            public double Sat;
        }

        public static Palette FromFile(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return FromStream(fs);
                }
            }
            catch (Exception ex)
            {
                Log.Error("palette file", ex);
                return null;
            }
        }

        public static Palette FromBytes(byte[] bytes)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    return FromStream(ms);
                }
            }
            catch (Exception ex)
            {
                Log.Error("palette bytes", ex);
                return null;
            }
        }

        private static Color Representative(Dictionary<int, Bucket> buckets, double hue, double radius)
        {
            long region = 0;
            foreach (Bucket b in buckets.Values)
            {
                Color c = Color.FromRgb(
                    (byte)(b.R / b.Count),
                    (byte)(b.G / b.Count),
                    (byte)(b.B / b.Count));
                double h, s, l;
                ColorUtil.ToHsl(c, out h, out s, out l);
                if (ColorUtil.HueDistance(h, hue) <= radius) region += b.Count;
            }
            if (region <= 0) return ColorUtil.FromHsl(hue, 0.42, 0.58);

            Bucket best = null;
            double bestScore = -1;
            foreach (Bucket b in buckets.Values)
            {
                if (b.Count < Math.Max(4L, (long)(region * 0.06))) continue;
                Color c = Color.FromRgb(
                    (byte)(b.R / b.Count),
                    (byte)(b.G / b.Count),
                    (byte)(b.B / b.Count));
                double h, s, l;
                ColorUtil.ToHsl(c, out h, out s, out l);
                if (ColorUtil.HueDistance(h, hue) > radius) continue;
                // Prefer a real, saturated cloth/leaf colour over the muddy
                // average of leaves, sky and haze. The count floor keeps a few
                // stray pixels from becoming the whole lyric theme.
                double score = b.Count * Math.Pow(s, 1.35);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            if (best == null) return ColorUtil.FromHsl(hue, 0.42, 0.58);
            return Color.FromRgb(
                (byte)(best.R / best.Count),
                (byte)(best.G / best.Count),
                (byte)(best.B / best.Count));
        }

        private static Palette FromStream(Stream stream)
        {
            BitmapImage img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = 96;
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();

            BitmapSource src = img;
            if (src.Format != PixelFormats.Bgra32)
            {
                FormatConvertedBitmap conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                src = conv;
            }

            int w = src.PixelWidth;
            int h = src.PixelHeight;
            int stride = w * 4;
            byte[] px = new byte[stride * h];
            src.CopyPixels(px, stride, 0);

            Dictionary<int, Bucket> buckets = new Dictionary<int, Bucket>();
            double[] hueWeight = new double[72];
            double totalLum = 0;
            long total = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    byte b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                    if (a < 160) continue;
                    double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
                    double max = Math.Max(rf, Math.Max(gf, bf));
                    double min = Math.Min(rf, Math.Min(gf, bf));
                    double lum = 0.2126 * rf + 0.7152 * gf + 0.0722 * bf;
                    totalLum += lum;
                    total++;
                    if (lum < 0.035 || lum > 0.97) continue;
                    double sat = max <= 0.001 ? 0 : (max - min) / max;
                    if (sat < 0.10) continue;

                    double hh, hs, hl;
                    ColorUtil.ToHsl(Color.FromRgb(r, g, b), out hh, out hs, out hl);
                    int hb = (int)Math.Floor(hh * 72.0);
                    if (hb < 0) hb = 0;
                    if (hb >= 72) hb = 71;
                    // Weight by area and saturation. Raw RGB buckets split a
                    // large autumn canopy across many orange/yellow buckets,
                    // while a uniform sky occupies one bucket; the hue
                    // histogram prevents that from winning by accident.
                    hueWeight[hb] += Math.Pow(0.30 + sat, 1.15);

                    int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                    Bucket bucket;
                    if (!buckets.TryGetValue(key, out bucket))
                    {
                        bucket = new Bucket();
                        buckets[key] = bucket;
                    }
                    bucket.Count++;
                    bucket.R += r;
                    bucket.G += g;
                    bucket.B += b;
                    bucket.Sat += sat;
                }
            }

            double avg = total == 0 ? 0.5 : totalLum / total;
            if (buckets.Count == 0)
            {
                Palette gray = new Palette();
                gray.Primary = Color.FromRgb(236, 240, 247);
                gray.Secondary = Color.FromRgb(206, 218, 232);
                gray.Luminance = avg;
                gray.IsDark = avg < 0.22;
                gray.IsVeryDark = avg < 0.12;
                gray.IsGray = true;
                gray.HasAccent = false;
                return gray;
            }

            // Smooth the 5-degree hue histogram so a large canopy split over
            // several orange/yellow buckets still reads as one dominant hue.
            double[] smooth = new double[72];
            for (int i = 0; i < 72; i++)
            {
                smooth[i] = hueWeight[(i + 71) % 72] * 0.22
                    + hueWeight[i]
                    + hueWeight[(i + 1) % 72] * 0.22;
            }

            int primaryBin = 0;
            for (int i = 1; i < 72; i++)
            {
                if (smooth[i] > smooth[primaryBin]) primaryBin = i;
            }
            double primaryHue = (primaryBin + 0.5) / 72.0;
            double primaryWeight = smooth[primaryBin];

            int secondaryBin = -1;
            double secondaryWeight = 0;
            for (int i = 0; i < 72; i++)
            {
                double hue = (i + 0.5) / 72.0;
                if (ColorUtil.HueDistance(hue, primaryHue) < 0.085) continue;
                if (smooth[i] > secondaryWeight)
                {
                    secondaryWeight = smooth[i];
                    secondaryBin = i;
                }
            }

            // A second hue only participates when it is genuinely present and
            // belongs to the same visual family. A small complementary patch
            // (blue sky, teal dress) must not turn an autumn cover cyan.
            double secondaryHue = secondaryBin >= 0 ? (secondaryBin + 0.5) / 72.0 : primaryHue;
            double secondaryHueDistance = ColorUtil.HueDistance(secondaryHue, primaryHue);
            bool sameFamily = secondaryHueDistance <= 0.20;
            bool overwhelming = secondaryWeight >= primaryWeight * 0.85;
            bool hasSecond = secondaryBin >= 0
                && secondaryWeight >= primaryWeight * 0.58
                && (sameFamily || overwhelming);

            Color primary = Representative(buckets, primaryHue, 0.055);
            double ph, ps, pl;
            ColorUtil.ToHsl(primary, out ph, out ps, out pl);

            Color secondary = hasSecond
                ? Representative(buckets, secondaryHue, 0.045)
                : ColorUtil.FromHsl(
                    (primaryHue + 0.055) % 1.0,
                    Math.Min(1.0, ps + 0.06),
                    Math.Min(0.80, pl + 0.10));

            double sh, ss, sl;
            ColorUtil.ToHsl(secondary, out sh, out ss, out sl);

            double primaryLum = ColorUtil.Lum(primary);
            double secondaryLum = ColorUtil.Lum(secondary);

            // Broad "dark" classification keeps more non-black covers tinted;
            // only a genuinely near-black image loses the cover palette.
            bool veryDark = avg < 0.10;
            bool hasAccent = hasSecond && secondaryLum >= 0.24 && ss >= 0.12;
            bool dark = avg < 0.32 || primaryLum < 0.19;
            bool grayish = ps < 0.12 && !hasAccent;

            Palette result = new Palette();
            result.Primary = primary;
            result.Secondary = secondary;
            result.Luminance = avg;
            result.IsDark = dark;
            result.IsVeryDark = veryDark;
            result.IsGray = grayish;
            result.HasAccent = hasAccent;
            return result;
        }
    }

    public static class FontCatalog
    {
        public static void Write(string path)
        {
            try
            {
                List<string> names = new List<string>();
                foreach (FontFamily f in Fonts.SystemFontFamilies)
                {
                    if (!string.IsNullOrWhiteSpace(f.Source) && !f.Source.StartsWith("@"))
                        names.Add(f.Source);
                }
                // WPF's own collection misses some per-user fonts (especially
                // fonts installed into %LOCALAPPDATA%\Microsoft\Windows\Fonts).
                // GDI+ enumerates those as well.
                try
                {
                    InstalledFontCollection gdi = new InstalledFontCollection();
                    foreach (System.Drawing.FontFamily f in gdi.Families)
                    {
                        if (!string.IsNullOrWhiteSpace(f.Name) && !f.Name.StartsWith("@"))
                            names.Add(f.Name);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("font catalog gdi", ex);
                }
                names = names.Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(delegate (string s) { return s; }, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                d["fonts"] = names;
                Json.Write(path, d);
            }
            catch (Exception ex)
            {
                Log.Error("font catalog", ex);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Lyric rendering
    // ---------------------------------------------------------------------

    public class TextArt
    {
        public Geometry Geometry;
        public Brush Fill;
        public Pen Pen;
        public double X;
        public double Y;
        public double Width;
        public double Height;
        public double Baseline;
    }

    public class LyricLayout
    {
        public List<TextArt> Arts = new List<TextArt>();
        public double Width;
        public double Height;
        public double ReservedHeight;
    }

    public class LyricView : FrameworkElement
    {
        public const double Pad = 12;
        private LyricLayout _layout;
        private Settings _settings;
        private Theme _theme;
        private LyricLine _line;
        private bool _compact;
        private double _maxWidth;
        private double _fixedHeight;

        public double ContentWidth { get { return _layout == null ? 0 : _layout.Width; } }
        public double ContentHeight { get { return _layout == null ? 0 : _layout.Height; } }
        public double ReservedHeight { get { return _layout == null ? 0 : _layout.ReservedHeight; } }
        public LyricLine Line { get { return _line; } }
        public bool HasContent { get { return _layout != null && _layout.Arts.Count > 0; } }

        public LyricView()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
        }

        public void SetLine(LyricLine line, Settings settings, Theme theme,
            bool compact, double maxWidth, double fixedHeight)
        {
            _line = line;
            _settings = settings;
            _theme = theme;
            _compact = compact;
            _maxWidth = Math.Max(40, maxWidth);
            _fixedHeight = fixedHeight;
            _layout = BuildLayout();
            InvalidateVisual();
        }

        public void Clear()
        {
            _line = null;
            _layout = null;
            InvalidateVisual();
        }

        public static double MeasureReserved(Settings settings)
        {
            if (settings == null) return 80;
            try
            {
                FormattedText fo = MakeText("Ag国", settings.OriginalFont.Family,
                    settings.OriginalFont.Size, FontWeights.SemiBold);
                FormattedText ft = MakeText("Ag国", settings.TranslationFont.Family,
                    settings.TranslationFont.Size, FontWeights.Normal);
                double h = 0;
                if (fo != null) h += fo.Height;
                if (ft != null) h += settings.Gap + ft.Height;
                return Math.Max(24, h);
            }
            catch
            {
                return 80;
            }
        }

        private double AlignOffset(double groupWidth, double artWidth)
        {
            string align = Settings.NormalizeAlign(_settings == null ? "center" : _settings.Align);
            if (align == "left") return 0;
            if (align == "right") return Math.Max(0, groupWidth - artWidth);
            return Math.Max(0, (groupWidth - artWidth) / 2.0);
        }

        private LyricLayout BuildLayout()
        {
            LyricLayout layout = new LyricLayout();
            if (_line == null || _settings == null || _theme == null) return layout;

            string o = (_line.Original ?? "").Trim();
            string tr = (_line.Translation ?? "").Trim();
            if (o.Length == 0 && tr.Length == 0) return layout;

            double oSize = _settings.OriginalFont.Size;
            double tSize = _settings.TranslationFont.Size;
            if (_compact)
            {
                // Taskbar mode uses the opposite emphasis: translation large,
                // original small. Keep desktop mode untouched.
                double swap = oSize;
                oSize = tSize;
                tSize = swap;
            }
            string oFam = _settings.OriginalFont.Family;
            string tFam = _settings.TranslationFont.Family;

            FormattedText fo = MakeText(o, oFam, oSize, FontWeights.SemiBold);
            FormattedText ft = MakeText(tr, tFam, tSize, FontWeights.Normal);

            double naturalWidth = Math.Max(fo == null ? 0 : fo.WidthIncludingTrailingWhitespace,
                ft == null ? 0 : ft.WidthIncludingTrailingWhitespace);
            double naturalHeight = Math.Max(fo == null ? 0 : fo.Height, ft == null ? 0 : ft.Height);

            double scale = 1.0;
            if (naturalWidth > _maxWidth && naturalWidth > 0)
                scale = Math.Min(scale, _maxWidth / naturalWidth);

            double compactGap = 0;
            if (_compact)
            {
                compactGap = Math.Max(1, Math.Min(6, _settings.Gap * 0.3));
                if (_fixedHeight > 0)
                {
                    double need = (fo == null ? 0 : fo.Height) + compactGap +
                        (ft == null ? 0 : ft.Height);
                    if (need > _fixedHeight && need > 0)
                        scale = Math.Min(scale, _fixedHeight / need);
                }
            }
            else if (_fixedHeight > 0 && naturalHeight > _fixedHeight && naturalHeight > 0)
            {
                scale = Math.Min(scale, _fixedHeight / naturalHeight);
            }

            if (scale < 0.999)
            {
                double ns = Math.Max(8, oSize * scale);
                double nt = Math.Max(7, tSize * scale);
                fo = MakeText(o, oFam, ns, FontWeights.SemiBold);
                ft = MakeText(tr, tFam, nt, FontWeights.Normal);
            }

            double oW = fo == null ? 0 : fo.WidthIncludingTrailingWhitespace;
            double oH = fo == null ? 0 : fo.Height;
            double tW = ft == null ? 0 : ft.WidthIncludingTrailingWhitespace;
            double tH = ft == null ? 0 : ft.Height;
            double oDrawSize = Math.Max(8, oSize * scale);
            double tDrawSize = Math.Max(7, tSize * scale);

            double gap = _settings.Gap;

            if (_compact)
            {
                // Taskbar mode: original on top, translation directly below,
                // always left aligned. The pair is scaled to the taskbar height.
                double groupW = Math.Max(oW, tW);
                double groupH = 0;
                if (fo != null && o.Length > 0) groupH += oH;
                if (ft != null && tr.Length > 0)
                {
                    if (groupH > 0) groupH += compactGap;
                    groupH += tH;
                }
                double y0 = _fixedHeight > 0 ? (_fixedHeight - groupH) / 2.0 : 0;
                if (y0 < 0) y0 = 0;

                if (fo != null && o.Length > 0)
                {
                    Pen oPen = oDrawSize < 16 ? null : ScalePen(_theme.OriginalPen, oDrawSize);
                    TextArt art = MakeArt(fo, _theme.OriginalFill, oPen, 0, y0);
                    if (art != null) layout.Arts.Add(art);
                }
                if (ft != null && tr.Length > 0)
                {
                    double ty = y0 + (fo != null && o.Length > 0 ? oH + compactGap : 0);
                    // At taskbar sizes the smaller line is only ~12 px tall; a
                    // normal outline swallows the glyph. Keep the fill clean.
                    Pen tPen = tDrawSize < 16 ? null : ScalePen(_theme.TranslationPen, tDrawSize);
                    TextArt art = MakeArt(ft, _theme.TranslationFill, tPen, 0, ty);
                    if (art != null) layout.Arts.Add(art);
                }
                layout.Width = groupW;
                layout.Height = groupH;
                layout.ReservedHeight = _fixedHeight > 0 ? _fixedHeight : groupH;
                return layout;
            }

            double groupWidth = Math.Max(oW, tW);
            double groupHeight = 0;
            if (fo != null && o.Length > 0) groupHeight += oH;
            if (ft != null && tr.Length > 0)
            {
                if (groupHeight > 0) groupHeight += gap;
                groupHeight += tH;
            }

            double reserved = _fixedHeight > 0 ? _fixedHeight : groupHeight;
            double top = (reserved - groupHeight) / 2.0;
            if (top < 0) top = 0;

            if (fo != null && o.Length > 0)
            {
                TextArt art = MakeArt(fo, _theme.OriginalFill,
                    ScalePen(_theme.OriginalPen, oDrawSize), 0, top);
                if (art != null)
                {
                    art.X = AlignOffset(groupWidth, art.Width);
                    layout.Arts.Add(art);
                }
            }
            if (ft != null && tr.Length > 0)
            {
                double ty = top + (fo != null && o.Length > 0 ? oH + gap : 0);
                TextArt art = MakeArt(ft, _theme.TranslationFill,
                    ScalePen(_theme.TranslationPen, tDrawSize), 0, ty);
                if (art != null)
                {
                    art.X = AlignOffset(groupWidth, art.Width);
                    layout.Arts.Add(art);
                }
            }

            layout.Width = groupWidth;
            layout.Height = groupHeight;
            layout.ReservedHeight = reserved;
            return layout;
        }

        private static FormattedText MakeText(string text, string family, double size, FontWeight weight)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                FontFamily ff = new FontFamily(family);
                Typeface tf = new Typeface(ff, FontStyles.Normal, weight, FontStretches.Normal);
                return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    tf, Math.Max(6, size), Brushes.White, 1.0);
            }
            catch
            {
                try
                {
                    Typeface tf = new Typeface(new FontFamily("Microsoft YaHei UI"),
                        FontStyles.Normal, weight, FontStretches.Normal);
                    return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        tf, Math.Max(6, size), Brushes.White, 1.0);
                }
                catch { return null; }
            }
        }

        private static Pen ScalePen(Pen pen, double fontSize)
        {
            if (pen == null) return null;
            double w = Math.Max(0.6, pen.Thickness * Math.Min(1.0, fontSize / 28.0));
            Pen p = new Pen(pen.Brush, w);
            p.LineJoin = pen.LineJoin;
            p.StartLineCap = pen.StartLineCap;
            p.EndLineCap = pen.EndLineCap;
            p.Freeze();
            return p;
        }

        private static TextArt MakeArt(FormattedText text, Brush fill, Pen pen, double x, double y)
        {
            try
            {
                Geometry g = text.BuildGeometry(new Point(0, 0));
                if (g == null) return null;
                g.Freeze();
                TextArt art = new TextArt();
                art.Geometry = g;
                art.Fill = fill;
                art.Pen = pen;
                art.X = x;
                art.Y = y;
                art.Width = text.WidthIncludingTrailingWhitespace;
                art.Height = text.Height;
                art.Baseline = text.Baseline;
                return art;
            }
            catch (Exception ex)
            {
                Log.Error("build geometry", ex);
                return null;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (_layout == null || _layout.Arts.Count == 0) return;
            double y = (ActualHeight - _layout.Height) / 2.0;
            if (y < 0) y = 0;
            foreach (TextArt art in _layout.Arts)
            {
                dc.PushTransform(new TranslateTransform(Pad + art.X, y + art.Y));
                // Outline first, fill on top: keeps the stroke outside the glyph
                // so narrow/small translation text does not turn into a dark blob.
                if (art.Pen != null) dc.DrawGeometry(null, art.Pen, art.Geometry);
                dc.DrawGeometry(art.Fill, null, art.Geometry);
                dc.Pop();
            }
        }
    }

    // ---------------------------------------------------------------------
    // Windows
    // ---------------------------------------------------------------------

    public class LyricWindow : Window
    {
        public readonly Grid Root;
        public readonly LyricView View;

        public LyricWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.Manual;
            IsHitTestVisible = false;
            Focusable = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            Root = new Grid();
            Root.Background = Brushes.Transparent;
            Root.IsHitTestVisible = false;
            View = new LyricView();
            Root.Children.Add(View);
            Content = Root;

            SourceInitialized += delegate
            {
                Native.MakeToolWindow(this, true, true);
            };
        }

        public void SetGlobalOpacity(double value)
        {
            Root.Opacity = Settings.Clamp(value, 0.05, 1.0);
        }

        public void SetFade(double value)
        {
            View.Opacity = Settings.Clamp(value, 0, 1);
        }
    }

    public class HitWindow : Window
    {
        private static readonly Brush InvisibleHit = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        public HitWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = InvisibleHit;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.Manual;
            Focusable = false;
            Cursor = Cursors.SizeAll;

            Border b = new Border();
            b.Background = InvisibleHit;
            Content = b;

            SourceInitialized += delegate
            {
                Native.MakeToolWindow(this, false, true);
            };
        }

        public void SetBounds(Rect r)
        {
            Width = Math.Max(2, r.Width);
            Height = Math.Max(2, r.Height);
            Left = r.Left;
            Top = r.Top;
        }
    }

    public class UnlockWindow : Window
    {
        private readonly Border _pill;
        private readonly TextBlock _label;
        public Action UnlockRequested;

        public UnlockWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.Manual;
            Width = 58;
            Height = 28;
            Focusable = false;

            _pill = new Border();
            _pill.CornerRadius = new CornerRadius(9);
            _pill.Background = new SolidColorBrush(Color.FromArgb(230, 20, 24, 32));
            _pill.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            _pill.BorderThickness = new Thickness(1);
            _pill.Cursor = Cursors.Hand;

            _label = new TextBlock();
            _label.Text = "解锁";
            _label.Foreground = Brushes.White;
            _label.FontSize = 12;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _pill.Child = _label;
            Content = _pill;

            _pill.MouseLeftButtonUp += delegate { if (UnlockRequested != null) UnlockRequested(); };

            SourceInitialized += delegate
            {
                Native.MakeToolWindow(this, false, true);
            };
        }

        public void SetBounds(Rect r)
        {
            Left = r.Left;
            Top = r.Top;
        }
    }

    public class MenuWindow : Window
    {
        private readonly Border _card;
        private readonly StackPanel _panel;
        private bool _suppress;
        private Action _onEnter;
        private Action _onLeave;

        public MenuWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.Height;
            Width = 220;

            _panel = new StackPanel();
            _panel.Margin = new Thickness(0);

            _card = new Border();
            _card.CornerRadius = new CornerRadius(10);
            _card.Background = new SolidColorBrush(Color.FromArgb(245, 24, 27, 35));
            _card.BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));
            _card.BorderThickness = new Thickness(1);
            _card.Padding = new Thickness(5);
            _card.Margin = new Thickness(4);
            _card.Child = _panel;

            Grid root = new Grid();
            root.Children.Add(_card);
            Content = root;

            MouseEnter += delegate { if (_onEnter != null) _onEnter(); };
            MouseLeave += delegate { if (_onLeave != null) _onLeave(); };
            Deactivated += delegate { if (_onLeave != null) _onLeave(); };

            SourceInitialized += delegate
            {
                Native.MakeToolWindow(this, false, false);
            };
        }

        public void SetCallbacks(Action onEnter, Action onLeave)
        {
            _onEnter = onEnter;
            _onLeave = onLeave;
        }

        public void Build(bool taskbarMode, Session session, Settings settings,
            Action lockAction, Action alignLeft, Action alignCenter, Action alignRight,
            Action fontDown, Action fontUp, Action<double> opacityChanged,
            Action taskbarAction, Action desktopAction, Action closeAction)
        {
            _panel.Children.Clear();
            _suppress = true;
            Width = taskbarMode ? 112 : 220;

            if (taskbarMode)
            {
                StackPanel taskRow = new StackPanel();
                taskRow.Orientation = Orientation.Horizontal;
                taskRow.Children.Add(ToolButton("桌面", "返回桌面模式", desktopAction, 40));
                taskRow.Children.Add(ToolButton("关闭", "关闭歌词", closeAction, 32));
                _panel.Children.Add(taskRow);
                _suppress = false;
                return;
            }

            string align = Settings.NormalizeAlign(settings.Align);
            Action alignNext = delegate
            {
                if (align == "left") alignCenter();
                else if (align == "center") alignRight();
                else alignLeft();
            };

            StackPanel tools = new StackPanel();
            tools.Orientation = Orientation.Horizontal;
            tools.Children.Add(ToolButton(session.Locked ? "解锁" : "锁定", "锁定歌词窗口", lockAction, 34));
            tools.Children.Add(ToolButton(
                align == "left" ? "左" : (align == "right" ? "右" : "中"),
                "对齐方式：点击切换（左 / 中 / 右）", alignNext, 26));
            tools.Children.Add(ToolButton("A−", "减小字号", fontDown, 22));
            tools.Children.Add(ToolButton("A+", "增大字号", fontUp, 22));
            tools.Children.Add(ToolButton("任务栏", "切换到任务栏模式", taskbarAction, 38));
            tools.Children.Add(ToolButton("×", "关闭歌词", closeAction, 20));
            _panel.Children.Add(tools);
            _panel.Children.Add(MakeGap(2));

            Grid opacityRow = new Grid();
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition());
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Slider slider = new Slider();
            slider.Minimum = 0.1;
            slider.Maximum = 1.0;
            slider.Value = settings.Opacity;
            slider.IsMoveToPointEnabled = true;
            slider.ToolTip = "歌词透明度";
            slider.HorizontalAlignment = HorizontalAlignment.Stretch;
            slider.VerticalAlignment = VerticalAlignment.Center;
            slider.ValueChanged += delegate
            {
                if (_suppress) return;
                opacityChanged(slider.Value);
            };
            Grid.SetColumn(slider, 0);
            opacityRow.Children.Add(slider);
            TextBlock pct = new TextBlock();
            pct.Text = Math.Round(settings.Opacity * 100).ToString(CultureInfo.InvariantCulture) + "%";
            pct.Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
            pct.FontSize = 9;
            pct.Width = 28;
            pct.TextAlignment = TextAlignment.Right;
            pct.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pct, 1);
            opacityRow.Children.Add(pct);
            slider.ValueChanged += delegate
            {
                if (!_suppress) pct.Text = Math.Round(slider.Value * 100).ToString(CultureInfo.InvariantCulture) + "%";
            };
            _panel.Children.Add(opacityRow);

            _suppress = false;
        }

        public void ShowAt(Rect textRect, Rect workArea)
        {
            if (!IsVisible)
            {
                Opacity = 0;
                Left = workArea.Left;
                Top = workArea.Top;
                Show();
            }
            UpdateLayout();
            double mw = ActualWidth > 0 ? ActualWidth : Width;
            double mh = ActualHeight;
            if (mh <= 0) mh = 90;
            double x = textRect.Left + textRect.Width / 2 - mw / 2;
            double y = textRect.Top - mh - 8;
            if (y < workArea.Top + 4)
            {
                y = textRect.Bottom + 8;
            }
            if (y + mh > workArea.Bottom - 4)
                y = Math.Max(workArea.Top + 4, workArea.Bottom - mh - 4);
            x = Math.Max(workArea.Left + 4, Math.Min(workArea.Right - mw - 4, x));
            Left = x;
            Top = y;
            Opacity = 1;
            UpdateLayout();
            Topmost = true;
        }

        public void HideMenu()
        {
            if (IsVisible) Hide();
        }

        private static TextBlock MakeLabel(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.Foreground = new SolidColorBrush(Color.FromArgb(145, 255, 255, 255));
            t.FontSize = 11;
            t.Margin = new Thickness(2, 0, 0, 5);
            return t;
        }

        private static Grid MakeRow(string label, FrameworkElement content)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 5);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock t = new TextBlock();
            t.Text = label;
            t.Foreground = new SolidColorBrush(Color.FromArgb(145, 255, 255, 255));
            t.FontSize = 11;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(2, 0, 4, 0);
            Grid.SetColumn(t, 0);
            g.Children.Add(t);
            Grid.SetColumn(content, 1);
            content.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(content);
            return g;
        }

        private static FrameworkElement MakeGap(double h)
        {
            Border b = new Border();
            b.Height = h;
            return b;
        }

        private Border MakeChip(string text, Action action, bool active)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(12, 5, 12, 5);
            b.Margin = new Thickness(0, 0, 4, 0);
            b.Cursor = Cursors.Hand;
            b.Background = active
                ? new SolidColorBrush(Color.FromRgb(59, 130, 246))
                : new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.Foreground = Brushes.White;
            b.Child = t;
            b.MouseEnter += delegate
            {
                if (!active) b.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
            };
            b.MouseLeave += delegate
            {
                if (!active) b.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
            };
            b.MouseLeftButtonUp += delegate { action(); };
            return b;
        }

        private Border ToolButton(string text, string tip, Action action, double minWidth)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(5);
            b.Padding = new Thickness(3, 2, 3, 3);
            b.Margin = new Thickness(0, 0, 2, 0);
            b.MinWidth = minWidth;
            b.Cursor = Cursors.Hand;
            b.ToolTip = tip;
            b.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 10;
            t.Foreground = Brushes.White;
            t.TextAlignment = TextAlignment.Center;
            b.Child = t;
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); };
            b.MouseLeave += delegate { b.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)); };
            b.MouseLeftButtonUp += delegate { action(); };
            return b;
        }

        private Border MakeSmallButton(string text, Action action)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(7);
            b.Padding = new Thickness(8, 4, 8, 5);
            b.Cursor = Cursors.Hand;
            b.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.Foreground = Brushes.White;
            b.Child = t;
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); };
            b.MouseLeave += delegate { b.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)); };
            b.MouseLeftButtonUp += delegate { action(); };
            return b;
        }

        private Border MakeActionButton(string text, Action action, bool accent)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(11, 6, 11, 6);
            b.Cursor = Cursors.Hand;
            b.Background = accent
                ? new SolidColorBrush(Color.FromRgb(59, 130, 246))
                : new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.Foreground = Brushes.White;
            b.Child = t;
            b.MouseEnter += delegate
            {
                if (!accent) b.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
            };
            b.MouseLeave += delegate
            {
                if (!accent) b.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            };
            b.MouseLeftButtonUp += delegate { action(); };
            return b;
        }

        private Border MakeWideButton(string text, Action action, bool accent)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(9);
            b.Padding = new Thickness(12, 7, 12, 7);
            b.Margin = new Thickness(0, 0, 0, 0);
            b.Cursor = Cursors.Hand;
            b.Background = accent
                ? new SolidColorBrush(Color.FromRgb(59, 130, 246))
                : new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.Foreground = Brushes.White;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            b.Child = t;
            b.MouseEnter += delegate
            {
                if (!accent) b.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
            };
            b.MouseLeave += delegate
            {
                if (!accent) b.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            };
            b.MouseLeftButtonUp += delegate { action(); };
            return b;
        }
    }
}
