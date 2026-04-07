using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TrayZoom
{
    // ═══════════════════════════════════════════════════════════════
    //  Entry point
    // ═══════════════════════════════════════════════════════════════
    static class Program
    {
        public static string? ArgModifier    = null;
        public static float?  ArgSmoothSpeed = null;

        [STAThread]
        static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLower();

                if (a is "--help" or "-h" or "/?")
                {
                    MessageBox.Show(
                        "TrayZoom – command-line options\n\n" +
                        "  --modifier  -m  <Win|Ctrl|Alt|Ctrl+Alt>\n" +
                        "      Hotkey modifier used with the scroll wheel.\n" +
                        "      Default: Alt\n\n" +
                        "  --speed  -s  <0.05 – 0.40>\n" +
                        "      Zoom animation speed (higher = snappier).\n" +
                        "      Default: 0.27\n\n" +
                        "  --help  -h  /?\n" +
                        "      Show this message.\n\n" +
                        "Examples:\n" +
                        "  TrayZoom.exe --modifier Ctrl --speed 0.30\n" +
                        "  TrayZoom.exe -m Ctrl+Alt -s 0.15\n\n" +
                        "Command-line values override saved settings for\n" +
                        "the current session but do not overwrite them.",
                        "TrayZoom – Help",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if ((a is "--modifier" or "-m") && i + 1 < args.Length)
                {
                    string v = args[++i].Trim();
                    if (v == "Win" || v == "Ctrl" || v == "Alt" || v == "Ctrl+Alt")
                        ArgModifier = v;
                    else
                    {
                        MessageBox.Show(
                            $"Unknown modifier \"{v}\".\n\nValid values: Win  Ctrl  Alt  Ctrl+Alt",
                            "TrayZoom", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else if ((a is "--speed" or "-s") && i + 1 < args.Length)
                {
                    if (float.TryParse(args[++i],
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out float spd) && spd is >= 0.05f and <= 0.40f)
                        ArgSmoothSpeed = spd;
                    else
                    {
                        MessageBox.Show(
                            $"Invalid speed \"{args[i]}\".\n\nValid range: 0.05 – 0.40",
                            "TrayZoom", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayZoomApp());
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Application context
    // ═══════════════════════════════════════════════════════════════
    public class TrayZoomApp : ApplicationContext
    {
        // ── Win32 ──────────────────────────────────────────────────
        delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn,
                                               IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
                                             IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT  pt;
            public uint   mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("magnification.dll")] static extern bool MagInitialize();
        [DllImport("magnification.dll")] static extern bool MagUninitialize();
        [DllImport("magnification.dll")]
        static extern bool MagSetFullscreenTransform(float magLevel,
                                                      int xOffset, int yOffset);

        const int WH_MOUSE_LL   = 14;
        const int WM_MOUSEWHEEL = 0x020A;

        const int VK_LWIN    = 0x5B;
        const int VK_RWIN    = 0x5C;
        const int VK_CONTROL = 0x11;
        const int VK_MENU    = 0x12;   // Alt

        // ── Settings ───────────────────────────────────────────────
        const string APP_NAME = "TrayZoom";
        const string REG_APP  = @"SOFTWARE\TrayZoom";

        string _modifier    = "Alt";   // default: Alt + scroll
        float  _zoomStep    = 0.20f;
        float  _zoomMin     = 1.0f;
        float  _zoomMax     = 8.0f;
        float  _smoothSpeed = 0.27f;

        // ── Runtime state ──────────────────────────────────────────
        IntPtr            _mouseHook = IntPtr.Zero;
        LowLevelMouseProc? _mouseProc;
        NotifyIcon?        _trayIcon;
        System.Windows.Forms.Timer? _smoothTimer;
        bool  _magInitialized = false;
        float _currentZoom    = 1.0f;
        float _targetZoom     = 1.0f;

        // ───────────────────────────────────────────────────────────
        public TrayZoomApp()
        {
            try   { _magInitialized = MagInitialize(); }
            catch { _magInitialized = false; }

            if (!_magInitialized)
            {
                MessageBox.Show(
                    "Could not initialise the Windows Magnification API.\n\n" +
                    "Please ensure you are running Windows 8.1 or later.",
                    APP_NAME, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }

            LoadSettings();

            // Command-line overrides (session only, do not persist)
            if (Program.ArgModifier    != null) _modifier    = Program.ArgModifier;
            if (Program.ArgSmoothSpeed != null) _smoothSpeed = Program.ArgSmoothSpeed.Value;

            BuildTrayIcon();
            InstallHook();
            BuildSmoothTimer();
        }

        // ── Settings ───────────────────────────────────────────────
        void LoadSettings()
        {
            using var key = Registry.CurrentUser.OpenSubKey(REG_APP);
            if (key == null) return;
            _modifier    = (key.GetValue("Modifier")    as string) ?? _modifier;
            _zoomStep    = ParseFloat(key.GetValue("ZoomStep"),    _zoomStep);
            _zoomMin     = ParseFloat(key.GetValue("ZoomMin"),     _zoomMin);
            _zoomMax     = ParseFloat(key.GetValue("ZoomMax"),     _zoomMax);
            _smoothSpeed = ParseFloat(key.GetValue("SmoothSpeed"), _smoothSpeed);
        }

        void SaveSettings()
        {
            using var key = Registry.CurrentUser.CreateSubKey(REG_APP);
            key.SetValue("Modifier",    _modifier);
            key.SetValue("ZoomStep",    _zoomStep.ToString());
            key.SetValue("ZoomMin",     _zoomMin.ToString());
            key.SetValue("ZoomMax",     _zoomMax.ToString());
            key.SetValue("SmoothSpeed", _smoothSpeed.ToString());
        }

        static float ParseFloat(object? v, float def) =>
            float.TryParse(v as string, out float r) ? r : def;

        // ── Tray icon ──────────────────────────────────────────────
        void BuildTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text    = APP_NAME,
                Icon    = CreateTrayIcon(),
                Visible = true,
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("⚙  Settings…", null, (_, _) => ShowSettings());
            menu.Items.Add("🔍 Reset Zoom", null, (_, _) => ResetZoom());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("✕  Exit",       null, (_, _) => ExitApp());

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => ShowSettings();
        }

        static Icon CreateTrayIcon()
        {
            var bmp = new Bitmap(16, 16);
            using var g     = Graphics.FromImage(bmp);
            using var pen   = new Pen(Color.White, 2f);
            using var brush = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            g.Clear(Color.Transparent);
            g.FillEllipse(brush, 1, 1, 10, 10);
            g.DrawEllipse(pen,   1, 1, 10, 10);
            g.DrawLine(pen, 10, 10, 14, 14);
            return Icon.FromHandle(bmp.GetHicon());
        }

        // ── Low-level mouse hook ───────────────────────────────────
        void InstallHook()
        {
            _mouseProc = MouseHookProc;
            using var mod = System.Diagnostics.Process.GetCurrentProcess().MainModule!;
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc,
                                          GetModuleHandle(mod.ModuleName), 0);
            if (_mouseHook == IntPtr.Zero)
            {
                MessageBox.Show(
                    "Could not install the mouse hook.\n\n" +
                    "Zoom will not function. This can happen if another application\n" +
                    "is blocking low-level input hooks.",
                    APP_NAME, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL && IsModifierDown())
            {
                var  data  = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int  delta = (int)(data.mouseData >> 16);
                if ((delta & 0x8000) != 0) delta -= 0x10000;

                _targetZoom = Clamp(_targetZoom + (delta > 0 ? _zoomStep : -_zoomStep),
                                    _zoomMin, _zoomMax);
                return (IntPtr)1; // consume event
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        bool IsModifierDown() => _modifier switch
        {
            "Ctrl"     => IsDown(VK_CONTROL),
            "Alt"      => IsDown(VK_MENU),
            "Ctrl+Alt" => IsDown(VK_CONTROL) && IsDown(VK_MENU),
            _          => IsDown(VK_LWIN) || IsDown(VK_RWIN),  // Win
        };

        static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        // ── Smooth zoom + cursor-follow (~60 fps) ──────────────────
        void BuildSmoothTimer()
        {
            _smoothTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _smoothTimer.Tick += SmoothTick;
            _smoothTimer.Start();
        }

        void SmoothTick(object? sender, EventArgs e)
        {
            bool settled = Math.Abs(_currentZoom - _targetZoom) < 0.002f;
            _currentZoom = settled
                ? _targetZoom
                : _currentZoom + (_targetZoom - _currentZoom) * _smoothSpeed;

            ApplyZoom(_currentZoom);
        }

        void ApplyZoom(float zoom)
        {
            if (!_magInitialized) return;

            if (zoom <= 1.005f)
            {
                MagSetFullscreenTransform(1.0f, 0, 0);
                return;
            }

            GetCursorPos(out POINT cur);

            int sw = Screen.PrimaryScreen?.Bounds.Width  ?? 1920;
            int sh = Screen.PrimaryScreen?.Bounds.Height ?? 1080;

            int ox = (int)(cur.x - cur.x / zoom);
            int oy = (int)(cur.y - cur.y / zoom);

            ox = Math.Max(0, Math.Min(ox, (int)(sw - sw / zoom)));
            oy = Math.Max(0, Math.Min(oy, (int)(sh - sh / zoom)));

            MagSetFullscreenTransform(zoom, ox, oy);
        }

        void ResetZoom()
        {
            _targetZoom = _currentZoom = 1.0f;
            if (_magInitialized) MagSetFullscreenTransform(1.0f, 0, 0);
        }

        // ── Settings dialog ────────────────────────────────────────
        void ShowSettings()
        {
            using var dlg = new SettingsForm(_modifier, _zoomStep,
                                              _zoomMin, _zoomMax, _smoothSpeed);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _modifier    = dlg.Modifier;
                _zoomStep    = dlg.ZoomStep;
                _zoomMin     = dlg.ZoomMin;
                _zoomMax     = dlg.ZoomMax;
                _smoothSpeed = dlg.SmoothSpeed;
                SaveSettings();
            }
        }

        // ── Cleanup ────────────────────────────────────────────────
        void ExitApp()
        {
            ResetZoom();
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
            if (_magInitialized)           MagUninitialize();
            _trayIcon?.Dispose();
            _smoothTimer?.Stop();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ExitApp();
            base.Dispose(disposing);
        }

        static float Clamp(float v, float lo, float hi) =>
            v < lo ? lo : v > hi ? hi : v;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Settings dialog
    // ═══════════════════════════════════════════════════════════════
    public class SettingsForm : Form
    {
        public string Modifier    { get; private set; }
        public float  ZoomStep    { get; private set; }
        public float  ZoomMin     { get; private set; }
        public float  ZoomMax     { get; private set; }
        public float  SmoothSpeed { get; private set; }

        readonly ComboBox _cbModifier;
        readonly TrackBar _tbStep, _tbSpeed, _tbMax;
        readonly Label    _lblStep, _lblSpeed, _lblMax;

        public SettingsForm(string modifier, float step,
                            float min, float max, float speed)
        {
            Modifier    = modifier;
            ZoomStep    = step;
            ZoomMin     = min;
            ZoomMax     = max;
            SmoothSpeed = speed;

            Text            = "TrayZoom – Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;
            ClientSize      = new Size(360, 280);
            BackColor       = Color.FromArgb(30, 30, 30);
            ForeColor       = Color.White;
            Font            = new Font("Segoe UI", 9.5f);

            int y = 18;

            AddLabel("Hotkey modifier:", 16, y);
            _cbModifier = new ComboBox
            {
                Left = 160, Top = y - 2, Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            _cbModifier.Items.AddRange(new object[] { "Win", "Ctrl", "Alt", "Ctrl+Alt" });
            _cbModifier.SelectedItem = modifier;
            if (_cbModifier.SelectedIndex < 0) _cbModifier.SelectedIndex = 0;
            Controls.Add(_cbModifier);
            y += 42;

            AddLabel("Zoom step per tick:", 16, y);
            _lblStep = AddValueLabel(StepDisplay(step), 300, y);
            _tbStep  = AddTrackBar(1, 10, (int)Math.Round(step / 0.05f), 16, y + 22);
            _tbStep.ValueChanged += (_, _) =>
                _lblStep.Text = StepDisplay(_tbStep.Value * 0.05f);
            y += 72;

            AddLabel("Maximum zoom:", 16, y);
            _lblMax = AddValueLabel($"{max:0.0}×", 300, y);
            _tbMax  = AddTrackBar(2, 16, (int)max, 16, y + 22);
            _tbMax.ValueChanged += (_, _) => _lblMax.Text = $"{_tbMax.Value}×";
            y += 72;

            AddLabel("Smoothness:", 16, y);
            _lblSpeed = AddValueLabel(SpeedLabel(speed), 300, y);
            _tbSpeed  = AddTrackBar(1, 10, SpeedToTick(speed), 16, y + 22);
            _tbSpeed.ValueChanged += (_, _) =>
                _lblSpeed.Text = SpeedLabel(TickToSpeed(_tbSpeed.Value));
            y += 72;

            var btnOk = new Button
            {
                Text         = "Save",
                Left         = ClientSize.Width - 170, Top = y,
                Width        = 72, Height = 28,
                BackColor    = Color.FromArgb(0, 120, 215),
                ForeColor    = Color.White,
                FlatStyle    = FlatStyle.Flat,
                DialogResult = DialogResult.OK,
            };
            btnOk.Click += (_, _) =>
            {
                Modifier    = (_cbModifier.SelectedItem as string) ?? "Alt";
                ZoomStep    = _tbStep.Value * 0.05f;
                ZoomMax     = _tbMax.Value;
                ZoomMin     = 1.0f;
                SmoothSpeed = TickToSpeed(_tbSpeed.Value);
            };

            var btnCancel = new Button
            {
                Text         = "Cancel",
                Left         = ClientSize.Width - 90, Top = y,
                Width        = 72, Height = 28,
                BackColor    = Color.FromArgb(60, 60, 60),
                ForeColor    = Color.White,
                FlatStyle    = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel,
            };

            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.Add(new Label
            {
                Text      = "Settings are saved to the registry.",
                Left      = 16, Top = y + 4,
                Width     = 220, Height = 20,
                ForeColor = Color.Gray,
                Font      = new Font("Segoe UI", 8f),
            });
        }

        void AddLabel(string text, int x, int y) =>
            Controls.Add(new Label
            {
                Text = text, Left = x, Top = y,
                Width = 200, Height = 20, ForeColor = Color.LightGray
            });

        Label AddValueLabel(string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text, Left = x, Top = y, Width = 50, Height = 20,
                ForeColor = Color.DeepSkyBlue,
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(lbl);
            return lbl;
        }

        TrackBar AddTrackBar(int min, int max, int val, int x, int y)
        {
            var tb = new TrackBar
            {
                Minimum = min, Maximum = max,
                Value   = Math.Max(min, Math.Min(max, val)),
                TickStyle = TickStyle.None,
                Left = x, Top = y, Width = 328, Height = 32,
                BackColor = Color.FromArgb(30, 30, 30),
            };
            Controls.Add(tb);
            return tb;
        }

        static string StepDisplay(float v) => $"{v:0.00}×";
        static string SpeedLabel(float s)  => s switch
        {
            <= 0.07f => "Silky",
            <= 0.11f => "Smooth",
            <= 0.16f => "Fluid",
            <= 0.22f => "Snappy",
            _        => "Fast",
        };
        static float TickToSpeed(int t)   => t * 0.03f + 0.04f;
        static int   SpeedToTick(float s) =>
            (int)Math.Round(Math.Max(1, Math.Min(10, (s - 0.04f) / 0.03f)));
    }
}
