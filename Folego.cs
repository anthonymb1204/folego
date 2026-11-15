// Fôlego: motor (memória, limpeza, inicialização, processos). A tela fica em Ui.cs.
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
[assembly: System.Reflection.AssemblyVersion("0.12.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.12.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.12.0")]

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
        public static int I(string k, int def) { string v; int n; return D.TryGetValue(k, out v) && int.TryParse(v, out n) ? n : def; }
        public static void SetI(string k, int v) { D[k] = v.ToString(); Save(); }
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

        // Fecha o Edge que está rodando escondido; se tiver alguma janela do Edge aberta, não mexe (perderia as abas).
        public static void KillEdgeBackground()
        {
            Process[] ps = Process.GetProcessesByName("msedge");
            try
            {
                if (ps.Any(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return true; } })) return;
                foreach (Process p in ps) try { p.Kill(); } catch { }
            }
            finally { foreach (Process p in ps) p.Dispose(); }
        }

        // O jogo só conta como aberto se tiver janela visível: o Roblox, por exemplo, fica rodando escondido
        // no cantinho da barra de tarefas desde que o Windows liga (--launch-to-tray), sem jogo nenhum.
        public static bool IsRunning(string nome)
        {
            bool aberto = false;
            foreach (Process p in Process.GetProcessesByName(nome))
            {
                try { if (p.MainWindowHandle != IntPtr.Zero) aberto = true; }
                catch { }
                finally { p.Dispose(); }
            }
            return aberto;
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
            try
            {
                string app = StoreApp(path);
                Process.Start("explorer.exe", app != null ? "shell:AppsFolder\\" + app : "\"" + path + "\"");
                return true;
            }
            catch { return false; }
        }

        // Apps da Microsoft Store (pasta WindowsApps, ex.: WhatsApp) não abrem pelo caminho do .exe: precisam do
        // identificador "Família!App". Pasta: Nome_Versão_Arquitetura__IdDoPublicador; o App vem do AppxManifest.xml.
        static string StoreApp(string path)
        {
            try
            {
                int i = path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                string resto = path.Substring(i + 13);
                string pasta = resto.Substring(0, resto.IndexOf('\\'));
                string[] partes = pasta.Split('_');
                if (partes.Length < 5) return null;
                string familia = partes[0] + "_" + partes[partes.Length - 1];
                string manifesto = Path.Combine(path.Substring(0, i + 13) + pasta, "AppxManifest.xml");
                string exe = Path.GetFileName(path);
                System.Xml.XmlDocument x = new System.Xml.XmlDocument();
                x.Load(manifesto);
                foreach (System.Xml.XmlNode n in x.GetElementsByTagName("Application"))
                {
                    System.Xml.XmlAttribute id = n.Attributes["Id"], ex = n.Attributes["Executable"];
                    if (id != null && (ex == null || ex.Value.EndsWith(exe, StringComparison.OrdinalIgnoreCase))) return familia + "!" + id.Value;
                }
                return familia + "!App";
            }
            catch { return null; }
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

        public static string LogPath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "folego_log.txt"); }
        }

        public static void Log(string linha)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + linha + Environment.NewLine);
            }
            catch { }
        }
    }

    static class Program
    {
        // Folego.exe                 abre a janela
        // Folego.exe --bandeja       abre direto no cantinho da barra de tarefas, sem janela (sem roubar o foco)
        // Folego.exe --foto x.png N  salva a página N (0 a 3) num arquivo, sem janela e sem mexer em nada (testes)
        [STAThread]
        static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogErro(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogErro(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) => { LogErro(e.Exception); e.SetObserved(); };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int fi = Array.FindIndex(args, a => a == "--foto");
            if (fi >= 0 && fi + 1 < args.Length)
            {
                int pagina = fi + 2 < args.Length ? int.Parse(args[fi + 2]) : 0;
                using (MainForm f = new MainForm(MainForm.Modo.Foto))
                {
                    f.CreateControl();
                    f.SaveFoto(args[fi + 1], pagina);
                }
                return;
            }

            bool bandeja = args.Any(a => a == "--bandeja" || a == "--tray");
            bool reaberto = args.Any(a => a == "--reaberto");
            bool unico;
            using (System.Threading.Mutex m = new System.Threading.Mutex(true, "Folego_App_Unico", out unico))
            {
                if (!unico && reaberto) // reaberto como admin: espera a cópia antiga fechar
                {
                    try { unico = m.WaitOne(10000); } catch (System.Threading.AbandonedMutexException) { unico = true; }
                }
                if (!unico)
                {
                    // Já está aberto: pede para a cópia aberta aparecer (só se a pessoa abriu a janela).
                    if (!bandeja)
                        try { System.Threading.EventWaitHandle.OpenExisting("Folego_Mostrar").Set(); } catch { }
                    return;
                }
                Application.Run(new MainForm(bandeja ? MainForm.Modo.Bandeja : MainForm.Modo.Janela));
            }
        }

        // Erros nunca viram janela (atrapalharia o jogo): vão para o registro.
        static void LogErro(Exception ex)
        {
            Engine.Log("Erro: " + (ex == null ? "desconhecido" : ex.ToString().Replace("\r", "").Replace("\n", " | ")));
        }
    }
}
