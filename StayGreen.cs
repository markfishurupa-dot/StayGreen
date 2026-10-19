using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("StayGreen")]
[assembly: AssemblyDescription("Keeps Windows active with periodic Ctrl+Shift pulses")]
[assembly: AssemblyProduct("StayGreen")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace StayGreenTray
{
    internal static class Program
    {
        private static Mutex singleInstanceMutex;

        [STAThread]
        private static void Main(string[] args)
        {
            bool selfTest = args.Length > 0 &&
                string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (selfTest)
            {
                using (TrayApplicationContext context = new TrayApplicationContext(true))
                {
                }

                return;
            }

            bool createdNew;
            singleInstanceMutex = new Mutex(true, @"Local\StayGreenTray", out createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "StayGreen Tray is already running.",
                    "StayGreen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                using (TrayApplicationContext context = new TrayApplicationContext())
                {
                    Application.Run(context);
                }
            }
            finally
            {
                singleInstanceMutex.ReleaseMutex();
                singleInstanceMutex.Dispose();
            }
        }
    }

    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;
        private const uint EsDisplayRequired = 0x00000002;
        private const byte VkControl = 0x11;
        private const byte VkShift = 0x10;
        private const uint KeyeventfKeyup = 0x0002;

        private readonly NotifyIcon notifyIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Icon activeIcon;
        private readonly Icon pausedIcon;

        private ToolStripMenuItem statusItem;
        private ToolStripMenuItem lastPulseItem;
        private ToolStripMenuItem nextPulseItem;
        private ToolStripMenuItem toggleItem;
        private ToolStripMenuItem intervalMenu;

        private int intervalSeconds = 240;
        private bool isActive = true;
        private DateTime? lastPulse;
        private DateTime nextPulse;
        private bool disposed;

        public TrayApplicationContext()
            : this(false)
        {
        }

        public TrayApplicationContext(bool selfTest)
        {
            activeIcon = CreateTrayIcon(false);
            pausedIcon = CreateTrayIcon(true);

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = activeIcon;
            notifyIcon.Visible = !selfTest;
            notifyIcon.ContextMenuStrip = BuildContextMenu();
            notifyIcon.DoubleClick += delegate
            {
                if (isActive)
                {
                    SendActivityPulse();
                }
                else
                {
                    SetActiveState(true);
                }
            };

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                if (isActive && DateTime.Now >= nextPulse)
                {
                    SendActivityPulse();
                    return;
                }

                UpdateTrayText();
            };

            SetPulseInterval(intervalSeconds);
            nextPulse = DateTime.Now.AddSeconds(2);
            UpdateTrayText();

            if (selfTest)
            {
                return;
            }

            SetSystemAwake(true);

            notifyIcon.ShowBalloonTip(
                1500,
                "StayGreen started",
                "Right-click the tray icon for controls.",
                ToolTipIcon.Info);

            timer.Start();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            statusItem = new ToolStripMenuItem();
            statusItem.Enabled = false;
            menu.Items.Add(statusItem);

            lastPulseItem = new ToolStripMenuItem();
            lastPulseItem.Enabled = false;
            menu.Items.Add(lastPulseItem);

            nextPulseItem = new ToolStripMenuItem();
            nextPulseItem.Enabled = false;
            menu.Items.Add(nextPulseItem);

            menu.Items.Add(new ToolStripSeparator());

            toggleItem = new ToolStripMenuItem();
            toggleItem.Click += delegate { SetActiveState(!isActive); };
            menu.Items.Add(toggleItem);

            ToolStripMenuItem pulseNowItem = new ToolStripMenuItem("Pulse now");
            pulseNowItem.Click += delegate { SendActivityPulse(); };
            menu.Items.Add(pulseNowItem);

            intervalMenu = new ToolStripMenuItem("Interval");
            menu.Items.Add(intervalMenu);

            int[] intervals = new int[] { 60, 120, 240, 300, 600, 900 };
            foreach (int seconds in intervals)
            {
                ToolStripMenuItem item = new ToolStripMenuItem("Every " + FormatInterval(seconds));
                item.Tag = seconds;
                item.Click += delegate(object sender, EventArgs args)
                {
                    ToolStripMenuItem clickedItem = (ToolStripMenuItem)sender;
                    SetPulseInterval((int)clickedItem.Tag);
                };
                intervalMenu.DropDownItems.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem openFolderItem = new ToolStripMenuItem("Open folder");
            openFolderItem.Click += delegate
            {
                System.Diagnostics.Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory);
            };
            menu.Items.Add(openFolderItem);

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(exitItem);

            return menu;
        }

        private void SetActiveState(bool enabled)
        {
            isActive = enabled;

            if (enabled)
            {
                notifyIcon.Icon = activeIcon;
                nextPulse = DateTime.Now.AddSeconds(2);
                SetSystemAwake(true);
                notifyIcon.ShowBalloonTip(1500, "StayGreen active", "Activity pulses are enabled.", ToolTipIcon.Info);
            }
            else
            {
                notifyIcon.Icon = pausedIcon;
                SetSystemAwake(false);
                notifyIcon.ShowBalloonTip(1500, "StayGreen paused", "Activity pulses are paused.", ToolTipIcon.Info);
            }

            UpdateTrayText();
        }

        private void SetPulseInterval(int seconds)
        {
            intervalSeconds = seconds;
            nextPulse = DateTime.Now.AddSeconds(intervalSeconds);

            foreach (ToolStripItem stripItem in intervalMenu.DropDownItems)
            {
                ToolStripMenuItem menuItem = stripItem as ToolStripMenuItem;
                if (menuItem != null && menuItem.Tag is int)
                {
                    menuItem.Checked = ((int)menuItem.Tag == intervalSeconds);
                }
            }

            UpdateTrayText();
        }

        private void SendActivityPulse()
        {
            bool controlDown = false;
            bool shiftDown = false;

            try
            {
                keybd_event(VkControl, 0, 0, UIntPtr.Zero);
                controlDown = true;
                keybd_event(VkShift, 0, 0, UIntPtr.Zero);
                shiftDown = true;
                Thread.Sleep(60);

                SetSystemAwake(true);
                lastPulse = DateTime.Now;
                nextPulse = lastPulse.Value.AddSeconds(intervalSeconds);
                UpdateTrayText();
            }
            catch (Exception ex)
            {
                notifyIcon.ShowBalloonTip(
                    3000,
                    "StayGreen pulse failed",
                    ex.Message,
                    ToolTipIcon.Error);
            }
            finally
            {
                if (shiftDown)
                {
                    keybd_event(VkShift, 0, KeyeventfKeyup, UIntPtr.Zero);
                }

                if (controlDown)
                {
                    keybd_event(VkControl, 0, KeyeventfKeyup, UIntPtr.Zero);
                }
            }
        }

        private void UpdateTrayText()
        {
            string state = isActive ? "Active" : "Paused";
            string last = lastPulse.HasValue ? lastPulse.Value.ToString("HH:mm:ss") : "not yet";
            string next = isActive ? nextPulse.ToString("HH:mm:ss") : "-";
            string interval = FormatInterval(intervalSeconds);

            statusItem.Text = "Status: " + state;
            lastPulseItem.Text = "Last pulse: " + last;
            nextPulseItem.Text = "Next pulse: " + next;
            toggleItem.Text = isActive ? "Pause" : "Resume";
            notifyIcon.Text = TrimForTray("StayGreen - " + state + ", every " + interval);
        }

        private static void SetSystemAwake(bool enabled)
        {
            if (enabled)
            {
                SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                return;
            }

            SetThreadExecutionState(EsContinuous);
        }

        private static string FormatInterval(int seconds)
        {
            if (seconds < 60)
            {
                return seconds + " sec";
            }

            double minutes = seconds / 60.0;
            if (Math.Abs(minutes - Math.Floor(minutes)) < 0.001)
            {
                return ((int)minutes) + " min";
            }

            return minutes.ToString("0.0") + " min";
        }

        private static string TrimForTray(string text)
        {
            if (text.Length <= 63)
            {
                return text;
            }

            return text.Substring(0, 63);
        }

        private static Icon CreateTrayIcon(bool paused)
        {
            Icon associatedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (associatedIcon == null)
            {
                throw new InvalidOperationException("StayGreen executable icon could not be loaded.");
            }

            using (associatedIcon)
            {
                if (!paused)
                {
                    return (Icon)associatedIcon.Clone();
                }

                using (Bitmap source = associatedIcon.ToBitmap())
                using (Bitmap bitmap = new Bitmap(32, 32))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, new Rectangle(0, 0, 32, 32));
                    using (SolidBrush dim = new SolidBrush(Color.FromArgb(155, 65, 72, 82)))
                    using (SolidBrush pause = new SolidBrush(Color.White))
                    {
                        graphics.FillRectangle(dim, 0, 0, 32, 32);
                        graphics.FillRectangle(pause, 11, 10, 3, 12);
                        graphics.FillRectangle(pause, 18, 10, 3, 12);
                    }

                    IntPtr handle = bitmap.GetHicon();
                    try
                    {
                        using (Icon icon = Icon.FromHandle(handle))
                        {
                            return (Icon)icon.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(handle);
                    }
                }
            }
        }
        private void ExitApplication()
        {
            Cleanup();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Cleanup();
            }

            base.Dispose(disposing);
        }

        private void Cleanup()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
            }

            SetSystemAwake(false);

            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }

            if (activeIcon != null)
            {
                activeIcon.Dispose();
            }

            if (pausedIcon != null)
            {
                pausedIcon.Dispose();
            }
        }
    }
}
