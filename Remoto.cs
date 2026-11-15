// Fôlego no celular: um servidorzinho na rede de casa. O celular abre http://<ip do PC>:47474 no
// navegador, pareia com o código de 6 dígitos que aparece no PC e passa a controlar o Fôlego.
// Segurança: só responde a endereços de rede local (casa ou Tailscale), toda ação pede o token do
// pareamento e, depois de 5 códigos errados, o pareamento trava por 5 minutos.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Folego
{
    class Remoto
    {
        public const int Porta = 47474;
        const string Regra = "Folego (celular)";

        readonly System.Windows.Forms.Control ui;   // as ações rodam na thread da tela
        TcpListener listener;
        volatile bool rodando;
        int erros;
        DateTime travadoAte;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public string Codigo { get; private set; }
        public bool Rodando { get { return rodando; } }
        public Func<Dictionary<string, object>> Estado;               // chamado na thread da tela
        public Func<string, Dictionary<string, object>, object> Acao;  // idem
        public Func<string, bool> TokenValido;
        public Func<byte[]> Icone, Fundo;

        public Remoto(System.Windows.Forms.Control ui) { this.ui = ui; }

        public void Iniciar()
        {
            if (rodando) return;
            listener = new TcpListener(IPAddress.Any, Porta);
            listener.Start();
            rodando = true;
            NovoCodigo();
            new Thread(Loop) { IsBackground = true, Name = "Folego-celular" }.Start();
        }

        public void Parar()
        {
            rodando = false;
            try { if (listener != null) listener.Stop(); } catch { }
        }

        public void NovoCodigo()
        {
            byte[] b = new byte[4];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
            Codigo = (100000 + BitConverter.ToUInt32(b, 0) % 900000).ToString();
            erros = 0;
        }

        public static string NovoToken()
        {
            byte[] b = new byte[24];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        // Endereço do PC na rede de casa (o da placa de rede que tem roteador).
        public static string IpLocal()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPInterfaceProperties p = ni.GetIPProperties();
                    if (!p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                    foreach (UnicastIPAddressInformation u in p.UnicastAddresses)
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork && Privado(u.Address)) return u.Address.ToString();
                }
            }
            catch { }
            return null;
        }

        static bool Privado(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127); // 100.64/10: Tailscale
        }

        // Libera o Fôlego no firewall só para redes privadas (precisa de admin). Refaz a regra para o .exe atual.
        public static bool LiberarFirewall()
        {
            Netsh("advfirewall firewall delete rule name=\"" + Regra + "\"");
            return Netsh("advfirewall firewall add rule name=\"" + Regra + "\" dir=in action=allow protocol=TCP localport=" + Porta +
                         " profile=private enable=yes program=\"" + System.Windows.Forms.Application.ExecutablePath + "\"") == 0;
        }

        public static void FecharFirewall() { Netsh("advfirewall firewall delete rule name=\"" + Regra + "\""); }

        static int Netsh(string args)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo("netsh.exe", args)
                       { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                         RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        void Loop()
        {
            while (rodando)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(delegate { Atender(c); });
            }
        }

        void Atender(TcpClient c)
        {
            using (c)
            {
                try
                {
                    c.ReceiveTimeout = 5000; c.SendTimeout = 8000;
                    NetworkStream s = c.GetStream();
                    if (!Privado(((IPEndPoint)c.Client.RemoteEndPoint).Address)) { Responder(s, 403, "text/plain", Encoding.UTF8.GetBytes("Só na rede de casa.")); return; }

                    // Cabeçalho até a linha em branco, depois o corpo (Content-Length).
                    List<byte> buf = new List<byte>();
                    int end = -1;
                    byte[] tmp = new byte[4096];
                    while (end < 0 && buf.Count < 16384)
                    {
                        int n = s.Read(tmp, 0, tmp.Length);
                        if (n <= 0) return;
                        buf.AddRange(tmp.Take(n));
                        for (int i = 3; i < buf.Count; i++)
                            if (buf[i - 3] == 13 && buf[i - 2] == 10 && buf[i - 1] == 13 && buf[i] == 10) { end = i + 1; break; }
                    }
                    if (end < 0) return;
                    string[] linhas = Encoding.UTF8.GetString(buf.Take(end).ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] req = linhas[0].Split(' ');
                    if (req.Length < 2) return;
                    string metodo = req[0], caminho = req[1].Split('?')[0];
                    Dictionary<string, string> h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string l in linhas.Skip(1))
                    {
                        int i = l.IndexOf(':');
                        if (i > 0) h[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                    }
                    int len = 0;
                    string cl;
                    if (h.TryGetValue("Content-Length", out cl)) int.TryParse(cl, out len);
                    len = Math.Min(len, 65536);
                    List<byte> body = buf.Skip(end).ToList();
                    while (body.Count < len)
                    {
                        int n = s.Read(tmp, 0, Math.Min(tmp.Length, len - body.Count));
                        if (n <= 0) break;
                        body.AddRange(tmp.Take(n));
                    }
                    Rotear(s, metodo, caminho, h, Encoding.UTF8.GetString(body.ToArray()));
                }
                catch { }
            }
        }

        void Rotear(Stream s, string metodo, string caminho, Dictionary<string, string> h, string corpo)
        {
            if (metodo == "GET" && (caminho == "/" || caminho == "/index.html"))
            {
                using (Stream r = typeof(Remoto).Assembly.GetManifestResourceStream("celular.html"))
                using (MemoryStream ms = new MemoryStream()) { r.CopyTo(ms); Responder(s, 200, "text/html; charset=utf-8", ms.ToArray()); }
                return;
            }
            if (metodo == "GET" && caminho == "/manifest.json")
            {
                RespJson(s, 200, new Dictionary<string, object> {
                    { "name", "Fôlego" }, { "short_name", "Fôlego" }, { "start_url", "/" }, { "display", "standalone" },
                    { "background_color", "#0e0c18" }, { "theme_color", "#0e0c18" },
                    { "icons", new[] { new Dictionary<string, object> { { "src", "/icone.png" }, { "sizes", "192x192" }, { "type", "image/png" } } } } });
                return;
            }
            if (metodo == "GET" && caminho == "/icone.png") { Responder(s, 200, "image/png", Na(Icone)); return; }
            if (metodo == "GET" && caminho == "/fundo.jpg") { Responder(s, 200, "image/jpeg", Na(Fundo)); return; }

            Dictionary<string, object> dados;
            try { dados = string.IsNullOrEmpty(corpo) ? new Dictionary<string, object>() : Json.Deserialize<Dictionary<string, object>>(corpo); }
            catch { RespJson(s, 400, Erro("pedido inválido")); return; }

            if (metodo == "POST" && caminho == "/api/parear")
            {
                if (DateTime.Now < travadoAte) { RespJson(s, 429, Erro("Muitas tentativas. Espere uns minutos.")); return; }
                object cod;
                if (dados.TryGetValue("codigo", out cod) && Convert.ToString(cod).Replace(" ", "") == Codigo)
                {
                    string token = NovoToken();
                    Na(delegate { return Acao("novo_token", new Dictionary<string, object> { { "token", token } }); });
                    NovoCodigo(); // cada código serve uma vez
                    RespJson(s, 200, new Dictionary<string, object> { { "token", token }, { "pc", Environment.MachineName } });
                }
                else
                {
                    if (++erros >= 5) { travadoAte = DateTime.Now.AddMinutes(5); erros = 0; }
                    RespJson(s, 401, Erro("Código errado."));
                }
                return;
            }

            string tk;
            if (!h.TryGetValue("X-Folego-Token", out tk) || !(bool)Na(delegate { return (object)TokenValido(tk); }))
            { RespJson(s, 401, Erro("Pareie de novo.")); return; }

            if (metodo == "GET" && caminho == "/api/estado") { RespJson(s, 200, Na(delegate { return (object)Estado(); })); return; }
            if (metodo == "POST" && caminho == "/api/acao")
            {
                object a;
                string acao = dados.TryGetValue("acao", out a) ? Convert.ToString(a) : "";
                dados["_token"] = tk;
                RespJson(s, 200, Na(delegate { return Acao(acao, dados); }));
                return;
            }
            RespJson(s, 404, Erro("não encontrado"));
        }

        object Na(Func<object> f) { return ui.IsHandleCreated ? ui.Invoke(f) : null; }
        byte[] Na(Func<byte[]> f) { return f == null ? new byte[0] : ((byte[])Na(delegate { return (object)f(); }) ?? new byte[0]); }

        static Dictionary<string, object> Erro(string msg) { return new Dictionary<string, object> { { "ok", false }, { "erro", msg } }; }

        static void RespJson(Stream s, int code, object o) { Responder(s, code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(Json.Serialize(o))); }

        static void Responder(Stream s, int code, string tipo, byte[] corpo)
        {
            string status = code == 200 ? "OK" : code == 401 ? "Unauthorized" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : code == 429 ? "Too Many Requests" : "Bad Request";
            byte[] cab = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + tipo + "\r\nContent-Length: " + corpo.Length +
                "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
            s.Write(cab, 0, cab.Length);
            s.Write(corpo, 0, corpo.Length);
            s.Flush();
        }
    }
}
