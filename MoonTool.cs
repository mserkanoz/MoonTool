// MoonTool for Windows 10 — tray-icon moon phase display.
// Inspired by John Walker's classic MoonTool (fourmilab.ch); the phase
// calculation is adapted from his public-domain moontool algorithm.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MoonTool
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--make-icon")
            {
                MoonRender.WriteIco(args[1]);
                return 0;
            }

            bool created;
            using (Mutex m = new Mutex(true, "MoonTool_Win10_SingleInstance", out created))
            {
                if (!created) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
            return 0;
        }
    }

    struct MoonInfo
    {
        public double PhaseAngle;      // 0..360 degrees, 0 = new moon
        public double Illumination;    // 0..1
        public double AgeDays;
        public double DistanceKm;
        public double AngularDiameter; // degrees
        public double SunDistanceKm;
        public double SunAngularDiameter;
    }

    static class MoonCalc
    {
        const double Epoch = 2444238.5;        // 1980 January 0.0
        const double Elonge = 278.833540;      // sun ecliptic longitude at epoch
        const double Elongp = 282.596403;      // sun ecliptic longitude of perigee
        const double Eccent = 0.016718;        // earth orbit eccentricity
        const double SunSmax = 1.495985e8;     // earth orbit semi-major axis, km
        const double SunAngSiz = 0.533128;
        const double MMLong = 64.975464;       // moon mean longitude at epoch
        const double MMLongP = 349.383063;     // mean longitude of perigee at epoch
        const double MLNode = 151.950429;
        const double MEcc = 0.054900;
        const double MAngSiz = 0.5181;
        const double MSmax = 384401.0;
        public const double SynMonth = 29.53058868;

        static double Fix(double a) { return a - 360.0 * Math.Floor(a / 360.0); }
        static double Rad(double d) { return d * Math.PI / 180.0; }
        static double Deg(double r) { return r * 180.0 / Math.PI; }

        static double Kepler(double m, double ecc)
        {
            double e = m = Rad(m);
            double delta;
            do
            {
                delta = e - ecc * Math.Sin(e) - m;
                e -= delta / (1 - ecc * Math.Cos(e));
            } while (Math.Abs(delta) > 1e-6);
            return e;
        }

        public static double JulianDate(DateTime utc) { return utc.ToOADate() + 2415018.5; }

        public static DateTime FromJulian(double jd)
        {
            return DateTime.SpecifyKind(DateTime.FromOADate(jd - 2415018.5), DateTimeKind.Utc);
        }

        public static MoonInfo Calc(double jd)
        {
            double day = jd - Epoch;

            // Sun
            double n = Fix((360 / 365.2422) * day);
            double m = Fix(n + Elonge - Elongp);
            double ec = Kepler(m, Eccent);
            ec = Math.Sqrt((1 + Eccent) / (1 - Eccent)) * Math.Tan(ec / 2);
            ec = 2 * Deg(Math.Atan(ec));
            double lambdaSun = Fix(ec + Elongp);
            double f = (1 + Eccent * Math.Cos(Rad(ec))) / (1 - Eccent * Eccent);

            // Moon
            double ml = Fix(13.1763966 * day + MMLong);
            double mm = Fix(ml - 0.1114041 * day - MMLongP);
            double ev = 1.2739 * Math.Sin(Rad(2 * (ml - lambdaSun) - mm));
            double ae = 0.1858 * Math.Sin(Rad(m));
            double a3 = 0.37 * Math.Sin(Rad(m));
            double mmp = mm + ev - ae - a3;
            double mec = 6.2886 * Math.Sin(Rad(mmp));
            double a4 = 0.214 * Math.Sin(Rad(2 * mmp));
            double lp = ml + ev + mec - ae + a4;
            double v = 0.6583 * Math.Sin(Rad(2 * (lp - lambdaSun)));
            double lpp = lp + v;

            double age = lpp - lambdaSun;
            double dist = (MSmax * (1 - MEcc * MEcc)) / (1 + MEcc * Math.Cos(Rad(mmp + mec)));

            MoonInfo r;
            r.PhaseAngle = Fix(age);
            r.Illumination = (1 - Math.Cos(Rad(age))) / 2;
            r.AgeDays = SynMonth * (r.PhaseAngle / 360.0);
            r.DistanceKm = dist;
            r.AngularDiameter = MAngSiz / (dist / MSmax);
            r.SunDistanceKm = SunSmax / f;
            r.SunAngularDiameter = f * SunAngSiz;
            return r;
        }

        static double Diff(double jd, double target)
        {
            double d = Fix(Calc(jd).PhaseAngle - target);
            return d > 180 ? d - 360 : d;
        }

        // First moment after startUtc when the phase angle reaches target (0/90/180/270).
        public static DateTime NextPhase(DateTime startUtc, double target)
        {
            double jd = JulianDate(startUtc);
            double prev = Diff(jd, target);
            for (int i = 0; i < 200; i++)
            {
                double jd2 = jd + 0.25;
                double cur = Diff(jd2, target);
                if (prev < 0 && cur >= 0 && cur - prev < 90)
                {
                    double lo = jd, hi = jd2;
                    for (int k = 0; k < 40; k++)
                    {
                        double mid = (lo + hi) / 2;
                        if (Diff(mid, target) < 0) lo = mid; else hi = mid;
                    }
                    return FromJulian(hi);
                }
                jd = jd2;
                prev = cur;
            }
            return startUtc;
        }

        // [last new, first quarter, full, last quarter, next new] of the current lunation.
        public static DateTime[] Lunation(DateTime nowUtc)
        {
            DateTime nm = NextPhase(nowUtc.AddDays(-32), 0);
            while (true)
            {
                DateTime next = NextPhase(nm.AddDays(1), 0);
                if (next > nowUtc) break;
                nm = next;
            }
            return new DateTime[] {
                nm,
                NextPhase(nm.AddHours(1), 90),
                NextPhase(nm.AddHours(1), 180),
                NextPhase(nm.AddHours(1), 270),
                NextPhase(nm.AddDays(1), 0)
            };
        }

        public static string PhaseName(double a)
        {
            if (a < 6 || a >= 354) return "Yeni Ay";
            if (a < 84) return "Büyüyen Hilal";
            if (a < 96) return "İlk Dördün";
            if (a < 174) return "Büyüyen Şişkin Ay";
            if (a < 186) return "Dolunay";
            if (a < 264) return "Küçülen Şişkin Ay";
            if (a < 276) return "Son Dördün";
            return "Küçülen Hilal";
        }
    }

    static class MoonRender
    {
        static readonly Color Lit = Color.FromArgb(255, 246, 216);
        static readonly Color Dark = Color.FromArgb(62, 64, 78);

        public static Bitmap Draw(int size, double phase, bool southern, bool shade)
        {
            const int ss = 4;
            int[] px = new int[size * size];
            double cx = size / 2.0, cy = size / 2.0, r = size / 2.0 - 0.25;
            double c = Math.Cos(phase * Math.PI / 180);
            bool waxing = phase < 180;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    double sr = 0, sg = 0, sb = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            double nx = (x + (sx + 0.5) / ss - cx) / r;
                            double ny = (y + (sy + 0.5) / ss - cy) / r;
                            double d2 = nx * nx + ny * ny;
                            if (d2 > 1) continue;
                            inside++;
                            double k = shade ? 0.72 + 0.28 * Math.Sqrt(1 - d2) : 1.0;
                            double mx = southern ? -nx : nx;
                            double w = Math.Sqrt(1 - ny * ny);
                            bool lit = waxing ? mx >= w * c : mx <= -w * c;
                            Color col = lit ? Lit : Dark;
                            sr += col.R * k; sg += col.G * k; sb += col.B * k;
                        }
                    }
                    if (inside == 0) continue;
                    int a = inside * 255 / (ss * ss);
                    px[y * size + x] = (a << 24)
                        | ((int)(sr / inside) << 16)
                        | ((int)(sg / inside) << 8)
                        | (int)(sb / inside);
                }
            }

            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, size, size),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        // Writes a multi-size PNG-based .ico of a waxing gibbous moon (used as the exe icon).
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 256 };
            byte[][] pngs = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap b = Draw(sizes[i], 135, false, sizes[i] >= 32))
                using (MemoryStream ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    pngs[i] = ms.ToArray();
                }
            }
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)1); w.Write((ushort)32);
                    w.Write((uint)pngs[i].Length);
                    w.Write((uint)offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) w.Write(p);
            }
        }
    }

    class TrayContext : ApplicationContext
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        const string SettingsKey = @"Software\MoonTool";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "MoonTool";

        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer;
        readonly ToolStripMenuItem southItem, startupItem;
        DetailsForm details;
        IntPtr hIcon = IntPtr.Zero;
        Icon currentIcon;
        bool southern;

        public TrayContext()
        {
            southern = ReadSouthern();

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem showItem = new ToolStripMenuItem("Ayrıntıları göster", null, delegate { ShowDetails(); });
            showItem.Font = new Font(showItem.Font, FontStyle.Bold);
            southItem = new ToolStripMenuItem("Güney yarımküre görünümü", null, delegate { ToggleSouthern(); });
            southItem.Checked = southern;
            startupItem = new ToolStripMenuItem("Windows açılışında başlat", null, delegate { ToggleStartup(); });
            startupItem.Checked = IsStartupEnabled();
            menu.Items.Add(showItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(southItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripMenuItem("Hakkında", null, delegate { ShowAbout(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Çıkış", null, delegate { Quit(); }));

            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowDetails();
            };

            UpdateIcon();
            tray.Visible = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 5 * 60 * 1000;
            timer.Tick += delegate { UpdateIcon(); };
            timer.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        }

        void OnPowerModeChanged(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) UpdateIcon();
        }

        void OnDisplayChanged(object s, EventArgs e) { UpdateIcon(); }

        public bool Southern { get { return southern; } }

        public Icon CurrentIcon { get { return currentIcon; } }

        void UpdateIcon()
        {
            MoonInfo mi = MoonCalc.Calc(MoonCalc.JulianDate(DateTime.UtcNow));
            int size = SystemInformation.SmallIconSize.Width;
            IntPtr h;
            using (Bitmap b = MoonRender.Draw(size, mi.PhaseAngle, southern, false))
                h = b.GetHicon();
            Icon ic = Icon.FromHandle(h);
            tray.Icon = ic;

            if (currentIcon != null) currentIcon.Dispose();
            if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
            currentIcon = ic;
            hIcon = h;

            string tip = string.Format(CultureInfo.GetCultureInfo("tr-TR"),
                "{0}\n%{1:0} aydınlık · {2:0.0} günlük",
                MoonCalc.PhaseName(mi.PhaseAngle), mi.Illumination * 100, mi.AgeDays);
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void ShowDetails()
        {
            if (details == null || details.IsDisposed)
            {
                details = new DetailsForm(this);
                details.FormClosed += delegate { details = null; };
                details.Show();
            }
            else
            {
                if (details.WindowState == FormWindowState.Minimized)
                    details.WindowState = FormWindowState.Normal;
            }
            details.Activate();
        }

        void ToggleSouthern()
        {
            southern = !southern;
            southItem.Checked = southern;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    k.SetValue("Southern", southern ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
            UpdateIcon();
            if (details != null && !details.IsDisposed) details.RefreshData(true);
        }

        static bool ReadSouthern()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                    return k != null && Convert.ToInt32(k.GetValue("Southern", 0)) == 1;
            }
            catch { return false; }
        }

        static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunValue) != null;
            }
            catch { return false; }
        }

        void ToggleStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (IsStartupEnabled()) k.DeleteValue(RunValue, false);
                    else k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Başlangıç ayarı değiştirilemedi:\n" + ex.Message, "MoonTool",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            startupItem.Checked = IsStartupEnabled();
        }

        void ShowAbout()
        {
            MessageBox.Show(
                "MoonTool — Windows 10 sürümü\n\n" +
                "Ay'ın o anki evresini sistem tepsisinde gösterir.\n\n" +
                "John Walker'ın (fourmilab.ch) klasik MoonTool programına saygıyla; " +
                "Ay hesaplamaları onun kamu malı algoritmasından uyarlanmıştır.",
                "Hakkında", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Quit()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            timer.Stop();
            if (details != null && !details.IsDisposed) details.Close();
            tray.Visible = false;
            tray.Dispose();
            if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
            ExitThread();
        }
    }

    class DetailsForm : Form
    {
        static readonly CultureInfo TR = CultureInfo.GetCultureInfo("tr-TR");
        static readonly string[] Names = {
            "Evre", "Aydınlanma", "Ay'ın yaşı", "Dünya'ya uzaklık", "Açısal çap",
            "Güneş'e uzaklık", "Yerel saat", "Evrensel saat (UTC)", "Jülyen tarihi", "",
            "Son Yeni Ay", "İlk Dördün", "Dolunay", "Son Dördün", "Sonraki Yeni Ay"
        };

        readonly TrayContext owner;
        readonly PictureBox pic;
        readonly Label[] vals = new Label[Names.Length];
        readonly System.Windows.Forms.Timer tick;
        readonly int picSize;
        DateTime lastSlowUpdate = DateTime.MinValue;

        public DetailsForm(TrayContext owner)
        {
            this.owner = owner;
            SuspendLayout();
            Text = "MoonTool";
            Icon = owner.CurrentIcon;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            float scale;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            picSize = (int)(170 * scale);

            TableLayoutPanel root = new TableLayoutPanel();
            root.ColumnCount = 2;
            root.RowCount = 1;
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Padding = new Padding((int)(12 * scale));

            pic = new PictureBox();
            pic.Size = new Size(picSize, picSize);
            pic.BackColor = Color.Black;
            pic.SizeMode = PictureBoxSizeMode.CenterImage;
            pic.Margin = new Padding(0, 0, (int)(16 * scale), 0);
            pic.Anchor = AnchorStyles.Top;

            TableLayoutPanel info = new TableLayoutPanel();
            info.ColumnCount = 2;
            info.RowCount = Names.Length;
            info.AutoSize = true;
            info.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            for (int i = 0; i < Names.Length; i++)
            {
                Label n = new Label();
                n.AutoSize = true;
                n.Text = Names[i].Length > 0 ? Names[i] + ":" : " ";
                n.ForeColor = SystemColors.GrayText;
                n.Margin = new Padding(0, 1, (int)(10 * scale), 1);
                Label v = new Label();
                v.AutoSize = true;
                v.Margin = new Padding(0, 1, 0, 1);
                if (i == 0) v.Font = new Font(Font, FontStyle.Bold);
                vals[i] = v;
                info.Controls.Add(n, 0, i);
                info.Controls.Add(v, 1, i);
            }

            root.Controls.Add(pic, 0, 0);
            root.Controls.Add(info, 1, 0);
            Controls.Add(root);
            ResumeLayout(true);

            RefreshData(true);

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += delegate { RefreshData(false); };
            tick.Start();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop();
            tick.Dispose();
            if (pic.Image != null) pic.Image.Dispose();
            base.OnFormClosed(e);
        }

        static string FormatDate(DateTime utc)
        {
            return utc.ToLocalTime().ToString("d MMMM yyyy dddd, HH:mm", TR);
        }

        public void RefreshData(bool force)
        {
            DateTime now = DateTime.UtcNow;
            double jd = MoonCalc.JulianDate(now);
            MoonInfo mi = MoonCalc.Calc(jd);

            double ageMin = mi.AgeDays * 24 * 60;
            int d = (int)(ageMin / 1440), h = (int)(ageMin % 1440 / 60), m = (int)(ageMin % 60);

            vals[0].Text = MoonCalc.PhaseName(mi.PhaseAngle);
            vals[1].Text = string.Format(TR, "%{0:0.0}", mi.Illumination * 100);
            vals[2].Text = string.Format(TR, "{0} gün, {1} saat, {2} dakika", d, h, m);
            vals[3].Text = string.Format(TR, "{0:N0} km  ({1:0.0} Dünya yarıçapı)", mi.DistanceKm, mi.DistanceKm / 6378.14);
            vals[4].Text = string.Format(TR, "{0:0.0000}°", mi.AngularDiameter);
            vals[5].Text = string.Format(TR, "{0:N0} km  ({1:0.000} AB)", mi.SunDistanceKm, mi.SunDistanceKm / 1.495985e8);
            vals[6].Text = now.ToLocalTime().ToString("d MMMM yyyy dddd, HH:mm:ss", TR);
            vals[7].Text = now.ToString("d MMMM yyyy, HH:mm:ss", TR);
            vals[8].Text = jd.ToString("0.00000", TR);

            if (force || (now - lastSlowUpdate).TotalSeconds >= 60)
            {
                lastSlowUpdate = now;
                DateTime[] lun = MoonCalc.Lunation(now);
                for (int i = 0; i < 5; i++) vals[10 + i].Text = FormatDate(lun[i]);

                Image old = pic.Image;
                pic.Image = MoonRender.Draw((int)(picSize * 0.86), mi.PhaseAngle, owner.Southern, true);
                if (old != null) old.Dispose();
                Icon = owner.CurrentIcon;
            }
        }
    }
}
