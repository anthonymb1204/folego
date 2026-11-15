// Limpeza funda: mede antes (prévia) e só apaga o que a pessoa escolher. Só caches e restos:
// senhas, histórico, cookies e logins ficam. O cache de shaders da placa de vídeo fica de fora de
// propósito (apagar faz o jogo engasgar na próxima vez, enquanto recompila).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Folego
{
    class Categoria
    {
        public string Id, Nome, Desc, Processo; // Processo: se estiver aberto, parte dos arquivos fica presa
        public bool Admin;
        public Func<List<string>> Pastas;      // pastas cujo conteúdo é apagado
        public Func<List<string>> Arquivos;    // arquivos soltos
        public int DiasMin;                    // só arquivos mais velhos que isso (0 = todos)
        public long Bytes = -1;                // -1 = ainda medindo
        public bool Marcada = true;
        public bool EstaAberto;                // medido junto com o tamanho
    }

    static class Limpeza
    {
        static string Env(string s) { return Environment.ExpandEnvironmentVariables(s); }

        // Pastas de cache de navegador/Electron (Cache, Code Cache, GPUCache) dentro de uma base, até 3 níveis.
        static List<string> Caches(string baseDir)
        {
            List<string> r = new List<string>();
            Achar(baseDir, 0, r);
            return r;
        }

        // Onde uma pasta "Cache" pode ser parte do programa (extensões, módulos): não entra.
        static readonly HashSet<string> NaoEntrar = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Service Worker", "Extensions", "modules", "IndexedDB", "Local Storage", "Session Storage", "databases", "File System" };

        static void Achar(string dir, int nivel, List<string> r)
        {
            if (nivel > 3 || !Directory.Exists(dir)) return;
            try
            {
                foreach (string d in Directory.GetDirectories(dir))
                {
                    string n = Path.GetFileName(d);
                    if (n.Equals("Cache", StringComparison.OrdinalIgnoreCase) || n.Equals("Code Cache", StringComparison.OrdinalIgnoreCase)
                        || n.Equals("GPUCache", StringComparison.OrdinalIgnoreCase)) r.Add(d);
                    else if (!NaoEntrar.Contains(n)) Achar(d, nivel + 1, r);
                }
            }
            catch { }
        }

        static List<string> L(params string[] p) { return p.Select(Env).ToList(); }

        public static List<Categoria> Categorias()
        {
            return new List<Categoria> {
                new Categoria { Id = "temp", Nome = "Temporários", Desc = "Arquivos temporários do Windows e dos programas (com mais de 1 dia)",
                    Pastas = () => Engine.IsAdmin() ? L(@"%TEMP%", @"%SystemRoot%\Temp") : L(@"%TEMP%"), DiasMin = 1 },
                new Categoria { Id = "chrome", Nome = "Cache do Chrome", Desc = "Páginas e imagens guardadas (senhas, histórico e logins ficam)", Processo = "chrome",
                    Pastas = () => Caches(Env(@"%LOCALAPPDATA%\Google\Chrome\User Data")) },
                new Categoria { Id = "edge", Nome = "Cache do Edge", Desc = "Páginas e imagens guardadas pelo Microsoft Edge", Processo = "msedge",
                    Pastas = () => Caches(Env(@"%LOCALAPPDATA%\Microsoft\Edge\User Data")) },
                new Categoria { Id = "discord", Nome = "Cache do Discord", Desc = "Imagens, emojis e vídeos já vistos (o Discord baixa de novo se precisar)", Processo = "Discord",
                    Pastas = () => Caches(Env(@"%APPDATA%\discord")) },
                new Categoria { Id = "steam", Nome = "Cache da loja do Steam", Desc = "Páginas da loja e da comunidade guardadas (jogos não são mexidos)", Processo = "steamwebhelper",
                    Pastas = () => Caches(Env(@"%LOCALAPPDATA%\Steam\htmlcache")) },
                new Categoria { Id = "wu", Nome = "Downloads do Windows Update", Desc = "Instaladores de atualizações que já foram instaladas", Admin = true,
                    Pastas = () => L(@"%SystemRoot%\SoftwareDistribution\Download"), DiasMin = 1 },
                new Categoria { Id = "erros", Nome = "Relatórios de erro", Desc = "Despejos de travamento e relatórios de erro antigos",
                    Pastas = () => Engine.IsAdmin() ? L(@"%LOCALAPPDATA%\CrashDumps", @"%LOCALAPPDATA%\Microsoft\Windows\WER", @"%ProgramData%\Microsoft\Windows\WER\ReportArchive",
                                                        @"%ProgramData%\Microsoft\Windows\WER\ReportQueue", @"%SystemRoot%\Minidump")
                                                    : L(@"%LOCALAPPDATA%\CrashDumps", @"%LOCALAPPDATA%\Microsoft\Windows\WER"),
                    Arquivos = () => Engine.IsAdmin() ? L(@"%SystemRoot%\MEMORY.DMP") : new List<string>() },
                new Categoria { Id = "roblox", Nome = "Logs do Roblox", Desc = "Registros antigos que o Roblox vai acumulando", Processo = null,
                    Pastas = () => L(@"%LOCALAPPDATA%\Roblox\logs"), DiasMin = 1 },
            };
        }

        public static bool Aberto(Categoria c)
        {
            if (c.Processo == null) return false;
            Process[] ps = Process.GetProcessesByName(c.Processo);
            foreach (Process p in ps) p.Dispose();
            return ps.Length > 0;
        }

        public static long Medir(Categoria c) { return Percorrer(c, false); }
        public static long Limpar(Categoria c) { return Percorrer(c, true); }

        static long Percorrer(Categoria c, bool apagar)
        {
            if (c.Admin && !Engine.IsAdmin()) return 0;
            DateTime limite = c.DiasMin > 0 ? DateTime.Now.AddDays(-c.DiasMin) : DateTime.MaxValue;
            long total = 0;
            c.EstaAberto = Aberto(c);
            foreach (string d in c.Pastas == null ? new List<string>() : c.Pastas()) total += Pasta(d, limite, apagar, false);
            if (c.Arquivos != null)
                foreach (string f in c.Arquivos())
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        if (!fi.Exists || fi.LastWriteTime > limite) continue;
                        long len = fi.Length;
                        if (apagar) fi.Delete();
                        total += len;
                    }
                    catch { }
            return total;
        }

        static long Pasta(string dir, DateTime limite, bool apagar, bool removerSe)
        {
            long total = 0;
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) return 0; // não segue atalhos de pasta
                foreach (FileInfo f in di.GetFiles())
                    try
                    {
                        if (f.LastWriteTime > limite) continue;
                        long len = f.Length;
                        if (apagar)
                        {
                            if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                            f.Delete();
                        }
                        total += len;
                    }
                    catch { } // em uso: fica
                foreach (DirectoryInfo s in di.GetDirectories()) total += Pasta(s.FullName, limite, apagar, true);
                if (apagar && removerSe) try { if (di.GetFileSystemInfos().Length == 0) di.Delete(); } catch { }
            }
            catch { }
            return total;
        }
    }
}
