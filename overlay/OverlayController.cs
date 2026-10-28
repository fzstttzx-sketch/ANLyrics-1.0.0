using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class OverlayController
    {
        private readonly Runtime _runtime;
        private Settings _settings;
        private Session _session;
        private readonly ThemeEngine _theme;
        private readonly LyricWindow _lyric;
        private readonly HitWindow _hit;
        private readonly UnlockWindow _unlock;
        private readonly MenuWindow _menu;
        private LyricLine _current;
        private Theme _themeValue;
        private string _mode;
        private bool _locked;
        private bool _dragging;
        private bool _dragMoved;
        private Native.POINT _dragStart;
        private double _dragLeft;
        private double _dragTop;
        private DispatcherTimer _openTimer;
        private DispatcherTimer _closeTimer;
        private DispatcherTimer _saveTimer;
        private DispatcherTimer _unlockTimer;
        private DateTime _unlockEnterAt;
        private DateTime _unlockHideAt;
        private int _generation;
        private bool _ready;
        private bool _placedOnce;

        public OverlayController(Runtime runtime, Settings settings, Session session, ThemeEngine theme)
        {
            _runtime = runtime;
            _settings = settings;
            _session = session;
            _theme = theme;
            _mode = Settings.NormalizeMode(session.Mode);
            _locked = session.Locked;

            _lyric = new LyricWindow();
            _hit = new HitWindow();
            _unlock = new UnlockWindow();
            _menu = new MenuWindow();

            _hit.MouseEnter += delegate { OnHoverEnter(); };
            _hit.MouseLeave += delegate { OnHoverLeave(); };
            _hit.MouseLeftButtonDown += OnLyricMouseDown;
            _hit.MouseMove += OnLyricMouseMove;
            _hit.MouseLeftButtonUp += OnLyricMouseUp;
            _hit.LostMouseCapture += delegate { EndDrag(false); };

            _unlock.UnlockRequested = delegate { SetLocked(false); };
            _unlock.MouseEnter += delegate { _unlockHideAt = DateTime.MinValue; };
            _unlock.MouseLeave += delegate
            {
                if (_unlock.IsVisible) _unlockHideAt = DateTime.UtcNow.AddMilliseconds(650);
            };

            _menu.SetCallbacks(OnMenuEnter, OnMenuLeave);

            _openTimer = new DispatcherTimer();
            _openTimer.Interval = TimeSpan.FromMilliseconds(240);
            _openTimer.Tick += delegate
            {
                _openTimer.Stop();
                if (!_dragging) ShowMenu();
            };

            _closeTimer = new DispatcherTimer();
            _closeTimer.Interval = TimeSpan.FromMilliseconds(700);
            _closeTimer.Tick += delegate
            {
                _closeTimer.Stop();
                if (_menu.IsMouseOver || _hit.IsMouseOver || _unlock.IsMouseOver) return;
                _menu.HideMenu();
            };

            _saveTimer = new DispatcherTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(220);
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                SaveDesktopPosition();
            };

            _unlockEnterAt = DateTime.MinValue;
            _unlockHideAt = DateTime.MinValue;
            _unlockTimer = new DispatcherTimer();
            _unlockTimer.Interval = TimeSpan.FromMilliseconds(90);
            _unlockTimer.Tick += delegate { TickUnlockHover(); };
            _unlockTimer.Start();

            _themeValue = _theme.Build();
        }

        public LyricWindow Lyric { get { return _lyric; } }
        public MenuWindow Menu { get { return _menu; } }
        public bool IsTaskbar { get { return _mode == "taskbar"; } }

        public void Initialize()
        {
            _lyric.SetGlobalOpacity(_settings.Opacity);
            _lyric.Width = 200;
            _lyric.Height = 80;
            _lyric.Show();
            _lyric.SetFade(1);

            if (_mode == "taskbar") PositionTaskbar(true);
            else
            {
                if (_session.HasDesktop && _session.DesktopX > -100000 && _session.DesktopY > -100000)
                {
                    _lyric.Left = _session.DesktopX;
                    _lyric.Top = _session.DesktopY;
                }
                else
                {
                    Rect work = Native.GetWorkArea(_lyric);
                    _lyric.Left = work.Left + (work.Width - _lyric.Width) / 2;
                    _lyric.Top = work.Bottom - _lyric.Height - 90;
                }
            }

            _ready = true;
            if (_mode == "taskbar") _locked = true;
            ApplyLockVisuals();
            _lyric.Hide();
            _hit.Hide();
            _unlock.Hide();
            _menu.HideMenu();
        }

        public void ApplySettings(Settings settings, bool rebuild)
        {
            _settings = settings;
            _themeValue = _theme.Build();
            _lyric.SetGlobalOpacity(_settings.Opacity);
            if (_current != null)
            {
                RebuildCurrent(rebuild);
            }
            else
            {
                _lyric.View.Clear();
            }
        }

        public void RefreshTheme()
        {
            _themeValue = _theme.Build();
            if (_current != null) RebuildCurrent(false);
        }

        public void SetLyrics(LyricLine line, bool animate)
        {
            if (!_ready) return;
            if (line == null)
            {
                HideLyrics();
                return;
            }
            if (_current != null && _current.Key == line.Key)
            {
                if (!_lyric.IsVisible) EnsureLyricVisible();
                if (_lyric.View.Opacity < 0.02) FadeIn();
                return;
            }

            if (!animate || _current == null || !_lyric.IsVisible || _lyric.View.Opacity < 0.02)
            {
                SwapLine(line);
                FadeIn();
                return;
            }

            int gen = ++_generation;
            LyricLine pending = line;
            DoubleAnimation fade = new DoubleAnimation();
            fade.To = 0;
            fade.Duration = TimeSpan.FromMilliseconds(90);
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            fade.Completed += delegate
            {
                if (gen != _generation) return;
                SwapLine(pending);
                FadeIn();
            };
            _lyric.View.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        public void HideLyrics()
        {
            if (!_lyric.IsVisible) return;
            int gen = ++_generation;
            DoubleAnimation fade = new DoubleAnimation();
            fade.To = 0;
            fade.Duration = TimeSpan.FromMilliseconds(120);
            fade.Completed += delegate
            {
                if (gen != _generation) return;
                _lyric.Hide();
                _hit.Hide();
                _menu.HideMenu();
            };
            _lyric.View.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void EnsureLyricVisible()
        {
            if (!_lyric.IsVisible) _lyric.Show();
            // NCM can be activated after us and take the top z-order. Re-assert
            // topmost on every rebuild so lyric changes never disappear behind it.
            Native.MakeToolWindow(_lyric, true, true);
        }

        private void FadeIn()
        {
            EnsureLyricVisible();
            _lyric.View.BeginAnimation(UIElement.OpacityProperty, null);
            _lyric.View.Opacity = 0;
            DoubleAnimation fade = new DoubleAnimation();
            fade.To = 1;
            fade.Duration = TimeSpan.FromMilliseconds(170);
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            _lyric.View.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void SwapLine(LyricLine line)
        {
            _current = line;
            RebuildCurrent(true);
            UpdateInteractiveWindows();
        }

        private void RebuildCurrent(bool reposition)
        {
            if (_current == null) return;
            bool compact = _mode == "taskbar";
            Rect work = Native.GetWorkArea(_lyric);
            Rect taskbar = Native.GetTaskbarRect(_lyric);
            double maxWidth;
            double fixedHeight;
            if (compact)
            {
                maxWidth = Math.Max(200, taskbar.Width - 230);
                fixedHeight = Math.Max(24, taskbar.Height * 0.84);
            }
            else
            {
                maxWidth = Math.Max(200, work.Width * _settings.MaxWidthRatio - LyricView.Pad * 2);
                fixedHeight = LyricView.MeasureReserved(_settings);
            }

            _lyric.View.SetLine(_current, _settings, _themeValue, compact, maxWidth, fixedHeight);
            if (!_lyric.View.HasContent)
            {
                _lyric.Hide();
                _hit.Hide();
                _menu.HideMenu();
                return;
            }

            double newW = Math.Max(90, _lyric.View.ContentWidth + LyricView.Pad * 2);
            double newH = compact
                ? Math.Max(26, taskbar.Height * 0.96)
                : Math.Max(40, _lyric.View.ReservedHeight + LyricView.Pad * 2);

            if (_mode == "taskbar")
            {
                _lyric.Width = newW;
                _lyric.Height = newH;
                _lyric.Left = taskbar.Left + 10;
                _lyric.Top = taskbar.Top + (taskbar.Height - newH) / 2;
            }
            else
            {
                double anchorX;
                double anchorY;
                double newLeft;
                double newTop;
                if (!_placedOnce)
                {
                    // First line after startup: preserve the saved top-left (or
                    // the default bottom-center) instead of anchoring on the
                    // placeholder 200x80 window.
                    if (_session.HasDesktop)
                    {
                        newLeft = _session.DesktopX;
                        newTop = _session.DesktopY;
                    }
                    else
                    {
                        newLeft = work.Left + (work.Width - newW) / 2;
                        newTop = work.Bottom - 90 - newH;
                    }
                }
                else
                {
                    anchorY = _lyric.Top + _lyric.Height / 2;
                    bool preserveCenter = _lyric.Width < 2 || _lyric.Height < 2 || reposition == false;
                    if (preserveCenter)
                        anchorX = _lyric.Left + _lyric.Width / 2;
                    else
                        anchorX = AnchorX();
                    newLeft = preserveCenter ? anchorX - newW / 2 : LeftForAnchor(anchorX, newW);
                    newTop = anchorY - newH / 2;
                }
                _lyric.Width = newW;
                _lyric.Height = newH;
                _lyric.Left = newLeft;
                _lyric.Top = newTop;
                _placedOnce = true;
                ClampDesktop();
            }

            EnsureLyricVisible();
            UpdateInteractiveWindows();
            Log.Info(string.Format(CultureInfo.InvariantCulture,
                "layout mode={0} content={1}x{2} reserved={3} win={4}x{5} at={6},{7} work={8}",
                _mode, _lyric.View.ContentWidth, _lyric.View.ContentHeight, _lyric.View.ReservedHeight,
                _lyric.Width, _lyric.Height, _lyric.Left, _lyric.Top, work));
        }

        private double AnchorX()
        {
            string align = Settings.NormalizeAlign(_settings.Align);
            if (align == "left") return _lyric.Left;
            if (align == "right") return _lyric.Left + _lyric.Width;
            return _lyric.Left + _lyric.Width / 2;
        }

        private double LeftForAnchor(double anchorX, double width)
        {
            string align = Settings.NormalizeAlign(_settings.Align);
            if (align == "left") return anchorX;
            if (align == "right") return anchorX - width;
            return anchorX - width / 2.0;
        }

        private void ClampDesktop()
        {
            Rect work = Native.GetWorkArea(_lyric);
            double minLeft = work.Left + 4;
            double maxLeft = work.Right - _lyric.Width - 4;
            if (maxLeft < minLeft) maxLeft = minLeft;
            if (_lyric.Left < minLeft) _lyric.Left = minLeft;
            if (_lyric.Left > maxLeft) _lyric.Left = maxLeft;

            double minTop = work.Top + 4;
            double maxTop = work.Bottom - _lyric.Height - 4;
            if (maxTop < minTop) maxTop = minTop;
            if (_lyric.Top < minTop) _lyric.Top = minTop;
            if (_lyric.Top > maxTop) _lyric.Top = maxTop;
        }

        private static Rect TriggerRect(Rect text)
        {
            return new Rect(text.Left - 28, text.Top - 14,
                text.Width + 28 + 72, text.Height + 28);
        }

        private Rect TextRect()
        {
            double h = _lyric.View.ContentHeight;
            double w = _lyric.View.ContentWidth;
            double y = _lyric.Top + (_lyric.Height - h) / 2;
            if (y < _lyric.Top) y = _lyric.Top;
            return new Rect(_lyric.Left + LyricView.Pad, y, Math.Max(2, w), Math.Max(2, h));
        }

        private void UpdateInteractiveWindows()
        {
            if (!_lyric.IsVisible || !_lyric.View.HasContent)
            {
                _hit.Hide();
                _unlock.Hide();
                return;
            }

            Rect text = TextRect();
            bool canHover = _mode == "taskbar" || !_locked;
            if (canHover)
            {
                _hit.Cursor = _mode == "taskbar" ? Cursors.Hand : Cursors.SizeAll;
                _hit.SetBounds(TriggerRect(text));
                if (!_hit.IsVisible) _hit.Show();
                Native.SetWindowPos(new WindowInteropHelper(_hit).Handle, Native.HWND_TOPMOST,
                    0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            else
            {
                _hit.Hide();
            }

            HideUnlockChip();
        }

        private void TickUnlockHover()
        {
            if (_mode != "desktop" || !_locked || !_lyric.IsVisible || !_lyric.View.HasContent)
            {
                HideUnlockChip();
                return;
            }

            Rect text = TextRect();
            Point cursor = CursorInDip();
            Rect chip = UnlockChipRect(text);
            bool overText = TriggerRect(text).Contains(cursor);
            bool overChip = chip.Contains(cursor) || _unlock.IsMouseOver;

            if (overText || overChip)
            {
                _unlockHideAt = DateTime.MinValue;
                if (!_unlock.IsVisible)
                {
                    if (_unlockEnterAt == DateTime.MinValue) _unlockEnterAt = DateTime.UtcNow;
                    if ((DateTime.UtcNow - _unlockEnterAt).TotalMilliseconds >= 180)
                        ShowUnlockChip(text);
                }
            }
            else
            {
                _unlockEnterAt = DateTime.MinValue;
                if (_unlock.IsVisible)
                {
                    if (_unlockHideAt == DateTime.MinValue)
                        _unlockHideAt = DateTime.UtcNow.AddMilliseconds(650);
                    else if (DateTime.UtcNow >= _unlockHideAt)
                        HideUnlockChip();
                }
            }
        }

        private Point CursorInDip()
        {
            Native.POINT p;
            Native.GetCursorPos(out p);
            try
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(_lyric);
                return new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
            }
            catch
            {
                return new Point(p.X, p.Y);
            }
        }

        private Rect UnlockChipRect(Rect text)
        {
            Rect work = Native.GetWorkArea(_lyric);
            double x = text.Left + text.Width / 2 - _unlock.Width / 2;
            double y = text.Top - _unlock.Height - 8;
            if (y < work.Top + 4) y = text.Bottom + 8;
            x = Math.Max(work.Left + 4, Math.Min(work.Right - _unlock.Width - 4, x));
            y = Math.Max(work.Top + 4, Math.Min(work.Bottom - _unlock.Height - 4, y));
            return new Rect(x, y, _unlock.Width, _unlock.Height);
        }

        private void ShowUnlockChip(Rect text)
        {
            _unlock.SetBounds(UnlockChipRect(text));
            if (!_unlock.IsVisible) _unlock.Show();
            Native.SetWindowPos(new WindowInteropHelper(_unlock).Handle, Native.HWND_TOPMOST,
                0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void HideUnlockChip()
        {
            if (_unlock.IsVisible) _unlock.Hide();
            _unlockEnterAt = DateTime.MinValue;
            _unlockHideAt = DateTime.MinValue;
        }

        private void OnHoverEnter()
        {
            if (_dragging) return;
            if (_mode == "desktop" && _locked) return;
            _closeTimer.Stop();
            if (_menu.IsVisible) return;
            _openTimer.Stop();
            _openTimer.Start();
        }

        private void OnHoverLeave()
        {
            _openTimer.Stop();
            if (_menu.IsVisible) ScheduleClose();
        }

        private void OnMenuEnter()
        {
            _closeTimer.Stop();
        }

        private void OnMenuLeave()
        {
            ScheduleClose();
        }

        private void ScheduleClose()
        {
            _closeTimer.Stop();
            _closeTimer.Start();
        }

        private void ShowMenu()
        {
            if (!_lyric.IsVisible) return;
            if (_mode == "desktop" && _locked) return;
            if (_menu.IsVisible) return;

            bool taskbar = _mode == "taskbar";
            _menu.Build(taskbar, _session, _settings,
                delegate { SetLocked(true); },
                delegate { SetAlign("left"); },
                delegate { SetAlign("center"); },
                delegate { SetAlign("right"); },
                delegate { ChangeFont(-2); },
                delegate { ChangeFont(2); },
                delegate(double it) { SetOpacityFromMenu(it); },
                delegate { SetMode("taskbar"); },
                delegate { SetMode("desktop"); },
                delegate { CloseByUser(); });

            _menu.ShowAt(TextRect(), Native.GetWorkArea(_lyric));
        }

        private void SetAlign(string align)
        {
            _settings.Align = Settings.NormalizeAlign(align);
            _runtime.SaveSettings();
            _themeValue = _theme.Build();
            RebuildCurrent(false);
            _menu.HideMenu();
            ShowMenu();
        }

        private void ChangeFont(double delta)
        {
            _settings.OriginalFont.Size = Settings.Clamp(_settings.OriginalFont.Size + delta, 12, 110);
            _settings.TranslationFont.Size = Settings.Clamp(_settings.TranslationFont.Size + delta, 8, 80);
            _runtime.SaveSettings();
            _themeValue = _theme.Build();
            RebuildCurrent(true);
            _menu.HideMenu();
            ShowMenu();
        }

        private void SetOpacityFromMenu(double value)
        {
            _settings.Opacity = Settings.Clamp(value, 0.1, 1.0);
            _lyric.SetGlobalOpacity(_settings.Opacity);
            _runtime.ScheduleSaveSettings();
        }

        private void SetLocked(bool locked)
        {
            if (_mode == "taskbar") return;
            Log.Info("set locked " + locked);
            _locked = locked;
            _session.Locked = locked;
            _session.Save(_runtime.SessionPath);
            _menu.HideMenu();
            ApplyLockVisuals();
            UpdateInteractiveWindows();
        }

        private void ApplyLockVisuals()
        {
            if (_mode == "desktop" && _locked)
            {
                _hit.Hide();
                _menu.HideMenu();
            }
            else
            {
                _unlock.Hide();
            }
            UpdateInteractiveWindows();
        }

        private void SetMode(string mode)
        {
            string target = Settings.NormalizeMode(mode);
            if (target == _mode)
            {
                if (target == "desktop") SetLocked(false);
                return;
            }

            if (_mode == "desktop")
            {
                _session.DesktopX = _lyric.Left;
                _session.DesktopY = _lyric.Top;
                _session.HasDesktop = true;
            }

            _mode = target;
            _session.Mode = target;
            if (target == "taskbar")
            {
                _locked = true;
                _session.Locked = true;
                _settings.Mode = "taskbar";
            }
            else
            {
                _locked = false;
                _session.Locked = false;
                _settings.Mode = "desktop";
            }

            _menu.HideMenu();
            _hit.Hide();
            _unlock.Hide();

            if (target == "taskbar")
            {
                PositionTaskbar(false);
                if (_current != null) RebuildCurrent(true);
            }
            else
            {
                if (_session.HasDesktop)
                {
                    _lyric.Left = _session.DesktopX;
                    _lyric.Top = _session.DesktopY;
                }
                if (_current != null) RebuildCurrent(false);
                if (_session.HasDesktop)
                {
                    _lyric.Left = _session.DesktopX;
                    _lyric.Top = _session.DesktopY;
                }
                ClampDesktop();
            }

            _session.Save(_runtime.SessionPath);
            _runtime.SaveSettings();
            ApplyLockVisuals();
        }

        private void PositionTaskbar(bool hide)
        {
            _locked = true;
            Rect taskbar = Native.GetTaskbarRect(_lyric);
            _lyric.Height = Math.Max(24, taskbar.Height * 0.92);
            _lyric.Width = Math.Max(120, _lyric.Width);
            _lyric.Left = taskbar.Left + 10;
            _lyric.Top = taskbar.Top + (taskbar.Height - _lyric.Height) / 2;
            if (hide) _lyric.Hide();
        }

        private void OnLyricMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_mode != "desktop" || _locked) return;
            _openTimer.Stop();
            _menu.HideMenu();
            _dragging = true;
            _dragMoved = false;
            _dragStart = GetCursor();
            _dragLeft = _lyric.Left;
            _dragTop = _lyric.Top;
            _hit.CaptureMouse();
            e.Handled = true;
        }

        private void OnLyricMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
            Native.POINT p = GetCursor();
            double scale = VisualTreeHelper.GetDpi(_lyric).DpiScaleX;
            if (scale <= 0) scale = 1;
            double dx = (p.X - _dragStart.X) / scale;
            double dy = (p.Y - _dragStart.Y) / scale;
            if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2) _dragMoved = true;
            _lyric.Left = _dragLeft + dx;
            _lyric.Top = _dragTop + dy;
            ClampDesktop();
            UpdateInteractiveWindows();
            _unlock.Hide();
        }

        private void OnLyricMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            bool moved = _dragMoved;
            EndDrag(true);
            if (!moved) ShowMenu();
            e.Handled = true;
        }

        private void EndDrag(bool save)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_hit.IsMouseCaptured) _hit.ReleaseMouseCapture();
            if (save) SaveDesktopPosition();
        }

        private void SaveDesktopPosition()
        {
            if (_mode != "desktop") return;
            _session.DesktopX = _lyric.Left;
            _session.DesktopY = _lyric.Top;
            _session.HasDesktop = true;
            _session.Save(_runtime.SessionPath);
        }

        private static Native.POINT GetCursor()
        {
            Native.POINT p;
            Native.GetCursorPos(out p);
            return p;
        }

        private void CloseByUser()
        {
            _settings.Enabled = false;
            _session.Running = false;
            _session.Save(_runtime.SessionPath);
            _runtime.SaveSettings();
            _runtime.NotifyClosedByUser();
            _runtime.Exit(false);
        }

        public void Shutdown()
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            _saveTimer.Stop();
            SaveDesktopPosition();
            _session.Running = false;
            _session.Save(_runtime.SessionPath);
            _menu.HideMenu();
            _unlock.Hide();
            _hit.Hide();
            _lyric.Hide();
        }

        public void MoveToTaskbarFromSettings()
        {
            SetMode("taskbar");
        }

        public void MoveToDesktopFromSettings()
        {
            SetMode("desktop");
        }

        public void ToggleLockFromSettings()
        {
            SetLocked(!_locked);
        }

        public void SaveNow()
        {
            SaveDesktopPosition();
        }
    }

    public class Runtime
    {
        private readonly string _dir;
        private readonly bool _demo;
        private readonly Dispatcher _dispatcher;
        private Application _app;
        private Settings _settings;
        private Session _session;
        private LyricsDoc _lyrics;
        private NowState _now;
        private ThemeEngine _theme;
        private OverlayController _controller;
        private DispatcherTimer _poll;
        private DispatcherTimer _alive;
        private DispatcherTimer _saveSettingsTimer;
        private DateTime _settingsMtime;
        private DateTime _lyricsMtime;
        private DateTime _nowMtime;
        private DateTime _controlMtime;
        private long _lastControlSeq;
        private DateTime _lastNowWriteUtc;
        private double _timeBase;
        private bool _playing;
        private long _receivedTicks;
        private long _startedTicks;
        private int _currentIndex = -2;
        private bool _exiting;
        private bool _closedByUser;

        public string SessionPath { get { return Path.Combine(_dir, "session.json"); } }
        public string SettingsPath { get { return Path.Combine(_dir, "settings.json"); } }
        public string LyricsPath { get { return Path.Combine(_dir, "lyrics.json"); } }
        public string NowPath { get { return Path.Combine(_dir, "now.json"); } }

        public Runtime(string dir, bool demo, Application app)
        {
            _dir = dir;
            _demo = demo;
            _app = app;
            _dispatcher = app.Dispatcher;
        }

        public bool Start()
        {
            Directory.CreateDirectory(_dir);
            Log.Init(_dir);
            Log.Info("overlay start; dir=" + _dir + " demo=" + _demo);
            FontCatalog.Write(Path.Combine(_dir, "fonts.json"));

            _settings = Settings.Load(SettingsPath);
            _session = Session.Load(SessionPath);
            _lyrics = LyricsDoc.Load(LyricsPath);
            _now = LoadNow();

            if (_demo) SeedDemo();

            _settingsMtime = Mtime(SettingsPath);
            _lyricsMtime = Mtime(LyricsPath);
            _nowMtime = Mtime(NowPath);
            _controlMtime = Mtime(Path.Combine(_dir, "control.json"));

            Dictionary<string, object> ctrl = Json.Read(Path.Combine(_dir, "control.json"));
            _lastControlSeq = Json.Long(ctrl, "seq", 0);

            _theme = new ThemeEngine(_settings, _dispatcher, Path.Combine(_dir, "covers"));
            _theme.Changed += delegate
            {
                if (_controller != null) _controller.RefreshTheme();
            };

            _controller = new OverlayController(this, _settings, _session, _theme);
            _controller.Initialize();

            if (_lyrics != null && _lyrics.Lines.Count > 0)
            {
                _theme.RequestCover(_lyrics.CoverUrl, _lyrics.SongId);
            }

            if (!_settings.Enabled)
            {
                Log.Info("disabled at start");
                return false;
            }

            ApplyNow(true);

            _poll = new DispatcherTimer();
            _poll.Interval = TimeSpan.FromMilliseconds(80);
            _poll.Tick += delegate { Poll(); };
            _poll.Start();

            _alive = new DispatcherTimer();
            _alive.Interval = TimeSpan.FromMilliseconds(1800);
            _alive.Tick += delegate { CheckAlive(); };
            _alive.Start();

            _saveSettingsTimer = new DispatcherTimer();
            _saveSettingsTimer.Interval = TimeSpan.FromMilliseconds(180);
            _saveSettingsTimer.Tick += delegate
            {
                _saveSettingsTimer.Stop();
                SaveSettings();
            };

            _startedTicks = Stopwatch.GetTimestamp();
            _session.Running = true;
            _session.Save(SessionPath);
            return true;
        }

        private NowState LoadNow()
        {
            NowState n = new NowState();
            Dictionary<string, object> d = Json.Read(NowPath);
            if (d == null) return n;
            n.SongId = Json.Str(d, "songId", "");
            n.Time = Json.Num(d, "time", 0);
            n.Playing = Json.Bool(d, "playing", false);
            n.Index = (int)Json.Long(d, "index", -1);
            n.UpdatedAt = Json.Long(d, "updatedAt", 0);
            return n;
        }

        private void SeedDemo()
        {
            if (_settings == null) _settings = new Settings();
            if (_session == null) _session = new Session();
            if (_lyrics == null || _lyrics.Lines.Count == 0)
            {
                _lyrics = new LyricsDoc();
                _lyrics.SongId = "demo";
                _lyrics.Title = "夜航";
                _lyrics.Artist = "ANLyrics";
                _lyrics.Lines.Add(new LyricLine(0, "夜色渐深 风穿过城市", "The night grows deep, wind crosses the town"));
                _lyrics.Lines.Add(new LyricLine(4, "灯火一盏一盏亮起", "Lights come on one by one"));
                _lyrics.Lines.Add(new LyricLine(8, "我们沿着河岸慢慢走", "We walk slowly along the river"));
                _lyrics.Lines.Add(new LyricLine(12, "直到天亮", "Until the morning comes"));
            }
            if (_now == null) _now = new NowState();
            _now.SongId = "demo";
            _now.Time = 0;
            _now.Playing = true;
            _now.Index = 0;
            _timeBase = 0;
            _playing = true;
            _receivedTicks = Stopwatch.GetTimestamp();
            _settings.Enabled = true;
        }

        private void Poll()
        {
            if (_exiting) return;
            try
            {
                DateTime m = Mtime(SettingsPath);
                if (m != _settingsMtime)
                {
                    _settingsMtime = m;
                    Settings s = Settings.Load(SettingsPath);
                    if (s != null)
                    {
                        _settings = s;
                        _controller.ApplySettings(s, true);
                        if (!s.Enabled)
                        {
                            Exit(false);
                            return;
                        }
                    }
                }

                m = Mtime(LyricsPath);
                if (m != _lyricsMtime)
                {
                    _lyricsMtime = m;
                    LyricsDoc doc = LyricsDoc.Load(LyricsPath);
                    if (doc != null && doc.SongId == _now.SongId)
                    {
                        _lyrics = doc;
                        _theme.RequestCover(doc.CoverUrl, doc.SongId);
                        _currentIndex = -2;
                        UpdateCurrentLine();
                    }
                }

                m = Mtime(NowPath);
                if (m != _nowMtime)
                {
                    _nowMtime = m;
                    _lastNowWriteUtc = DateTime.UtcNow;
                    NowState n = LoadNow();
                    if (n.SongId != _now.SongId)
                    {
                        _now = n;
                        _lyrics = LyricsDoc.Load(LyricsPath);
                        if (_lyrics == null || _lyrics.SongId != _now.SongId)
                        {
                            _lyrics = new LyricsDoc();
                        }
                        _theme.RequestCover(_lyrics.CoverUrl, _lyrics.SongId);
                        _currentIndex = -2;
                        _receivedTicks = Stopwatch.GetTimestamp();
                        _timeBase = n.Time;
                        _playing = n.Playing;
                        _controller.SetLyrics(null, false);
                        UpdateCurrentLine();
                    }
                    else
                    {
                        _now = n;
                        _receivedTicks = Stopwatch.GetTimestamp();
                        _timeBase = n.Time;
                        _playing = n.Playing;
                        UpdateCurrentLine();
                    }
                }
                else
                {
                    UpdateCurrentLine();
                }

                m = Mtime(Path.Combine(_dir, "control.json"));
                if (m != _controlMtime)
                {
                    _controlMtime = m;
                    HandleControl();
                }
            }
            catch (Exception ex)
            {
                Log.Error("poll", ex);
            }
        }

        private void HandleControl()
        {
            Dictionary<string, object> d = Json.Read(Path.Combine(_dir, "control.json"));
            if (d == null) return;
            long seq = Json.Long(d, "seq", 0);
            if (seq <= _lastControlSeq) return;
            _lastControlSeq = seq;
            string cmd = Json.Str(d, "cmd", "");
            Log.Info("control: " + cmd);
            if (cmd == "shutdown") Exit(false);
            else if (cmd == "mode:taskbar") _controller.MoveToTaskbarFromSettings();
            else if (cmd == "mode:desktop") _controller.MoveToDesktopFromSettings();
            else if (cmd == "reload")
            {
                _settingsMtime = DateTime.MinValue;
                _lyricsMtime = DateTime.MinValue;
                _nowMtime = DateTime.MinValue;
            }
            else if (cmd == "show") ShowAgain();
            else if (cmd == "fonts")
            {
                FontCatalog.Write(Path.Combine(_dir, "fonts.json"));
                Log.Info("fonts refreshed");
            }
        }

        private void ShowAgain()
        {
            if (_currentIndex != -2) UpdateCurrentLine();
        }

        private void UpdateCurrentLine()
        {
            if (_now == null || string.IsNullOrEmpty(_now.SongId) || _lyrics == null || _lyrics.SongId != _now.SongId)
            {
                _controller.SetLyrics(null, false);
                return;
            }

            if (_lyrics.Kind == "instrumental")
            {
                if (_currentIndex == -4) return;
                _currentIndex = -4;
                _controller.SetLyrics(PlaceholderLine("-纯音乐 请欣赏-"), true);
                return;
            }

            if (_lyrics.Kind == "none" || _lyrics.Lines.Count == 0)
            {
                if (_currentIndex == -3) return;
                _currentIndex = -3;
                _controller.SetLyrics(PlaceholderLine("-没有人上传歌词哦-"), true);
                return;
            }

            double elapsed = (Stopwatch.GetTimestamp() - _receivedTicks) / (double)Stopwatch.Frequency;
            double t = _timeBase + (_playing ? elapsed : 0) + _settings.OffsetMs / 1000.0;
            int idx = FindLine(t);
            if (idx < 0)
            {
                if (_currentIndex == -5) return;
                _currentIndex = -5;
                _controller.SetLyrics(PlaceholderLine("-前奏-"), true);
                return;
            }
            if (idx == _currentIndex) return;
            _currentIndex = idx;
            _controller.SetLyrics(_lyrics.Lines[idx], true);
        }

        private LyricLine PlaceholderLine(string message)
        {
            string title = _lyrics != null && !string.IsNullOrWhiteSpace(_lyrics.Title)
                ? _lyrics.Title
                : "未知歌曲";
            return new LyricLine(0, title, message);
        }

        private int FindLine(double t)
        {
            if (_lyrics == null || _lyrics.Lines.Count == 0) return -1;
            if (t < _lyrics.Lines[0].T - 0.25) return -1;
            int found = 0;
            for (int i = 0; i < _lyrics.Lines.Count; i++)
            {
                if (_lyrics.Lines[i].T <= t + 0.02) found = i;
                else break;
            }
            return found;
        }

        private void ApplyNow(bool initial)
        {
            if (_now == null) _now = new NowState();
            _receivedTicks = Stopwatch.GetTimestamp();
            _timeBase = _now.Time;
            _playing = _now.Playing;
            if (string.IsNullOrEmpty(_now.SongId))
            {
                _controller.SetLyrics(null, false);
                return;
            }
            if (_lyrics == null || _lyrics.SongId != _now.SongId)
            {
                _lyrics = LyricsDoc.Load(LyricsPath);
                if (_lyrics == null || _lyrics.SongId != _now.SongId) _lyrics = new LyricsDoc();
            }
            _theme.RequestCover(_lyrics.CoverUrl, _lyrics.SongId);
            _currentIndex = -2;
            UpdateCurrentLine();
        }

        private void CheckAlive()
        {
            if (_exiting) return;
            if (!_demo)
            {
                bool found = false;
                try
                {
                    Process[] ps = Process.GetProcessesByName("cloudmusic");
                    found = ps != null && ps.Length > 0;
                }
                catch { found = true; }
                if (!found)
                {
                    Log.Info("cloudmusic process gone");
                    Exit(false);
                    return;
                }
            }

            if (_lastNowWriteUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _lastNowWriteUtc).TotalSeconds > 30)
            {
                Log.Info("heartbeat stale");
                Exit(false);
            }

            _session.Running = true;
            _session.Save(SessionPath);
        }

        public void SaveSettings()
        {
            try
            {
                Json.Write(SettingsPath, _settings.ToDict());
                _settingsMtime = Mtime(SettingsPath);
            }
            catch (Exception ex)
            {
                Log.Error("save settings", ex);
            }
        }

        public void ScheduleSaveSettings()
        {
            if (_saveSettingsTimer == null) return;
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }

        public void NotifyClosedByUser()
        {
            _closedByUser = true;
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["seq"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                d["type"] = "close";
                d["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Json.Write(Path.Combine(_dir, "events.json"), d);
            }
            catch { }
        }

        public void Exit(bool save)
        {
            if (_exiting) return;
            _exiting = true;
            Log.Info("exit; userClosed=" + _closedByUser);
            try { if (_poll != null) _poll.Stop(); } catch { }
            try { if (_alive != null) _alive.Stop(); } catch { }
            try { if (_saveSettingsTimer != null) _saveSettingsTimer.Stop(); } catch { }
            try { if (_controller != null) _controller.Shutdown(); } catch (Exception ex) { Log.Error("controller shutdown", ex); }
            try
            {
                _session.Running = false;
                _session.Save(SessionPath);
            }
            catch { }
            if (_closedByUser)
            {
                try
                {
                    _settings.Enabled = false;
                    SaveSettings();
                }
                catch { }
            }
            try { _app.Shutdown(); } catch { }
        }

        private static DateTime Mtime(string path)
        {
            try
            {
                if (!File.Exists(path)) return DateTime.MinValue;
                return File.GetLastWriteTimeUtc(path);
            }
            catch { return DateTime.MinValue; }
        }
    }

    public static class OverlayApp
    {
        private static Mutex _mutex;

        [STAThread]
        public static void Run(string dataDir, bool demo)
        {
            try
            {
                bool created;
                _mutex = new Mutex(true, "Local\\ANLyricsOverlay", out created);
                if (!created)
                {
                    Log.Init(dataDir);
                    Log.Info("another overlay instance is running");
                    return;
                }

                Directory.CreateDirectory(dataDir);
                Log.Init(dataDir);

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate (object sender, DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Error("dispatcher unhandled", e.Exception);
                    e.Handled = true;
                };

                Runtime rt = new Runtime(dataDir, demo, app);
                if (rt.Start())
                {
                    app.Run();
                }
                else
                {
                    Log.Info("overlay not started (disabled)");
                }
            }
            catch (Exception ex)
            {
                try { Log.Init(dataDir); } catch { }
                Log.Error("fatal", ex);
                try
                {
                    MessageBox.Show(ex.ToString(), "ANLyrics overlay error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }
        }
    }
}
