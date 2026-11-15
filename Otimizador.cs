// Otimizador: um botão que libera RAM e limpa lixo do PC sem fechar o jogo aberto.
// Compila com o csc do .NET Framework que já vem no Windows (C# 5): veja build.bat.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Otimizador")]
[assembly: System.Reflection.AssemblyProduct("Otimizador")]
[assembly: System.Reflection.AssemblyVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.1.0")]

namespace Otimizador
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                         ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("dnsapi.dll")] public static extern bool DnsFlushResolverCache();
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, int flags);
        [DllImport("ntdll.dll")] public static extern int NtSetSystemInformation(int cls, ref int info, int len);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr h, int access, out IntPtr tok);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct TOKEN_PRIV { public int Count; public long Luid; public int Attr; }
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr tok, bool disableAll, ref TOKEN_PRIV np, int len, IntPtr prev, IntPtr retLen);

        public static bool EnablePrivilege(string name)
        {
            IntPtr tok;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x20 | 0x8, out tok)) return false;
            try
            {
                long luid;
                if (!LookupPrivilegeValue(null, name, out luid)) return false;
                TOKEN_PRIV tp = new TOKEN_PRIV { Count = 1, Luid = luid, Attr = 2 };
                return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(tok); }
        }

        public static Native.MEMORYSTATUSEX Mem()
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(m);
            return m;
        }
    }

    // Tudo o que mexe no PC fica aqui.
    static class Engine
    {
        // Nunca encolhidos nem listados para fechar: jogo, núcleo do Windows e o próprio app.
        static readonly HashSet<string> Protegidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "RobloxPlayerBeta", "Roblox", "RobloxStudioBeta", "RobloxCrashHandler",
            "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "LsaIso", "dwm", "explorer", "fontdrvhost", "audiodg", "MsMpEng"
        };
        // Podem ser encolhidos, mas nunca aparecem na lista de "fechar" (fechar quebraria o Windows).
        static readonly HashSet<string> NuncaFechar = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "svchost", "sihost", "ctfmon", "conhost", "dllhost", "taskhostw", "RuntimeBroker", "spoolsv",
            "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "SearchHost", "SearchIndexer",
            "SecurityHealthService", "SecurityHealthSystray", "SgrmBroker", "WUDFHost", "WmiPrvSE",
            "ApplicationFrameHost", "SystemSettings", "LockApp", "Widgets", "WidgetService", "smartscreen",
            "NisSrv", "atiesrxx", "atieclxx", "amdfendrsr", "RadeonSoftware", "AMDRSServ", "Otimizador"
        };

        public static bool IsAdmin()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static int ForegroundPid()
        {
            int pid;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            return pid;
        }

        static bool IsProtected(Process p, int self, int fg)
        {
            return p.Id == self || p.Id == fg || p.Id <= 4 || Protegidos.Contains(p.ProcessName)
                || p.ProcessName.StartsWith("Roblox", StringComparison.OrdinalIgnoreCase);
        }

        // Encolhe a RAM dos programas em segundo plano. Não fecha nada: o Windows traz de volta o que
        // cada um precisar. Devolve quantos MB ficaram disponíveis a mais.
        public static double TrimRam()
        {
            double antes = Native.Mem().ullAvailPhys / 1048576.0;
            int self = Process.GetCurrentProcess().Id, fg = ForegroundPid();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (IsProtected(p, self, fg)) continue;
                    IntPtr h = Native.OpenProcess(0x1000 | 0x0100, false, p.Id); // consultar + cotas
                    if (h == IntPtr.Zero) continue;
                    Native.EmptyWorkingSet(h);
                    Native.CloseHandle(h);
                }
                catch { }
                finally { p.Dispose(); }
            }
            System.Threading.Thread.Sleep(1500);
            return Native.Mem().ullAvailPhys / 1048576.0 - antes;
        }

        // Só como administrador: limpa a "lista de espera" (cache de memória), como o RAMMap.
        public static bool PurgeStandby()
        {
            if (!IsAdmin() || !Native.EnablePrivilege("SeProfileSingleProcessPrivilege")) return false;
            int cmd = 4; // MemoryPurgeStandbyList
            return Native.NtSetSystemInformation(80, ref cmd, 4) == 0;
        }

        // Apaga temporários com mais de 1 dia; arquivos em uso são pulados.
        public static long CleanTemp()
        {
            List<string> dirs = new List<string> {
                Path.GetTempPath(),
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\CrashDumps"),
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive"),
            };
            if (IsAdmin())
            {
                dirs.Add(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Temp"));
                dirs.Add(Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"));
                dirs.Add(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\SoftwareDistribution\Download"));
            }
            long total = 0;
            DateTime limite = DateTime.Now.AddDays(-1);
            foreach (string d in dirs) total += CleanDir(d, limite, false);
            return total;
        }

        static long CleanDir(string dir, DateTime limite, bool removeSelf)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                DirectoryInfo di = new DirectoryInfo(dir);
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) return 0; // não segue atalhos de pasta
                foreach (FileInfo f in di.GetFiles())
                {
                    try
                    {
                        if (f.LastWriteTime > limite) continue;
                        long len = f.Length;
                        if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                        f.Delete();
                        total += len;
                    }
                    catch { }
                }
                foreach (DirectoryInfo sub in di.GetDirectories()) total += CleanDir(sub.FullName, limite, true);
                if (removeSelf && di.GetFileSystemInfos().Length == 0 && di.LastWriteTime <= limite) di.Delete();
            }
            catch { }
            return total;
        }

        public static void FlushDns() { try { Native.DnsFlushResolverCache(); } catch { } }

        public static void EmptyRecycleBin() { try { Native.SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4); } catch { } }

        public class Consumidor { public string Nome; public long Bytes; public int Qtd; }

        // Programas do usuário que mais gastam RAM, somando as várias janelas/processos de cada um.
        public static List<Consumidor> TopConsumers(int n)
        {
            int self = Process.GetCurrentProcess().Id, fg = ForegroundPid();
            int sessao = Process.GetCurrentProcess().SessionId;
            Dictionary<string, Consumidor> map = new Dictionary<string, Consumidor>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.SessionId != sessao || IsProtected(p, self, fg) || NuncaFechar.Contains(p.ProcessName)) continue;
                    Consumidor c;
                    if (!map.TryGetValue(p.ProcessName, out c)) map[p.ProcessName] = c = new Consumidor { Nome = p.ProcessName };
                    c.Bytes += p.PrivateMemorySize64;
                    c.Qtd++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return map.Values.OrderByDescending(c => c.Bytes).Take(n).ToList();
        }

        public static int Kill(string nome)
        {
            if (Protegidos.Contains(nome) || NuncaFechar.Contains(nome)) return 0;
            int n = 0;
            foreach (Process p in Process.GetProcessesByName(nome))
            {
                try { p.Kill(); n++; } catch { }
                finally { p.Dispose(); }
            }
            return n;
        }

        public static void Log(string linha)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "otimizador_log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + linha + Environment.NewLine);
            }
            catch { }
        }
    }

    // Barra de uso desenhada à mão (a ProgressBar do Windows não aceita cor).
    class Meter : Control
    {
        public double Value; // 0..1
        public Meter() { DoubleBuffered = true; Height = 10; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(48, 50, 58));
            Color c = Value > 0.85 ? Color.FromArgb(235, 87, 87) : Value > 0.7 ? Color.FromArgb(242, 181, 68) : Color.FromArgb(76, 201, 140);
            using (SolidBrush b = new SolidBrush(c))
                e.Graphics.FillRectangle(b, 0, 0, (int)(Width * Math.Max(0, Math.Min(1, Value))), Height);
        }
    }

    class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 25, 30), Card = Color.FromArgb(34, 36, 43),
            Fg = Color.FromArgb(235, 236, 240), Muted = Color.FromArgb(150, 154, 166), Accent = Color.FromArgb(88, 101, 242);

        Label ramLbl, cpuLbl, diskLbl, result, adminLbl;
        Meter ramBar, cpuBar;
        Button go, killBtn, adminBtn;
        CheckBox cTemp, cDns, cBin, cAuto;
        ListView list;
        Timer tick;
        NotifyIcon tray;
        long lastIdle, lastKernel, lastUser;
        DateTime lastAuto = DateTime.MinValue;
        bool busy;

        public MainForm()
        {
            Text = "Otimizador 0.1" + (Engine.IsAdmin() ? " (administrador)" : "");
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Bg; ForeColor = Fg;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 640);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Label title = L("⚡ Otimizador", 20, 14, 16f, FontStyle.Bold, Fg);
            Label sub = L("Libera RAM e limpa lixo sem fechar seu jogo.", 22, 50, 9f, FontStyle.Regular, Muted);

            Panel stats = CardPanel(20, 80, 440, 128);
            ramLbl = L("RAM", 14, 10, 9.5f, FontStyle.Regular, Fg, stats);
            ramBar = new Meter { Left = 14, Top = 34, Width = 412 }; stats.Controls.Add(ramBar);
            cpuLbl = L("CPU", 14, 52, 9.5f, FontStyle.Regular, Fg, stats);
            cpuBar = new Meter { Left = 14, Top = 76, Width = 412 }; stats.Controls.Add(cpuBar);
            diskLbl = L("Disco C:", 14, 96, 9.5f, FontStyle.Regular, Muted, stats);

            go = new Button { Text = "Otimizar agora", Left = 20, Top = 222, Width = 440, Height = 56,
                FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold), Cursor = Cursors.Hand };
            go.FlatAppearance.BorderSize = 0;
            go.Click += delegate { RunOptimize(false); };
            Controls.Add(go);

            result = L("Protege o Roblox, o app em primeiro plano e o Windows.", 22, 286, 9.5f, FontStyle.Regular, Muted);
            result.AutoSize = false; result.Width = 440; result.Height = 40;

            cTemp = Chk("Limpar arquivos temporários", 22, 328, true);
            cDns = Chk("Limpar cache de DNS (internet)", 250, 328, true);
            cBin = Chk("Esvaziar a lixeira", 22, 354, false);
            cAuto = Chk("Automático: RAM acima de 85%", 250, 354, false);
            cAuto.CheckedChanged += delegate { Engine.Log("Automático " + (cAuto.Checked ? "ligado" : "desligado")); };

            L("Programas que mais gastam RAM", 20, 392, 10.5f, FontStyle.Bold, Fg);
            list = new ListView { Left = 20, Top = 418, Width = 440, Height = 150, View = View.Details,
                FullRowSelect = true, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Card, ForeColor = Fg, BorderStyle = BorderStyle.None };
            list.Columns.Add("Programa", 250); list.Columns.Add("RAM", 100, HorizontalAlignment.Right); list.Columns.Add("Abertos", 70, HorizontalAlignment.Right);
            Controls.Add(list);

            killBtn = SmallButton("Fechar selecionado", 20, 576, 150);
            killBtn.Click += delegate { KillSelected(); };
            Button refresh = SmallButton("Atualizar lista", 178, 576, 120);
            refresh.Click += delegate { RefreshList(); };

            adminLbl = L(Engine.IsAdmin() ? "Modo forte ligado: limpa também o cache de memória e o Temp do Windows."
                                          : "Modo forte limpa mais (cache de memória e Temp do Windows).",
                20, 612, 8.5f, FontStyle.Regular, Muted);
            adminLbl.AutoSize = false; adminLbl.Width = Engine.IsAdmin() ? 440 : 300;
            if (!Engine.IsAdmin())
            {
                adminBtn = SmallButton("Modo forte (admin)", 330, 576, 130);
                adminBtn.Click += delegate { RelaunchAdmin(); };
            }

            tray = new NotifyIcon { Icon = Icon, Text = "Otimizador", Visible = false };
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; tray.Visible = false; };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && cAuto.Checked)
                {
                    Hide(); tray.Visible = true;
                    tray.ShowBalloonTip(2000, "Otimizador", "Continua cuidando da RAM aqui embaixo.", ToolTipIcon.Info);
                }
            };
            FormClosed += delegate { tray.Visible = false; };

            Native.GetSystemTimes(out lastIdle, out lastKernel, out lastUser);
            tick = new Timer { Interval = 1000 };
            tick.Tick += delegate { UpdateStats(); };
            tick.Start();
            UpdateStats();
            Shown += delegate { RefreshList(); };
        }

        Label L(string t, int x, int y, float size, FontStyle st, Color c, Control parent = null)
        {
            Label l = new Label { Text = t, Left = x, Top = y, AutoSize = true, ForeColor = c, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", size, st) };
            (parent ?? this).Controls.Add(l);
            return l;
        }

        Panel CardPanel(int x, int y, int w, int h)
        {
            Panel p = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = Card };
            Controls.Add(p);
            return p;
        }

        CheckBox Chk(string t, int x, int y, bool on)
        {
            CheckBox c = new CheckBox { Text = t, Left = x, Top = y, AutoSize = true, Checked = on, ForeColor = Fg };
            Controls.Add(c);
            return c;
        }

        Button SmallButton(string t, int x, int y, int w)
        {
            Button b = new Button { Text = t, Left = x, Top = y, Width = w, Height = 30, FlatStyle = FlatStyle.Flat,
                BackColor = Card, ForeColor = Fg, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(60, 63, 74);
            Controls.Add(b);
            return b;
        }

        void UpdateStats()
        {
            Native.MEMORYSTATUSEX m = Native.Mem();
            double total = m.ullTotalPhys / 1073741824.0, livre = m.ullAvailPhys / 1073741824.0;
            ramBar.Value = m.dwMemoryLoad / 100.0; ramBar.Invalidate();
            ramLbl.Text = string.Format("RAM  {0}%   ·   {1:0.0} GB livres de {2:0.0} GB", m.dwMemoryLoad, livre, total);

            long idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                long sys = (kernel - lastKernel) + (user - lastUser), id = idle - lastIdle;
                double cpu = sys > 0 ? 1.0 - (double)id / sys : 0;
                lastIdle = idle; lastKernel = kernel; lastUser = user;
                cpuBar.Value = cpu; cpuBar.Invalidate();
                cpuLbl.Text = string.Format("CPU  {0:0}%", cpu * 100);
            }

            try
            {
                DriveInfo c = new DriveInfo("C");
                diskLbl.Text = string.Format("Disco C:  {0:0} GB livres de {1:0} GB", c.AvailableFreeSpace / 1073741824.0, c.TotalSize / 1073741824.0);
            }
            catch { }

            if (cAuto.Checked && !busy && m.dwMemoryLoad >= 85 && (DateTime.Now - lastAuto).TotalMinutes >= 3)
            {
                lastAuto = DateTime.Now;
                RunOptimize(true);
            }
        }

        async void RunOptimize(bool auto)
        {
            if (busy) return;
            busy = true;
            go.Enabled = false; go.Text = "Otimizando...";
            bool temp = !auto && cTemp.Checked, dns = !auto && cDns.Checked, bin = !auto && cBin.Checked;
            double ram = 0; long bytes = 0; bool purged = false;
            await Task.Run(delegate
            {
                ram = Engine.TrimRam();
                if (Engine.IsAdmin())
                {
                    purged = Engine.PurgeStandby();
                }
                if (temp) bytes = Engine.CleanTemp();
                if (dns) Engine.FlushDns();
                if (bin) Engine.EmptyRecycleBin();
            });
            string msg = string.Format("{0}Liberou {1:0} MB de RAM", auto ? "Automático: " : "✔ ", Math.Max(0, ram));
            if (purged) msg += " e limpou o cache de memória";
            if (temp) msg += string.Format(" · apagou {0:0} MB de temporários", bytes / 1048576.0);
            if (bin) msg += " · lixeira vazia";
            result.Text = msg + ".";
            result.ForeColor = Color.FromArgb(76, 201, 140);
            Engine.Log(msg);
            if (auto && tray.Visible) tray.ShowBalloonTip(2000, "Otimizador", msg, ToolTipIcon.Info);
            go.Text = "Otimizar agora"; go.Enabled = true;
            busy = false;
            UpdateStats();
            RefreshList();
        }

        async void RefreshList()
        {
            List<Engine.Consumidor> top = await Task.Run(() => Engine.TopConsumers(12));
            list.BeginUpdate();
            list.Items.Clear();
            foreach (Engine.Consumidor c in top)
            {
                ListViewItem it = new ListViewItem(c.Nome);
                it.SubItems.Add(string.Format("{0:N0} MB", c.Bytes / 1048576.0));
                it.SubItems.Add(c.Qtd.ToString());
                list.Items.Add(it);
            }
            list.EndUpdate();
        }

        void KillSelected()
        {
            if (list.SelectedItems.Count == 0) { MessageBox.Show("Escolha um programa na lista primeiro.", "Otimizador"); return; }
            string nome = list.SelectedItems[0].Text;
            if (MessageBox.Show("Fechar \"" + nome + "\"?\n\nO que não estiver salvo nele será perdido.", "Otimizador",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int n = Engine.Kill(nome);
            Engine.Log("Fechou " + nome + " (" + n + " processos)");
            result.Text = n > 0 ? "Fechou " + nome + "." : "Não deu para fechar " + nome + " (tente o modo forte).";
            RefreshList();
        }

        void RelaunchAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch (System.ComponentModel.Win32Exception) { } // a pessoa clicou em "Não" no aviso do Windows
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
