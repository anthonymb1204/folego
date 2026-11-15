// Fôlego: um botão que libera RAM e limpa lixo do PC sem fechar o jogo aberto.
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
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Fôlego")]
[assembly: System.Reflection.AssemblyProduct("Fôlego")]
[assembly: System.Reflection.AssemblyVersion("0.2.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.2.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.2.2")]

namespace Folego
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
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
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

        public static MEMORYSTATUSEX Mem()
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(m);
            return m;
        }
    }

    // Preferências, guardadas em %APPDATA%\Folego\config.txt (uma "chave=valor" por linha).
    static class Config
    {
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Folego", "config.txt");
        static readonly Dictionary<string, string> D = new Dictionary<string, string>();

        public static void Load()
        {
            try
            {
                // Veio do tempo em que o app se chamava Otimizador: traz as preferências junto.
                string antigo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Otimizador", "config.txt");
                if (!File.Exists(FilePath) && File.Exists(antigo))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.Copy(antigo, FilePath);
                }
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) D[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, D.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            catch { }
        }

        public static bool B(string k, bool def) { string v; return D.TryGetValue(k, out v) ? v == "1" : def; }
        public static void SetB(string k, bool v) { D[k] = v ? "1" : "0"; Save(); }
        public static List<string> L(string k, string def)
        {
            string v;
            if (!D.TryGetValue(k, out v)) v = def;
            return v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s != "").ToList();
        }
        public static void SetL(string k, List<string> v) { D[k] = string.Join(";", v); Save(); }
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
            "NisSrv", "atiesrxx", "atieclxx", "amdfendrsr", "RadeonSoftware", "AMDRSServ", "Folego", "Otimizador"
        };
        // Jogos do Modo Jogo: protegidos como o Roblox.
        public static HashSet<string> Jogos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            return p.Id == self || p.Id == fg || p.Id <= 4 || Protegidos.Contains(p.ProcessName) || Jogos.Contains(p.ProcessName)
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

        // Programas com janela aberta agora (para escolher jogos e o que fechar no Modo Jogo).
        public static List<string> WindowedApps()
        {
            int self = Process.GetCurrentProcess().Id, sessao = Process.GetCurrentProcess().SessionId;
            HashSet<string> nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id != self && p.SessionId == sessao && p.MainWindowHandle != IntPtr.Zero
                        && !NuncaFechar.Contains(p.ProcessName) && !p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                        nomes.Add(p.ProcessName);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return nomes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static bool IsRunning(string nome)
        {
            Process[] ps = Process.GetProcessesByName(nome);
            foreach (Process p in ps) p.Dispose();
            return ps.Length > 0;
        }

        public static string PathOf(string nome)
        {
            foreach (Process p in Process.GetProcessesByName(nome))
            {
                try { return p.MainModule.FileName; }
                catch { }
                finally { p.Dispose(); }
            }
            return null;
        }

        public static int SetHighPriority(string nome)
        {
            int n = 0;
            foreach (Process p in Process.GetProcessesByName(nome))
            {
                try { p.PriorityClass = ProcessPriorityClass.High; n++; }
                catch { }
                finally { p.Dispose(); }
            }
            return n;
        }

        public static int Kill(string nome)
        {
            if (Protegidos.Contains(nome) || NuncaFechar.Contains(nome) || Jogos.Contains(nome)) return 0;
            int n = 0;
            foreach (Process p in Process.GetProcessesByName(nome))
            {
                try { p.Kill(); n++; } catch { }
                finally { p.Dispose(); }
            }
            return n;
        }

        // Abre um programa como usuário normal (via Explorer), mesmo com o Fôlego em modo admin.
        public static bool OpenAsUser(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); return true; }
            catch { return false; }
        }

        // ---- Programas que abrem com o Windows -------------------------------------------------------
        // Liga/desliga do mesmo jeito que o Gerenciador de Tarefas (chaves StartupApproved): nada é
        // apagado, e o que for desligado aqui aparece desligado lá também (e vice-versa).
        public class StartupItem
        {
            public string Nome, Comando, ValueName, ApprovedKey;
            public bool Ligado, Todos;
            public RegistryKey Root;
        }

        const string AP = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupItem> StartupItems()
        {
            List<StartupItem> r = new List<StartupItem>();
            AddRun(r, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", false);
            AddRun(r, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", true);
            AddRun(r, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", true);
            AddFolder(r, Environment.GetFolderPath(Environment.SpecialFolder.Startup), false);
            AddFolder(r, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), true);
            return r.OrderBy(i => !i.Ligado).ThenBy(i => i.Nome, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void AddRun(List<StartupItem> r, RegistryKey root, string path, string ap, bool todos)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (string v in k.GetValueNames())
                    {
                        if (v == "") continue;
                        StartupItem i = new StartupItem { Nome = v, Comando = Convert.ToString(k.GetValue(v)), Todos = todos,
                            Root = root, ApprovedKey = AP + ap, ValueName = v };
                        i.Ligado = IsApproved(i);
                        r.Add(i);
                    }
                }
            }
            catch { }
        }

        static void AddFolder(List<StartupItem> r, string dir, bool todos)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string n = Path.GetFileName(f);
                    if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    StartupItem i = new StartupItem { Nome = Path.GetFileNameWithoutExtension(f), Comando = f, Todos = todos,
                        Root = todos ? Registry.LocalMachine : Registry.CurrentUser, ApprovedKey = AP + "StartupFolder", ValueName = n };
                    i.Ligado = IsApproved(i);
                    r.Add(i);
                }
            }
            catch { }
        }

        static bool IsApproved(StartupItem i)
        {
            try
            {
                using (RegistryKey k = i.Root.OpenSubKey(i.ApprovedKey))
                {
                    byte[] b = k == null ? null : k.GetValue(i.ValueName) as byte[];
                    return b == null || b.Length == 0 || b[0] % 2 == 0; // 02/06 = ligado, 03/07 = desligado
                }
            }
            catch { return true; }
        }

        // Lança exceção se não tiver permissão (itens "de todos os usuários" precisam do modo admin).
        public static void SetStartup(StartupItem i, bool on)
        {
            byte[] b = new byte[12];
            b[0] = (byte)(on ? 2 : 3);
            if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(b, 4);
            using (RegistryKey k = i.Root.CreateSubKey(i.ApprovedKey))
                k.SetValue(i.ValueName, b, RegistryValueKind.Binary);
            i.Ligado = on;
        }

        public static void Log(string linha)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "folego_log.txt"),
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
        const string Versao = "0.2.2";
        static readonly Color Bg = Color.FromArgb(24, 25, 30), Card = Color.FromArgb(34, 36, 43),
            Fg = Color.FromArgb(235, 236, 240), Muted = Color.FromArgb(150, 154, 166), Accent = Color.FromArgb(88, 101, 242),
            Good = Color.FromArgb(76, 201, 140);

        Panel[] pages;
        Button[] tabs;

        // Otimizar
        Label ramLbl, cpuLbl, diskLbl, result;
        Meter ramBar, cpuBar;
        Button go;
        CheckBox cTemp, cDns, cBin, cAuto;
        ListView list;

        // Inicialização
        ListView startList;
        Label startLbl;
        bool startLoaded;

        // Modo Jogo
        CheckBox cGame, cGRam, cGPrio, cGReopen;
        Label gameLbl;
        List<string> jogos, fechar;
        string currentGame;
        readonly List<string> reabrir = new List<string>();
        bool gameBusy;

        Timer tick;
        int tickN;
        NotifyIcon tray;
        long lastIdle, lastKernel, lastUser;
        DateTime lastAuto = DateTime.MinValue;
        bool busy;

        public MainForm()
        {
            Config.Load();
            jogos = Config.L("jogos", "RobloxPlayerBeta");
            fechar = Config.L("fechar", "");
            SyncJogos();

            Text = "Fôlego " + Versao + (Engine.IsAdmin() ? " (administrador)" : "");
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Bg; ForeColor = Fg;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 690);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            PictureBox logo = new PictureBox { Left = 20, Top = 16, Width = 32, Height = 32, SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent };
            try { using (Icon big = new Icon(Icon, 64, 64)) logo.Image = big.ToBitmap(); } catch { }
            Controls.Add(logo);
            L("Fôlego", 58, 14, 16f, FontStyle.Bold, Fg, this);
            L("Libera RAM e limpa lixo sem fechar seu jogo.", 22, 50, 9f, FontStyle.Regular, Muted, this);

            string[] nomes = { "Otimizar", "Inicialização", "Modo Jogo" };
            pages = new Panel[nomes.Length];
            tabs = new Button[nomes.Length];
            for (int i = 0; i < nomes.Length; i++)
            {
                int idx = i;
                tabs[i] = SmallButton(nomes[i], 20 + i * 148, 80, 144, this);
                tabs[i].Height = 32;
                tabs[i].Click += delegate { ShowPage(idx); };
                pages[i] = new Panel { Left = 0, Top = 124, Width = 480, Height = 566, BackColor = Bg };
                Controls.Add(pages[i]);
            }
            BuildOtimizar(pages[0]);
            BuildInicializacao(pages[1]);
            BuildModoJogo(pages[2]);
            ShowPage(0);

            tray = new NotifyIcon { Icon = Icon, Text = "Fôlego", Visible = false };
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; tray.Visible = false; };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && (cAuto.Checked || cGame.Checked))
                {
                    Hide(); tray.Visible = true;
                    tray.ShowBalloonTip(2000, "Fôlego", "Continua cuidando do PC aqui embaixo.", ToolTipIcon.Info);
                }
            };
            FormClosed += delegate { tray.Visible = false; };

            Native.GetSystemTimes(out lastIdle, out lastKernel, out lastUser);
            tick = new Timer { Interval = 1000 };
            tick.Tick += delegate
            {
                UpdateStats();
                if (++tickN % 2 == 0) CheckGame();
            };
            tick.Start();
            UpdateStats();
            Shown += delegate { RefreshList(); CheckGame(); };
        }

        // ================= Páginas =================

        void ShowPage(int i)
        {
            for (int j = 0; j < pages.Length; j++)
            {
                pages[j].Visible = j == i;
                tabs[j].BackColor = j == i ? Accent : Card;
                tabs[j].ForeColor = j == i ? Color.White : Fg;
            }
            if (i == 1 && !startLoaded) LoadStartup();
        }

        void BuildOtimizar(Panel pg)
        {
            Panel stats = new Panel { Left = 20, Top = 0, Width = 440, Height = 128, BackColor = Card };
            pg.Controls.Add(stats);
            ramLbl = L("RAM", 14, 10, 9.5f, FontStyle.Regular, Fg, stats);
            ramBar = new Meter { Left = 14, Top = 34, Width = 412 }; stats.Controls.Add(ramBar);
            cpuLbl = L("CPU", 14, 52, 9.5f, FontStyle.Regular, Fg, stats);
            cpuBar = new Meter { Left = 14, Top = 76, Width = 412 }; stats.Controls.Add(cpuBar);
            diskLbl = L("Disco C:", 14, 96, 9.5f, FontStyle.Regular, Muted, stats);

            go = new Button { Text = "Otimizar agora", Left = 20, Top = 142, Width = 440, Height = 56,
                FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold), Cursor = Cursors.Hand };
            go.FlatAppearance.BorderSize = 0;
            go.Click += delegate { RunOptimize(false); };
            pg.Controls.Add(go);

            result = L("Protege o Roblox, os jogos do Modo Jogo, o app da frente e o Windows.", 22, 206, 9.5f, FontStyle.Regular, Muted, pg);
            result.AutoSize = false; result.Width = 440; result.Height = 40;

            cTemp = Chk("Limpar arquivos temporários", 22, 248, "temp", true, pg);
            cDns = Chk("Limpar cache de DNS (internet)", 250, 248, "dns", true, pg);
            cBin = Chk("Esvaziar a lixeira", 22, 274, "lixeira", false, pg);
            cAuto = Chk("Automático: RAM acima de 85%", 250, 274, "auto", false, pg);

            L("Programas que mais gastam RAM", 20, 312, 10.5f, FontStyle.Bold, Fg, pg);
            list = new ListView { Left = 20, Top = 338, Width = 440, Height = 150, View = View.Details,
                FullRowSelect = true, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Card, ForeColor = Fg, BorderStyle = BorderStyle.None };
            list.Columns.Add("Programa", 250); list.Columns.Add("RAM", 100, HorizontalAlignment.Right); list.Columns.Add("Abertos", 70, HorizontalAlignment.Right);
            Dark(list);
            pg.Controls.Add(list);

            Button killBtn = SmallButton("Fechar selecionado", 20, 496, 150, pg);
            killBtn.Click += delegate { KillSelected(); };
            Button refresh = SmallButton("Atualizar lista", 178, 496, 120, pg);
            refresh.Click += delegate { RefreshList(); };

            Label adminLbl = L(Engine.IsAdmin() ? "Modo forte ligado: limpa também o cache de memória e o Temp do Windows."
                                                : "Modo forte limpa mais (cache de memória e Temp do Windows).",
                20, 534, 8.5f, FontStyle.Regular, Muted, pg);
            adminLbl.AutoSize = false; adminLbl.Width = Engine.IsAdmin() ? 440 : 300;
            if (!Engine.IsAdmin())
            {
                Button adminBtn = SmallButton("Modo forte (admin)", 330, 496, 130, pg);
                adminBtn.Click += delegate { RelaunchAdmin(); };
            }
        }

        void BuildInicializacao(Panel pg)
        {
            Label info = L("Programas que abrem junto com o Windows. Desligar não desinstala nada: dá para religar "
                + "quando quiser (o Gerenciador de Tarefas mostra a mesma coisa).", 20, 0, 9f, FontStyle.Regular, Muted, pg);
            info.AutoSize = false; info.Width = 440; info.Height = 40;

            startList = new ListView { Left = 20, Top = 46, Width = 440, Height = 420, View = View.Details,
                FullRowSelect = true, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Card, ForeColor = Fg, BorderStyle = BorderStyle.None, ShowItemToolTips = true };
            startList.Columns.Add("Programa", 240); startList.Columns.Add("Status", 90); startList.Columns.Add("Para", 90);
            Dark(startList);
            startList.DoubleClick += delegate { ToggleStartup(); };
            pg.Controls.Add(startList);

            Button toggle = SmallButton("Ligar / desligar", 20, 476, 150, pg);
            toggle.Click += delegate { ToggleStartup(); };
            Button refresh = SmallButton("Atualizar", 178, 476, 100, pg);
            refresh.Click += delegate { LoadStartup(); };

            startLbl = L("", 20, 516, 9f, FontStyle.Regular, Muted, pg);
            startLbl.AutoSize = false; startLbl.Width = 440; startLbl.Height = 40;
        }

        void BuildModoJogo(Panel pg)
        {
            cGame = Chk("Ligar o Modo Jogo", 20, 0, "jogo", true, pg);
            cGame.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            cGame.CheckedChanged += delegate { if (!cGame.Checked && currentGame != null) EndGame(); };
            Label info = L("Quando um jogo da lista abre, o Fôlego prepara o PC sozinho. Ele precisa estar "
                + "aberto (pode ficar minimizado, no cantinho da barra de tarefas).", 20, 28, 9f, FontStyle.Regular, Muted, pg);
            info.AutoSize = false; info.Width = 440; info.Height = 38;

            ListEditor(pg, "Jogos", 74, jogos, "jogos", "Adicionar jogo");
            ListEditor(pg, "Fechar quando o jogo abrir", 218, fechar, "fechar", "Adicionar");

            cGRam = Chk("Liberar RAM (e o cache de memória, no modo forte)", 20, 362, "jogo_ram", true, pg);
            cGPrio = Chk("Prioridade alta para o jogo", 20, 388, "jogo_prio", true, pg);
            cGReopen = Chk("Reabrir os programas fechados quando o jogo acabar", 20, 414, "jogo_reabrir", true, pg);

            gameLbl = L("Esperando um jogo abrir...", 20, 452, 9.5f, FontStyle.Regular, Muted, pg);
            gameLbl.AutoSize = false; gameLbl.Width = 440; gameLbl.Height = 60;
        }

        // Lista editável (jogos / programas para fechar) com caixa de escolha dos apps abertos.
        void ListEditor(Panel pg, string title, int y, List<string> data, string cfgKey, string addText)
        {
            L(title, 20, y, 10.5f, FontStyle.Bold, Fg, pg);
            ListBox lb = new ListBox { Left = 20, Top = y + 26, Width = 300, Height = 74, BackColor = Card, ForeColor = Fg,
                BorderStyle = BorderStyle.None, IntegralHeight = false };
            foreach (string s in data) lb.Items.Add(s);
            pg.Controls.Add(lb);
            Button rem = SmallButton("Remover", 330, y + 26, 130, pg);

            ComboBox cb = new ComboBox { Left = 20, Top = y + 106, Width = 300, DropDownStyle = ComboBoxStyle.DropDown,
                BackColor = Card, ForeColor = Fg, FlatStyle = FlatStyle.Flat };
            cb.DropDown += delegate
            {
                cb.Items.Clear();
                foreach (string n in Engine.WindowedApps()) cb.Items.Add(n);
            };
            pg.Controls.Add(cb);
            Button add = SmallButton(addText, 330, y + 104, 130, pg);

            add.Click += delegate
            {
                string n = cb.Text.Trim();
                if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
                if (n == "" || data.Contains(n, StringComparer.OrdinalIgnoreCase)) return;
                data.Add(n); lb.Items.Add(n); cb.Text = "";
                Config.SetL(cfgKey, data); SyncJogos();
            };
            rem.Click += delegate
            {
                string sel = lb.SelectedItem as string;
                if (sel == null) return;
                data.RemoveAll(x => x.Equals(sel, StringComparison.OrdinalIgnoreCase));
                lb.Items.Remove(sel);
                Config.SetL(cfgKey, data); SyncJogos();
            };
        }

        void SyncJogos() { Engine.Jogos = new HashSet<string>(jogos, StringComparer.OrdinalIgnoreCase); }

        // ================= Componentes =================

        Label L(string t, int x, int y, float size, FontStyle st, Color c, Control parent)
        {
            Label l = new Label { Text = t, Left = x, Top = y, AutoSize = true, ForeColor = c, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", size, st) };
            parent.Controls.Add(l);
            return l;
        }

        CheckBox Chk(string t, int x, int y, string cfgKey, bool def, Control parent)
        {
            CheckBox c = new CheckBox { Text = t, Left = x, Top = y, AutoSize = true, Checked = Config.B(cfgKey, def), ForeColor = Fg };
            c.CheckedChanged += delegate { Config.SetB(cfgKey, c.Checked); Engine.Log(t + ": " + (c.Checked ? "ligado" : "desligado")); };
            parent.Controls.Add(c);
            return c;
        }

        Button SmallButton(string t, int x, int y, int w, Control parent)
        {
            Button b = new Button { Text = t, Left = x, Top = y, Width = w, Height = 30, FlatStyle = FlatStyle.Flat,
                BackColor = Card, ForeColor = Fg, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(60, 63, 74);
            parent.Controls.Add(b);
            return b;
        }

        // Cabeçalho escuro nas listas (o padrão do Windows é branco).
        static void Dark(ListView lv)
        {
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += (s, e) =>
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(44, 46, 54))) e.Graphics.FillRectangle(b, e.Bounds);
                TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, Rectangle.Inflate(e.Bounds, -6, 0), Muted, f);
            };
            lv.DrawItem += (s, e) => { e.DrawDefault = true; };
            lv.DrawSubItem += (s, e) => { e.DrawDefault = true; };
            // A última coluna ocupa o resto da largura (senão sobra um pedaço branco no cabeçalho).
            EventHandler fill = delegate { FitLast(lv); };
            lv.ClientSizeChanged += fill;
            lv.HandleCreated += fill;
        }

        // Estica a última coluna até a borda, descontando a barra de rolagem vertical quando ela aparece
        // (sem isso sobra um pedaço branco no cabeçalho ou surge uma barra de rolagem horizontal).
        static void FitLast(ListView lv)
        {
            if (lv.Columns.Count == 0 || !lv.IsHandleCreated) return;
            lv.BeginInvoke((MethodInvoker)delegate
            {
                bool vs = (Native.GetWindowLong(lv.Handle, -16) & 0x00200000) != 0; // WS_VSCROLL
                int w = lv.Width - (vs ? SystemInformation.VerticalScrollBarWidth : 0) - 1;
                for (int c = 0; c < lv.Columns.Count - 1; c++) w -= lv.Columns[c].Width;
                if (w > 30) lv.Columns[lv.Columns.Count - 1].Width = w;
            });
        }

        void Notify(string msg)
        {
            result.Text = msg;
            result.ForeColor = Good;
            Engine.Log(msg);
            if (tray.Visible) tray.ShowBalloonTip(2500, "Fôlego", msg, ToolTipIcon.Info);
        }

        // ================= Otimizar =================

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
                purged = Engine.PurgeStandby();
                if (temp) bytes = Engine.CleanTemp();
                if (dns) Engine.FlushDns();
                if (bin) Engine.EmptyRecycleBin();
            });
            string msg = string.Format("{0}Liberou {1:0} MB de RAM", auto ? "Automático: " : "✔ ", Math.Max(0, ram));
            if (purged) msg += " e limpou o cache de memória";
            if (temp) msg += string.Format(" · apagou {0:0} MB de temporários", bytes / 1048576.0);
            if (bin) msg += " · lixeira vazia";
            Notify(msg + ".");
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
            FitLast(list);
        }

        void KillSelected()
        {
            if (list.SelectedItems.Count == 0) { MessageBox.Show("Escolha um programa na lista primeiro.", "Fôlego"); return; }
            string nome = list.SelectedItems[0].Text;
            if (MessageBox.Show("Fechar \"" + nome + "\"?\n\nO que não estiver salvo nele será perdido.", "Fôlego",
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

        // ================= Inicialização =================

        void LoadStartup()
        {
            startLoaded = true;
            List<Engine.StartupItem> items = Engine.StartupItems();
            startList.BeginUpdate();
            startList.Items.Clear();
            foreach (Engine.StartupItem i in items)
            {
                ListViewItem it = new ListViewItem(i.Nome) { Tag = i, ToolTipText = i.Comando };
                it.SubItems.Add("");
                it.SubItems.Add(i.Todos ? "Todos" : "Só você");
                PaintStartup(it);
                startList.Items.Add(it);
            }
            startList.EndUpdate();
            FitLast(startList);
            UpdateStartupLbl();
        }

        void PaintStartup(ListViewItem it)
        {
            Engine.StartupItem i = (Engine.StartupItem)it.Tag;
            it.SubItems[1].Text = i.Ligado ? "● Ligado" : "Desligado";
            it.ForeColor = i.Ligado ? Fg : Muted;
        }

        void UpdateStartupLbl()
        {
            int on = startList.Items.Cast<ListViewItem>().Count(x => ((Engine.StartupItem)x.Tag).Ligado);
            startLbl.Text = string.Format("{0} de {1} ligados. Desligue o que você não usa logo que liga o PC "
                + "(o que muda vale a partir da próxima vez que o Windows iniciar).", on, startList.Items.Count);
        }

        void ToggleStartup()
        {
            if (startList.SelectedItems.Count == 0) { MessageBox.Show("Escolha um programa na lista primeiro.", "Fôlego"); return; }
            ListViewItem it = startList.SelectedItems[0];
            Engine.StartupItem i = (Engine.StartupItem)it.Tag;
            try
            {
                Engine.SetStartup(i, !i.Ligado);
                Engine.Log("Inicialização: " + i.Nome + (i.Ligado ? " ligado" : " desligado"));
                PaintStartup(it);
                UpdateStartupLbl();
            }
            catch (Exception)
            {
                MessageBox.Show("\"" + i.Nome + "\" abre para todos os usuários do PC: para mudar, use o Modo forte (admin) na aba Otimizar.",
                    "Fôlego", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ================= Modo Jogo =================

        void CheckGame()
        {
            if (gameBusy) return;
            string found = null;
            if (cGame.Checked)
                foreach (string g in jogos)
                    if (Engine.IsRunning(g)) { found = g; break; }
            if (found != null && currentGame == null) StartGame(found);
            else if (found == null && currentGame != null) EndGame();
        }

        async void StartGame(string g)
        {
            currentGame = g;
            gameBusy = true;
            gameLbl.ForeColor = Fg;
            gameLbl.Text = "🎮 " + g + " abriu: preparando o PC...";
            bool ram = cGRam.Checked, prio = cGPrio.Checked;
            List<string> alvo = new List<string>(fechar), fechados = new List<string>(), caminhos = new List<string>();
            double mb = 0; int prioN = 0;
            await Task.Run(delegate
            {
                System.Threading.Thread.Sleep(3000); // deixa o jogo terminar de abrir
                if (prio) prioN = Engine.SetHighPriority(g);
                foreach (string n in alvo)
                {
                    string path = Engine.PathOf(n);
                    if (Engine.Kill(n) > 0) { fechados.Add(n); if (path != null) caminhos.Add(path); }
                }
                if (ram) { mb = Engine.TrimRam(); Engine.PurgeStandby(); }
            });
            reabrir.Clear();
            reabrir.AddRange(caminhos);
            string msg = "🎮 Modo Jogo: " + g;
            if (prioN > 0) msg += " em prioridade alta";
            if (fechados.Count > 0) msg += " · fechou " + string.Join(", ", fechados);
            if (ram) msg += string.Format(" · liberou {0:0} MB", Math.Max(0, mb));
            gameLbl.ForeColor = Good;
            gameLbl.Text = msg;
            Notify(msg);
            gameBusy = false;
        }

        void EndGame()
        {
            string g = currentGame;
            currentGame = null;
            List<string> abertos = new List<string>();
            if (cGReopen.Checked)
                foreach (string path in reabrir)
                    if (Engine.OpenAsUser(path)) abertos.Add(Path.GetFileNameWithoutExtension(path));
            reabrir.Clear();
            string msg = g + " fechou: Modo Jogo encerrado";
            if (abertos.Count > 0) msg += " · reabriu " + string.Join(", ", abertos);
            gameLbl.ForeColor = Muted;
            gameLbl.Text = msg + ". Esperando um jogo abrir...";
            Notify(msg + ".");
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
