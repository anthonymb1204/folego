// Tela do Fôlego, toda desenhada à mão (cards de vidro, anéis, chaves liga/desliga), misturando o
// visual de painel com cards e o launcher da TV (papel de parede desfocado e relógio grande).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Folego
{
    class MainForm : Form
    {
        const string Versao = "0.16.0";
        const string Repo = "https://github.com/anthonymb1204/folego";

        // ---------------- Cores e fontes ----------------
        static readonly Color CText = Color.FromArgb(242, 241, 250), CMuted = Color.FromArgb(168, 166, 192),
            CFaint = Color.FromArgb(112, 110, 138), CCard = Color.FromArgb(172, 17, 15, 30), CBorder = Color.FromArgb(34, 255, 255, 255),
            CPurple = Color.FromArgb(139, 92, 246), CLilac = Color.FromArgb(178, 150, 255), CBlue = Color.FromArgb(59, 110, 246),
            CTeal = Color.FromArgb(45, 212, 191), CGood = Color.FromArgb(76, 201, 140), CBad = Color.FromArgb(239, 98, 98),
            COrange = Color.FromArgb(251, 146, 60), CWarnSoft = Color.FromArgb(242, 200, 90);

        const string GHome = "", GGame = "", GStart = "", GSettings = "", GShield = "",
            GMem = "", GCpu = "", GDrive = "", GPulse = "", GBroom = "", GPlay = "",
            GClose = "", GAdd = "", GRefresh = "", GInfo = "", GClock = "\uE823", GSpeed = "\uEC4A", GPhone = "\uE8EA", GBell = "\uEA8F", GNet = "\uE839";

        static readonly CultureInfo Pt = new CultureInfo("pt-BR");
        static readonly string IconFamily = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        static bool HasFont(string n) { using (Font f = new Font(n, 10f)) return f.Name == n; }
        Font Fnt(float size, string family)
        {
            string k = family + size;
            Font f;
            if (!fonts.TryGetValue(k, out f)) fonts[k] = f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
            return f;
        }
        Font Reg(float s) { return Fnt(s, "Segoe UI"); }
        Font Light(float s) { return Fnt(s, "Segoe UI Light"); }
        Font Semi(float s) { return Fnt(s, "Segoe UI Semibold"); }
        Font Ico(float s) { return Fnt(s, IconFamily); }

        // ---------------- Estado ----------------
        class Hit { public RectangleF R; public string Id; public Action Click; public string Tip; }
        class Act { public DateTime T; public string Text; public Color C; }
        class Consumer { public string Nome; public long Bytes; }

        readonly List<Hit> hits = new List<Hit>();
        string hoverId;
        int page;
        Bitmap bg, appIcon;

        double ramTarget, ramDisp, cpuTarget, cpuDisp, ramFree, ramTotal, diskFree, diskTotal;
        long lastIdle, lastKernel, lastUser;
        bool busy;
        float spin;
        string lastMsg = "Protege o Roblox, seus jogos e o app da frente.";
        Color lastMsgColor = CMuted;
        DateTime lastAuto = DateTime.MinValue;

        readonly List<Act> acts = new List<Act>();
        double freedTodayMB;
        int runsToday;

        List<Consumer> consumers = new List<Consumer>();
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        List<Engine.StartupItem> startItems;
        float startScroll;

        readonly Dictionary<string, bool> opts = new Dictionary<string, bool>();
        List<string> jogos, fechar;
        string currentGame, gameStatus;
        readonly List<string> reabrir = new List<string>();
        bool gameBusy;
        int periodo;                            // Fôlego programado: minutos entre um e outro (0 = desligado)
        DateTime nextRun = DateTime.MaxValue;
        static readonly int[] Periodos = { 0, 15, 30, 60, 120 };
        static readonly string[] PeriodoNomes = { "Desligado", "15 min", "30 min", "1 hora", "2 horas" };

        DateTime wpStamp, wpSoon = DateTime.MaxValue; // papel de parede: data do arquivo usado no fundo atual
        bool bgBuilding;
        Timer tick, anim;
        int tickN;
        NotifyIcon tray;

        static readonly string[,] OptDefs = {
            { "temp", "1" }, { "dns", "1" }, { "lixeira", "0" }, { "auto", "0" }, { "jogo", "1" },
            { "jogo_ram", "1" }, { "jogo_prio", "1" }, { "jogo_reabrir", "1" }, { "fundo_wall", "1" }, { "escala_nitido", "1" }, { "celular", "0" }, { "jogo_acordado", "1" }, { "anti_afk", "0" }, { "aviso_pesado", "1" } };

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        // Como o app foi aberto: janela normal, direto no cantinho da barra de tarefas (sem janela e sem
        // roubar o foco do jogo) ou só para tirar uma "foto" da tela num arquivo (testes; não mexe em nada).
        public enum Modo { Janela, Bandeja, Foto }
        readonly Modo modo;
        bool startHidden, exiting, trayTipShown;

        bool Background { get { return opts["auto"] || opts["jogo"] || periodo > 0; } }

        public MainForm(Modo modo)
        {
            this.modo = modo;
            startHidden = modo == Modo.Bandeja;
            Config.Load();
            for (int i = 0; i < OptDefs.GetLength(0); i++) opts[OptDefs[i, 0]] = Config.B(OptDefs[i, 0], OptDefs[i, 1] == "1");
            jogos = Config.L("jogos", "RobloxPlayerBeta");
            fechar = Config.L("fechar", "");
            periodo = Config.I("periodo", 0);
            tokens = Config.L("tokens", "");
            ResetSchedule();
            SyncJogos();

            Text = "Fôlego " + Versao + (Engine.IsAdmin() ? " (administrador)" : "");
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 720);
            BackColor = Color.FromArgb(14, 12, 24);
            Font = Reg(10f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            try { using (Icon big = new Icon(Icon, 64, 64)) appIcon = big.ToBitmap(); } catch { }

            tray = new NotifyIcon { Icon = Icon, Text = "Fôlego", Visible = false };
            tray.DoubleClick += delegate { ShowFromTray(); };
            ContextMenuStrip tm = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
                BackColor = Color.FromArgb(30, 27, 48), ForeColor = CText, Font = Reg(10f), ShowImageMargin = false };
            tm.Items.Add(new ToolStripMenuItem("Abrir o Fôlego", null, delegate { ShowFromTray(); }) { ForeColor = CText, Font = Semi(10f) });
            tm.Items.Add(new ToolStripMenuItem("Dar fôlego agora", null, delegate { RunOptimize(null); }) { ForeColor = CText });
            tm.Items.Add(new ToolStripSeparator());
            tm.Items.Add(new ToolStripMenuItem("Sair", null, delegate { exiting = true; Close(); }) { ForeColor = CText });
            tray.ContextMenuStrip = tm;
            // Minimizar ou fechar no X com algo automático ligado: continua no cantinho da barra de tarefas.
            Resize += delegate { if (WindowState == FormWindowState.Minimized && Background) HideToTray(); };
            FormClosing += (s, ev) =>
            {
                if (!exiting && ev.CloseReason == CloseReason.UserClosing && Background && modo != Modo.Foto)
                {
                    ev.Cancel = true;
                    HideToTray();
                }
            };
            // O Windows avisa quando o papel de parede muda; o fundo acompanha (confere de novo 1 s depois,
            // quando o arquivo novo já foi gravado).
            Microsoft.Win32.UserPreferenceChangedEventHandler wpHandler = (s, ev) =>
            {
                if (ev.Category == Microsoft.Win32.UserPreferenceCategory.Desktop) wpSoon = DateTime.Now.AddSeconds(1);
            };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += wpHandler;
            FormClosed += delegate { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= wpHandler; };
            FormClosed += delegate { tray.Visible = false; };
            FormClosed += delegate { Escala.Encerrar(); try { UnregisterHotKey(Handle, HotkeyEscala); } catch { } };
            FormClosed += delegate { if (remoto != null) remoto.Parar(); };
            FormClosed += delegate { Acordado(false); };
            if (modo != Modo.Foto && opts["celular"]) StartCelular();

            LoadActivity();
            Native.GetSystemTimes(out lastIdle, out lastKernel, out lastUser);
            ReadStats();
            ramDisp = ramTarget; cpuDisp = cpuTarget;

            LerInicio();
            BuildFps(); // placa de vídeo (e, fora do modo foto, confere driver novo da AMD)
            if (modo == Modo.Foto) return; // foto: sem timers, sem Modo Jogo, sem fôlego automático

            tick = new Timer { Interval = 1000 };
            tick.Tick += delegate
            {
                ReadStats();
                tickN++;
                if (periodo > 0 && !busy && DateTime.Now >= nextRun) RunOptimize("Programado");
                if (tickN % 2 == 0) CheckGame();
                if (tickN % 5 == 0) CheckAntiAfk();
                if (tickN % 30 == 10) CheckPesados();
                if (tickN % 30 == 1) AmostraRam();
                if (tickN % 5 == 2 && WindowState != FormWindowState.Minimized) MedirPing();
                if (tickN % 3 == 0 || DateTime.Now >= wpSoon) { wpSoon = DateTime.MaxValue; CheckWallpaper(); }
                if (tickN % 15 == 0 && page == 0 && Visible) RefreshList();
                if (tickN % 3600 == 0) ChecarDriverAmd(false);
                if (tickN == 20 || tickN % 21600 == 0) ChecarAtualizacao(); // 20 s depois de abrir e a cada 6 h
                if (testeUpd && tickN == 5) ChecarAtualizacao();          // --testar-atualizacao: confere e atualiza sozinho (testes)
                if (testeUpd && novaVersao != null && !atualizando) Atualizar();
                cpuDisp = cpuTarget; // a CPU muda o tempo todo: pula direto, sem animar
                if (Math.Abs(ramTarget - ramDisp) > 0.01) StartAnim(); else ramDisp = ramTarget;
                if (Visible && WindowState != FormWindowState.Minimized) Invalidate();
            };
            tick.Start();
            anim = new Timer { Interval = 33 };
            anim.Tick += delegate { Animate(); };
            Shown += delegate { RefreshList(); LoadStartup(); CheckGame(); };

            // Outra cópia do Fôlego aberta pela pessoa pede para esta aparecer.
            System.Threading.Thread waiter = new System.Threading.Thread(delegate ()
            {
                try
                {
                    using (System.Threading.EventWaitHandle ev = new System.Threading.EventWaitHandle(false,
                               System.Threading.EventResetMode.AutoReset, "Folego_Mostrar"))
                        while (true)
                        {
                            ev.WaitOne();
                            if (IsHandleCreated) BeginInvoke((MethodInvoker)ShowFromTray);
                        }
                }
                catch { }
            }) { IsBackground = true };
            waiter.Start();
        }

        // Abrindo em segundo plano: cria a janela sem mostrar (não rouba o foco do jogo).
        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                value = false;
                tray.Visible = true;
                RefreshList();
                LoadStartup();
            }
            base.SetVisibleCore(value);
        }

        void HideToTray()
        {
            Hide();
            tray.Visible = true;
            if (!trayTipShown) // só da primeira vez, e só porque a pessoa minimizou/fechou
            {
                trayTipShown = true;
                tray.ShowBalloonTip(2000, "Fôlego", "Continua cuidando do PC aqui embaixo. Para sair, clique com o botão direito.", ToolTipIcon.Info);
            }
        }

        void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            tray.Visible = false;
            Invalidate();
        }

        // Foto da tela (testes): desenha a página pedida num arquivo, sem mostrar janela.
        public void SaveFoto(string arquivo, int pagina)
        {
            page = pagina;
            consumers = Engine.TopConsumers(8).Select(x => new Consumer { Nome = x.Nome, Bytes = x.Bytes }).ToList();
            foreach (Consumer c in consumers) IconForProcess(c.Nome);
            foreach (string j in jogos.Concat(fechar)) IconForProcess(j);
            startItems = Engine.StartupItems();
            foreach (Engine.StartupItem it in startItems) icons["start:" + it.Nome] = IconFromFile(ExeFromCommand(it.Comando));
            System.Threading.Thread.Sleep(500);
            for (int i = 0; i < 3; i++) MedirPingSync();
            ReadStats();
            if (Environment.GetCommandLineArgs().Contains("--demo")) // só para testar o desenho do histórico
                for (int i = 0; i < HistMax; i++)
                {
                    hist.Add((float)(0.78 + 0.1 * Math.Sin(i / 14.0) - (i % 60 < 3 ? 0.12 : 0)));
                    histT.Add(DateTime.Now.AddSeconds(-30 * (HistMax - i)));
                }
            ramDisp = ramTarget; cpuDisp = cpuTarget;
            using (Bitmap b = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                using (Graphics g = Graphics.FromImage(b))
                    OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, ClientSize)));
                b.Save(arquivo, ImageFormat.Png);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            try { DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { } // barra de título escura
            if (modo != Modo.Foto && !RegisterHotKey(Handle, HotkeyEscala, 0x2 | 0x1 | 0x4000, (int)Keys.F)) // Ctrl+Alt+F
                Engine.Log("Escala nítida: o atalho Ctrl+Alt+F já é usado por outro programa");
        }

        // ---------------- Fundo: papel de parede desfocado (como na TV) ou roxo com montanhas ----------------
        Bitmap BuildBackground(Size sz)
        {
            Bitmap b = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Image wp = opts["fundo_wall"] ? LoadWallpaper() : null;
                if (wp != null)
                {
                    using (wp)
                    using (Bitmap small = new Bitmap(Math.Max(1, sz.Width / 14), Math.Max(1, sz.Height / 14)))
                    {
                        using (Graphics gs = Graphics.FromImage(small))
                        {
                            gs.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gs.DrawImage(wp, Cover(wp.Size, small.Size));
                        }
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ia.SetWrapMode(WrapMode.TileFlipXY);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(small, new Rectangle(0, 0, sz.Width, sz.Height), 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    using (SolidBrush dark = new SolidBrush(Color.FromArgb(150, 10, 8, 22))) g.FillRectangle(dark, 0, 0, sz.Width, sz.Height);
                    using (LinearGradientBrush tint = new LinearGradientBrush(new Rectangle(0, 0, sz.Width, sz.Height),
                               Color.FromArgb(70, 40, 20, 90), Color.FromArgb(10, 10, 8, 22), 30f))
                        g.FillRectangle(tint, 0, 0, sz.Width, sz.Height);
                }
                else
                {
                    using (LinearGradientBrush sky = new LinearGradientBrush(new Rectangle(0, 0, sz.Width, sz.Height),
                               Color.FromArgb(12, 10, 26), Color.FromArgb(46, 26, 84), 90f))
                        g.FillRectangle(sky, 0, 0, sz.Width, sz.Height);
                    using (GraphicsPath glow = new GraphicsPath())
                    {
                        glow.AddEllipse(sz.Width * 0.45f, sz.Height * 0.25f, sz.Width * 0.7f, sz.Height * 0.8f);
                        using (PathGradientBrush pg = new PathGradientBrush(glow))
                        {
                            pg.CenterColor = Color.FromArgb(90, 150, 90, 255);
                            pg.SurroundColors = new[] { Color.FromArgb(0, 150, 90, 255) };
                            g.FillPath(pg, glow);
                        }
                    }
                    Mountain(g, sz, new[] { 0f, .78f, .18f, .62f, .34f, .72f, .52f, .55f, .70f, .66f, .86f, .50f, 1f, .64f }, Color.FromArgb(34, 20, 60));
                    Mountain(g, sz, new[] { 0f, .86f, .14f, .76f, .30f, .84f, .48f, .72f, .66f, .82f, .82f, .70f, 1f, .80f }, Color.FromArgb(20, 12, 38));
                }
            }
            return b;
        }

        static void Mountain(Graphics g, Size sz, float[] pts, Color c)
        {
            List<PointF> p = new List<PointF> { new PointF(0, sz.Height) };
            for (int i = 0; i < pts.Length; i += 2) p.Add(new PointF(pts[i] * sz.Width, pts[i + 1] * sz.Height));
            p.Add(new PointF(sz.Width, sz.Height));
            using (SolidBrush br = new SolidBrush(c)) g.FillPolygon(br, p.ToArray());
        }

        static string WallFile { get { return Environment.ExpandEnvironmentVariables(@"%APPDATA%\Microsoft\Windows\Themes\TranscodedWallpaper"); } }

        static DateTime WallStamp()
        {
            try { return File.Exists(WallFile) ? File.GetLastWriteTimeUtc(WallFile) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        // Se o papel de parede mudou desde o último fundo, refaz o fundo em segundo plano e troca.
        async void CheckWallpaper()
        {
            if (!opts["fundo_wall"] || bgBuilding || bg == null) return;
            DateTime s = WallStamp();
            if (s == wpStamp) return;
            wpStamp = s;
            bgBuilding = true;
            Size sz = ClientSize;
            Bitmap nb = await Task.Run(() => BuildBackground(sz));
            Bitmap old = bg;
            bg = nb;
            if (old != null) old.Dispose();
            bgBuilding = false;
            Engine.Log("Fundo atualizado: papel de parede novo");
            Invalidate();
        }

        static Image LoadWallpaper()
        {
            try
            {
                string tw = WallFile;
                if (!File.Exists(tw)) return null;
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(tw)))
                using (Image img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        static Rectangle Cover(Size src, Size dst)
        {
            float s = Math.Max((float)dst.Width / src.Width, (float)dst.Height / src.Height);
            int w = (int)(src.Width * s), h = (int)(src.Height * s);
            return new Rectangle((dst.Width - w) / 2, (dst.Height - h) / 2, w, h);
        }

        // ---------------- Desenho ----------------
        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (bg == null) { wpStamp = WallStamp(); bg = BuildBackground(ClientSize); }
            g.DrawImageUnscaled(bg, 0, 0);
            hits.Clear();

            DrawSidebar(g);
            switch (page)
            {
                case 0: DrawHeader(g, null, null); DrawHome(g); break;
                case 1: DrawHeader(g, "Inicialização", "Programas que abrem junto com o Windows"); DrawStartup(g); break;
                case 2: DrawHeader(g, "Modo Jogo", "O PC se prepara sozinho quando o jogo abre"); DrawGame(g); break;
                case 3: DrawHeader(g, "Mais FPS", "Windows e placa de vídeo afinados pro jogo"); DrawFps(g); break;
                default: DrawHeader(g, "Ajustes", "Do seu jeito"); DrawSettings(g); break;
            }
            DrawTooltip(g);
            }
            catch (Exception ex) { Engine.Log("Erro ao desenhar: " + ex.Message); }
        }

        static GraphicsPath RR(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = RR(r, rad)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w = 1f)
        {
            using (GraphicsPath p = RR(r, rad)) using (Pen pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        static void FillGrad(Graphics g, RectangleF r, float rad, Color a, Color b)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (GraphicsPath p = RR(r, rad))
            using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), a, b, 30f))
                g.FillPath(br, p);
        }

        void Txt(Graphics g, string s, Font f, Color c, RectangleF r,
                 StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near, bool wrap = false)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (StringFormat sf = new StringFormat())
            using (SolidBrush b = new SolidBrush(c))
            {
                sf.Alignment = h; sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                if (!wrap) sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s, f, b, r, sf);
            }
        }

        float Measure(Graphics g, string s, Font f)
        {
            using (StringFormat sf = StringFormat.GenericTypographic)
                return g.MeasureString(s, f, 1000, sf).Width;
        }

        // Registra uma área clicável/destacável; devolve se o mouse está em cima dela.
        bool AddHit(RectangleF r, string id, Action click, string tip = null)
        {
            hits.Add(new Hit { R = r, Id = id, Click = click, Tip = tip });
            return hoverId == id;
        }

        void Card(Graphics g, RectangleF r, string title, string glyph)
        {
            Fill(g, r, 20, CCard);
            Stroke(g, r, 20, CBorder);
            if (title == null) return;
            float x = r.X + 20;
            if (glyph != null)
            {
                Txt(g, glyph, Ico(12f), CLilac, new RectangleF(x, r.Y + 17, 24, 24), StringAlignment.Near, StringAlignment.Center);
                x += 28;
            }
            Txt(g, title, Semi(12f), CText, new RectangleF(x, r.Y + 15, r.Width - (x - r.X) - 60, 28), StringAlignment.Near, StringAlignment.Center);
        }

        void Toggle(Graphics g, RectangleF r, bool on, bool hover)
        {
            if (on) FillGrad(g, r, r.Height / 2, CPurple, CBlue);
            else Fill(g, r, r.Height / 2, Color.FromArgb(hover ? 70 : 50, 255, 255, 255));
            float k = r.Height - 6;
            RectangleF knob = new RectangleF(on ? r.Right - k - 3 : r.X + 3, r.Y + 3, k, k);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, knob);
        }

        void Bar(Graphics g, RectangleF r, double frac, bool alert)
        {
            Fill(g, r, r.Height / 2, Color.FromArgb(30, 255, 255, 255));
            float w = (float)(r.Width * Math.Max(0, Math.Min(1, frac)));
            if (w < r.Height) w = r.Height;
            FillGrad(g, new RectangleF(r.X, r.Y, w, r.Height), r.Height / 2, alert ? CBad : CPurple, alert ? COrange : CBlue);
        }

        void DrawSidebar(Graphics g)
        {
            RectangleF sb = new RectangleF(20, 20, 68, ClientSize.Height - 40);
            Fill(g, sb, 24, CCard);
            Stroke(g, sb, 24, CBorder);
            if (appIcon != null) g.DrawImage(appIcon, sb.X + 16, sb.Y + 18, 36, 36);

            string[] gl = { GHome, GStart, GGame, GSpeed, GSettings };
            string[] nm = { "Início", "Inicialização", "Modo Jogo", "Mais FPS", "Ajustes" };
            for (int i = 0; i < gl.Length; i++)
            {
                int idx = i;
                RectangleF r = new RectangleF(sb.X + 10, sb.Y + 88 + i * 60, 48, 48);
                bool hov = AddHit(r, "nav" + i, delegate { page = idx; startScroll = 0; if (idx == 1) LoadStartup(); Invalidate(); }, nm[i]);
                if (page == i) FillGrad(g, r, 15, CPurple, CBlue);
                else if (hov) Fill(g, r, 15, Color.FromArgb(28, 255, 255, 255));
                Txt(g, gl[i], Ico(15f), page == i ? Color.White : (hov ? CText : CMuted), r, StringAlignment.Center, StringAlignment.Center);
                if (i == gl.Length - 1 && novaVersao != null) // bolinha: tem versão nova nos Ajustes
                    using (SolidBrush dot = new SolidBrush(COrange)) g.FillEllipse(dot, r.Right - 13, r.Y + 7, 9, 9);
            }

            bool admin = Engine.IsAdmin();
            RectangleF a = new RectangleF(sb.X + 10, sb.Bottom - 64, 48, 48);
            bool h = AddHit(a, "admin", admin ? null : (Action)RelaunchAdmin,
                admin ? "Modo forte ligado" : "Ligar o modo forte (admin)");
            if (h) Fill(g, a, 15, Color.FromArgb(28, 255, 255, 255));
            Txt(g, GShield, Ico(15f), admin ? CTeal : CMuted, a, StringAlignment.Center, StringAlignment.Center);
        }

        void DrawHeader(Graphics g, string title, string sub)
        {
            float x = 116;
            if (title == null)
            {
                string sauda = Saudacao(), nome = NomeUsuario();
                Txt(g, sauda + ",", Light(24f), CText, new RectangleF(x, 14, 600, 46));
                Txt(g, nome, Semi(24f), CLilac, new RectangleF(x, 52, 600, 46));
                Txt(g, "Libera  ·  Limpa  ·  Joga", Reg(10.5f), CMuted, new RectangleF(x + 2, 100, 600, 24));
            }
            else
            {
                Txt(g, title, Semi(24f), CText, new RectangleF(x, 28, 600, 46));
                Txt(g, sub, Reg(10.5f), CMuted, new RectangleF(x + 2, 76, 600, 24));
            }
            DateTime now = DateTime.Now;
            float W = ClientSize.Width;
            Txt(g, now.ToString("HH:mm"), Light(46f), CText, new RectangleF(W - 424, 4, 400, 86), StringAlignment.Far, StringAlignment.Near);
            Txt(g, now.ToString("dddd, d 'de' MMMM", Pt), Reg(10.5f), CMuted, new RectangleF(W - 424, 92, 400, 24), StringAlignment.Far);
            // Internet: ping atual (como o Wi-Fi no launcher da TV).
            string pt; Color pc;
            PingTexto(out pt, out pc);
            float pw = Measure(g, pt, Reg(9.5f)) + 6;
            Txt(g, pt, Reg(9.5f), pc, new RectangleF(W - 24 - pw, 116, pw + 2, 20), StringAlignment.Far);
            Txt(g, GNet, Ico(10f), pc, new RectangleF(W - 24 - pw - 22, 116, 18, 20), StringAlignment.Far, StringAlignment.Center);
        }

        void DrawTooltip(Graphics g)
        {
            Hit h = hits.LastOrDefault(x => x.Id == hoverId && x.Tip != null);
            if (h == null) return;
            Font f = Reg(9.5f);
            float w = Measure(g, h.Tip, f) + 24, ht = 30;
            RectangleF r = h.R.X < 100
                ? new RectangleF(h.R.Right + 14, h.R.Y + (h.R.Height - ht) / 2, w, ht)
                : new RectangleF(h.R.X + h.R.Width / 2 - w / 2, h.R.Y - ht - 8, w, ht);
            if (r.Right > ClientSize.Width - 8) r.X = ClientSize.Width - 8 - r.Width;
            if (r.X < 8) r.X = 8;
            if (r.Y < 8) r.Y = h.R.Bottom + 8;
            Fill(g, r, 10, Color.FromArgb(240, 32, 28, 52));
            Stroke(g, r, 10, Color.FromArgb(60, 255, 255, 255));
            Txt(g, h.Tip, f, CText, r, StringAlignment.Center, StringAlignment.Center);
        }

        // ---------------- Início ----------------
        void DrawHome(Graphics g)
        {
            float x0 = 116, W = ClientSize.Width - 24 - x0, top = 150, H1 = 262;
            RectangleF a = new RectangleF(x0, top, 300, H1);
            RectangleF b = new RectangleF(a.Right + 16, top, 330, H1);
            RectangleF c = new RectangleF(b.Right + 16, top, x0 + W - b.Right - 16, H1);
            float top2 = a.Bottom + 16, H2 = ClientSize.Height - 20 - top2;
            RectangleF d = new RectangleF(x0, top2, 480, H2);
            RectangleF e = new RectangleF(d.Right + 16, top2, x0 + W - d.Right - 16, H2);

            // Memória: anel como o do "Focus Timer".
            Card(g, a, "Memória", GMem);
            float cx = a.X + a.Width / 2, cy = a.Y + 112, R = 56;
            RectangleF ring = new RectangleF(cx - R, cy - R, 2 * R, 2 * R);
            using (Pen track = new Pen(Color.FromArgb(32, 255, 255, 255), 11)) g.DrawEllipse(track, ring);
            float sweep = (float)(360 * Math.Max(0, Math.Min(1, ramDisp)));
            bool alert = ramDisp > 0.85;
            if (sweep > 0.5f)
                using (LinearGradientBrush br = new LinearGradientBrush(ring, alert ? CBad : CPurple, alert ? COrange : CBlue, 45f))
                using (Pen p = new Pen(br, 11) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(p, ring, -90, sweep);
            Txt(g, (int)Math.Round(ramDisp * 100) + "%", Light(23f), CText, new RectangleF(cx - 70, cy - 26, 140, 40), StringAlignment.Center, StringAlignment.Center);
            Txt(g, "em uso", Reg(8.8f), CMuted, new RectangleF(cx - 70, cy + 10, 140, 20), StringAlignment.Center);
            DrawHistorico(g, new RectangleF(a.X + 20, a.Y + 184, a.Width - 40, 40));
            Txt(g, "últimas 2 h", Reg(8f), CFaint, new RectangleF(a.X + 20, a.Y + 166, a.Width - 40, 16), StringAlignment.Far);
            Txt(g, string.Format(Pt, "{0:0.0} GB livres de {1:0.0} GB", ramFree, ramTotal), Reg(9.5f), CMuted,
                new RectangleF(a.X, a.Bottom - 32, a.Width, 22), StringAlignment.Center);

            // Dar fôlego: o botão principal.
            Card(g, b, "Dar fôlego", GBroom);
            if (periodo > 0)
            {
                string ag = PeriodoNome(periodo) + " · " + nextRun.ToString("HH:mm");
                Font af = Reg(8.8f);
                float aw = Measure(g, ag, af);
                RectangleF ar = new RectangleF(b.Right - 20 - aw - 2, b.Y + 15, aw + 4, 28);
                Txt(g, ag, af, CMuted, ar, StringAlignment.Far, StringAlignment.Center);
                Txt(g, GClock, Ico(9f), CLilac, new RectangleF(ar.X - 20, b.Y + 15, 18, 28), StringAlignment.Far, StringAlignment.Center);
            }
            float bx = b.X + b.Width / 2, by = b.Y + 98, BR = 40;
            RectangleF btn = new RectangleF(bx - BR, by - BR, 2 * BR, 2 * BR);
            bool hov = AddHit(RectangleF.Inflate(btn, 6, 6), "go", delegate { RunOptimize(null); }, busy ? null : "Libera RAM e limpa o que estiver marcado");
            if (hov || busy)
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(RectangleF.Inflate(btn, 22, 22));
                    using (PathGradientBrush pg = new PathGradientBrush(gp))
                    {
                        pg.CenterColor = Color.FromArgb(110, CPurple);
                        pg.SurroundColors = new[] { Color.FromArgb(0, CPurple) };
                        g.FillPath(pg, gp);
                    }
                }
            using (LinearGradientBrush br = new LinearGradientBrush(btn, CPurple, CBlue, 45f)) g.FillEllipse(br, btn);
            if (busy)
                using (Pen sp = new Pen(Color.FromArgb(230, 255, 255, 255), 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(sp, RectangleF.Inflate(btn, -14, -14), spin, 100);
            else
                Txt(g, GPlay, Ico(22f), Color.White, new RectangleF(btn.X + 3, btn.Y, btn.Width, btn.Height), StringAlignment.Center, StringAlignment.Center);
            Txt(g, busy ? "Dando fôlego..." : "Dar fôlego agora", Semi(12.5f), CText, new RectangleF(b.X, by + BR + 8, b.Width, 26), StringAlignment.Center);
            Txt(g, lastMsg, Reg(9.2f), lastMsgColor, new RectangleF(b.X + 18, by + BR + 33, b.Width - 36, 34), StringAlignment.Center, StringAlignment.Near, true);

            string[] ck = { "temp", "dns", "lixeira", "auto" };
            string[] cn = { "Temporários", "DNS", "Lixeira", "Auto 85%" };
            string[] ct = { "Apagar temporários com mais de 1 dia", "Limpar o cache de DNS da internet",
                            "Esvaziar a lixeira (não dá para desfazer)", "Dar fôlego sozinho quando a RAM passar de 85%" };
            Font cf = Reg(9f);
            float[] cw = cn.Select(s => Measure(g, s, cf) + 22).ToArray();
            float cxp = b.X + (b.Width - (cw.Sum() + 6 * (cw.Length - 1))) / 2;
            for (int i = 0; i < ck.Length; i++)
            {
                string k = ck[i];
                RectangleF r = new RectangleF(cxp, b.Bottom - 40, cw[i], 26);
                bool on = opts[k];
                bool h = AddHit(r, "chip" + k, delegate { ToggleOpt(k); }, ct[i]);
                if (on) Fill(g, r, 13, Color.FromArgb(72, CPurple));
                else if (h) Fill(g, r, 13, Color.FromArgb(22, 255, 255, 255));
                Stroke(g, r, 13, on ? Color.FromArgb(170, CLilac) : Color.FromArgb(h ? 90 : 50, 255, 255, 255));
                Txt(g, cn[i], cf, on ? CText : CMuted, r, StringAlignment.Center, StringAlignment.Center);
                cxp += cw[i] + 6;
            }

            // Sistema
            Card(g, c, "Sistema", GPulse);
            float y = c.Y + 60;
            StatRow(g, c, ref y, GCpu, "Processador", string.Format("{0:0}%", cpuDisp * 100), cpuDisp, cpuDisp > 0.9);
            StatRow(g, c, ref y, GDrive, "Disco C:", string.Format(Pt, "{0:0} GB livres", diskFree),
                diskTotal > 0 ? 1 - diskFree / diskTotal : 0, diskTotal > 0 && diskFree / diskTotal < 0.1);
            RectangleF t = new RectangleF(c.X + 16, c.Bottom - 94, c.Width - 32, 78);
            Fill(g, t, 14, Color.FromArgb(20, 255, 255, 255));
            Txt(g, "Liberado hoje", Reg(9.5f), CMuted, new RectangleF(t.X + 14, t.Y + 10, t.Width - 28, 20));
            Txt(g, FormatMB(freedTodayMB), Semi(20f), CTeal, new RectangleF(t.X + 12, t.Y + 30, t.Width - 24, 40), StringAlignment.Near, StringAlignment.Center);
            Txt(g, runsToday == 1 ? "1 vez" : runsToday + " vezes", Reg(9.5f), CMuted, new RectangleF(t.X + 14, t.Y + 30, t.Width - 28, 40), StringAlignment.Far, StringAlignment.Center);

            // Gastando mais RAM, com o ícone de cada programa (como os logos na TV).
            Card(g, d, "Gastando mais RAM", GMem);
            RectangleF rb = new RectangleF(d.Right - 50, d.Y + 12, 34, 34);
            if (AddHit(rb, "refresh", RefreshList, "Atualizar")) Fill(g, rb, 10, Color.FromArgb(28, 255, 255, 255));
            Txt(g, GRefresh, Ico(11f), CMuted, rb, StringAlignment.Center, StringAlignment.Center);
            RectangleF bell = new RectangleF(rb.X - 40, rb.Y, 34, 34);
            bool hb = AddHit(bell, "bell", delegate { ToggleOpt("aviso_pesado"); },
                opts["aviso_pesado"] ? "Avisando quando um programa passa de 1,5 GB (clique para parar)" : "Avisar quando um programa passar de 1,5 GB");
            if (hb) Fill(g, bell, 10, Color.FromArgb(28, 255, 255, 255));
            Txt(g, GBell, Ico(11f), opts["aviso_pesado"] ? COrange : CFaint, bell, StringAlignment.Center, StringAlignment.Center);
            long max = consumers.Count > 0 ? Math.Max(1, consumers.Max(x => x.Bytes)) : 1;
            float ry = d.Y + 54;
            if (consumers.Count == 0) Txt(g, "Carregando...", Reg(10f), CMuted, new RectangleF(d.X + 20, ry, 300, 30));
            foreach (Consumer cn2 in consumers)
            {
                if (ry + 32 > d.Bottom - 10) break;
                Consumer item = cn2;
                RectangleF row = new RectangleF(d.X + 10, ry, d.Width - 20, 32);
                bool hrow = AddHit(row, "row" + item.Nome, null) || hoverId == "kill" + item.Nome;
                if (hrow) Fill(g, row, 10, Color.FromArgb(20, 255, 255, 255));
                DrawAppIcon(g, item.Nome, new RectangleF(row.X + 10, ry + 5, 22, 22));
                bool pesado = item.Bytes > LimitePesado;
                Txt(g, item.Nome, Reg(10f), pesado ? COrange : CText, new RectangleF(row.X + 42, ry, 170, 32), StringAlignment.Near, StringAlignment.Center);
                Bar(g, new RectangleF(row.X + 222, ry + 13, 120, 6), (double)item.Bytes / max, pesado);
                Txt(g, string.Format(Pt, "{0:N0} MB", item.Bytes / 1048576.0), Reg(9.5f), CMuted,
                    new RectangleF(row.X + 344, ry, 78, 32), StringAlignment.Far, StringAlignment.Center);
                if (hrow)
                {
                    RectangleF kx = new RectangleF(row.Right - 30, ry + 4, 24, 24);
                    bool hk = AddHit(kx, "kill" + item.Nome, delegate { AskKill(item.Nome); }, "Fechar " + item.Nome);
                    if (hk) Fill(g, kx, 8, Color.FromArgb(50, CBad));
                    Txt(g, GClose, Ico(8.5f), hk ? CBad : CMuted, kx, StringAlignment.Center, StringAlignment.Center);
                }
                ry += 34;
            }

            // Modo Jogo + Atividade (como a "agenda" da referência: barrinha colorida + hora).
            Card(g, e, "Modo Jogo", GGame);
            RectangleF tg = new RectangleF(e.Right - 66, e.Y + 17, 46, 24);
            Toggle(g, tg, opts["jogo"], AddHit(tg, "tgjogo", delegate { ToggleOpt("jogo"); }, opts["jogo"] ? "Desligar o Modo Jogo" : "Ligar o Modo Jogo"));
            string st; Color sc;
            if (currentGame != null) { st = "● " + (gameStatus ?? currentGame + " aberto"); sc = CGood; }
            else if (opts["jogo"]) { st = "Esperando " + string.Join(", ", jogos.Select(Bonito)) + " abrir"; sc = CMuted; }
            else { st = "Desligado"; sc = CFaint; }
            Txt(g, st, Reg(9.8f), sc, new RectangleF(e.X + 20, e.Y + 50, e.Width - 40, 24), StringAlignment.Near, StringAlignment.Center);
            using (Pen ln = new Pen(Color.FromArgb(26, 255, 255, 255))) g.DrawLine(ln, e.X + 20, e.Y + 84, e.Right - 20, e.Y + 84);
            Txt(g, "Atividade de hoje", Semi(10.5f), CText, new RectangleF(e.X + 20, e.Y + 94, 300, 24));
            float ay = e.Y + 124;
            List<Act> recent = acts.AsEnumerable().Reverse().Take(4).ToList();
            if (recent.Count == 0)
                Txt(g, "Nada ainda hoje. Aperte o Dar fôlego!", Reg(9.5f), CMuted, new RectangleF(e.X + 20, ay, e.Width - 40, 24));
            foreach (Act act in recent)
            {
                if (ay + 32 > e.Bottom - 8) break;
                Fill(g, new RectangleF(e.X + 20, ay + 4, 3, 24), 1.5f, act.C);
                Txt(g, act.T.ToString("HH:mm"), Reg(9f), CMuted, new RectangleF(e.X + 32, ay, 46, 32), StringAlignment.Near, StringAlignment.Center);
                Txt(g, act.Text, Reg(9.5f), CText, new RectangleF(e.X + 80, ay, e.Width - 100, 32), StringAlignment.Near, StringAlignment.Center);
                ay += 36;
            }
        }

        void StatRow(Graphics g, RectangleF c, ref float y, string glyph, string label, string value, double frac, bool alert)
        {
            Txt(g, glyph, Ico(11f), CMuted, new RectangleF(c.X + 20, y, 20, 22), StringAlignment.Near, StringAlignment.Center);
            Txt(g, label, Reg(10f), CText, new RectangleF(c.X + 44, y, 140, 22), StringAlignment.Near, StringAlignment.Center);
            Txt(g, value, Reg(10f), CMuted, new RectangleF(c.X + 120, y, c.Width - 140, 22), StringAlignment.Far, StringAlignment.Center);
            Bar(g, new RectangleF(c.X + 20, y + 28, c.Width - 40, 6), frac, alert);
            y += 54;
        }

        static string FormatMB(double mb)
        {
            return mb >= 1024 ? string.Format(Pt, "{0:0.0} GB", mb / 1024) : string.Format(Pt, "{0:0} MB", mb);
        }

        static string Bonito(string proc)
        {
            if (proc.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase)) return "Roblox";
            return proc.EndsWith(".Root", StringComparison.OrdinalIgnoreCase) ? proc.Substring(0, proc.Length - 5) : proc;
        }

        void DrawAppIcon(Graphics g, string key, RectangleF r)
        {
            Bitmap ic;
            if (TryIcon(key, out ic) && ic != null) { g.DrawImage(ic, r); return; }
            Fill(g, r, r.Width / 2, Color.FromArgb(70, CPurple));
            Txt(g, key.Length > 0 ? key.Substring(0, 1).ToUpper() : "?", Semi(r.Width * 0.38f), CText, r, StringAlignment.Center, StringAlignment.Center);
        }

        // ---------------- Inicialização ----------------
        void DrawStartup(Graphics g)
        {
            RectangleF c = new RectangleF(116, 150, ClientSize.Width - 140, ClientSize.Height - 170);
            List<Engine.StartupItem> items = startItems ?? new List<Engine.StartupItem>();
            int on = items.Count(i => i.Ligado);
            Card(g, c, items.Count == 0 ? "Carregando..." : on + " de " + items.Count + " ligados", GStart);
            Txt(g, "Desligar não desinstala nada: dá para religar quando quiser. Vale a partir da próxima vez que o PC ligar.",
                Reg(9.5f), CMuted, new RectangleF(c.X + 20, c.Y + 46, c.Width - 100, 22));
            RectangleF rb = new RectangleF(c.Right - 50, c.Y + 12, 34, 34);
            if (AddHit(rb, "srefresh", LoadStartup, "Atualizar")) Fill(g, rb, 10, Color.FromArgb(28, 255, 255, 255));
            Txt(g, GRefresh, Ico(11f), CMuted, rb, StringAlignment.Center, StringAlignment.Center);

            RectangleF area = new RectangleF(c.X + 8, c.Y + 80, c.Width - 16, c.Height - 90);
            float rowH = 58, content = items.Count * rowH;
            startScroll = Math.Max(0, Math.Min(startScroll, Math.Max(0, content - area.Height)));
            g.SetClip(area);
            float y = area.Y - startScroll;
            for (int i = 0; i < items.Count; i++, y += rowH)
            {
                if (y + rowH < area.Y || y > area.Bottom) continue;
                Engine.StartupItem it = items[i];
                RectangleF row = new RectangleF(area.X, y, area.Width - 14, rowH - 4);
                RectangleF vis = RectangleF.Intersect(row, area);
                RectangleF tg = new RectangleF(row.Right - 66, y + 15, 46, 24);
                bool hrow = AddHit(vis, "srow" + i, null) || hoverId == "stg" + i;
                if (hrow) Fill(g, row, 12, Color.FromArgb(20, 255, 255, 255));
                DrawAppIcon(g, "start:" + it.Nome, new RectangleF(row.X + 14, y + 11, 32, 32));
                Txt(g, it.Nome, Semi(11f), it.Ligado ? CText : CMuted, new RectangleF(row.X + 60, y + 7, row.Width - 280, 24));
                Txt(g, it.Comando, Reg(8.5f), CFaint, new RectangleF(row.X + 60, y + 30, row.Width - 280, 20));
                RectangleF pill = new RectangleF(row.Right - 170, y + 16, 76, 22);
                Stroke(g, pill, 11, Color.FromArgb(60, 255, 255, 255));
                Txt(g, it.Todos ? "Todos" : "Só você", Reg(8.5f), CMuted, pill, StringAlignment.Center, StringAlignment.Center);
                AddHit(RectangleF.Intersect(tg, area), "stg" + i, delegate { ToggleStartup(it); }, it.Ligado ? "Desligar" : "Ligar");
                Toggle(g, tg, it.Ligado, hrow);
            }
            g.ResetClip();
            if (content > area.Height)
            {
                float vh = area.Height * area.Height / content, vy = area.Y + (area.Height - vh) * (startScroll / (content - area.Height));
                Fill(g, new RectangleF(c.Right - 12, vy, 4, vh), 2, Color.FromArgb(70, 255, 255, 255));
            }
        }

        // ---------------- Modo Jogo ----------------
        void DrawGame(Graphics g)
        {
            float x0 = 116, W = ClientSize.Width - 24 - x0, half = (W - 16) / 2, top = 150, H1 = 196;
            RectangleF l1 = new RectangleF(x0, top, half, H1), r1 = new RectangleF(l1.Right + 16, top, half, H1);
            float top2 = l1.Bottom + 16, H2 = ClientSize.Height - 20 - top2;
            RectangleF l2 = new RectangleF(x0, top2, half, H2), r2 = new RectangleF(l1.Right + 16, top2, half, H2);

            Card(g, l1, "Jogos", GGame);
            ChipEditor(g, l1, jogos, "jogos", "Quando um destes abrir, o Modo Jogo entra sozinho. Use o Adicionar com o jogo aberto.");
            Card(g, r1, "Fechar quando o jogo abrir", GClose);
            ChipEditor(g, r1, fechar, "fechar", "Fecham quando o jogo abre e voltam quando ele fecha. Ex.: Steam, navegador.");

            Card(g, l2, "Opções", GSettings);
            float y = l2.Y + 52;
            OptionRow(g, l2, ref y, "jogo", "Modo Jogo ligado", "Entra sozinho quando um jogo da lista abre");
            OptionRow(g, l2, ref y, "jogo_ram", "Liberar RAM", "E o cache de memória, no modo forte");
            OptionRow(g, l2, ref y, "jogo_prio", "Prioridade alta para o jogo", "O Windows dá preferência ao jogo");
            OptionRow(g, l2, ref y, "jogo_reabrir", "Reabrir os fechados", "Quando o jogo acabar");

            Card(g, r2, "Agora e farm", GPulse);
            bool live = currentGame != null;
            Txt(g, GGame, Ico(30f), live ? CGood : CFaint, new RectangleF(r2.X + 20, r2.Y + 54, 60, 60), StringAlignment.Center, StringAlignment.Center);
            Txt(g, live ? Bonito(currentGame) + " aberto" : (opts["jogo"] ? "Nenhum jogo aberto" : "Modo Jogo desligado"),
                Semi(13f), CText, new RectangleF(r2.X + 92, r2.Y + 54, r2.Width - 112, 28));
            string det = live ? (gameStatus ?? "Preparando o PC...")
                : opts["jogo"] ? "De olho em: " + string.Join(", ", jogos.Select(Bonito)) : "Ligue nas Opções ao lado";
            Txt(g, det, Reg(9.2f), live ? CGood : CMuted, new RectangleF(r2.X + 92, r2.Y + 82, r2.Width - 112, 40), StringAlignment.Near, StringAlignment.Near, true);
            using (Pen ln = new Pen(Color.FromArgb(26, 255, 255, 255))) g.DrawLine(ln, r2.X + 20, r2.Y + 130, r2.Right - 20, r2.Y + 130);
            float fy = r2.Y + 140;
            OptionRow(g, r2, ref fy, "jogo_acordado", "PC acordado durante o jogo", "Não deixa o Windows dormir no meio do farm");
            OptionRow(g, r2, ref fy, "anti_afk", "Anti-AFK (por sua conta e risco)", "Um passo pra frente e um pra trás a cada ~15 min, se você estiver parado");
            if (opts["anti_afk"])
                Txt(g, currentGame != null ? "Próximo toque no jogo: " + nextAfk.ToString("HH:mm") + " (se você estiver parado)" : "Entra quando o jogo abrir. É contra as regras do Roblox: a conta pode ser punida.",
                    Reg(8.8f), COrange, new RectangleF(r2.X + 20, fy + 2, r2.Width - 40, 36), StringAlignment.Near, StringAlignment.Near, true);
        }

        // ---- Anti-AFK: com o jogo aberto e a pessoa parada, aperta espaço no jogo de vez em quando ----
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        DateTime nextAfk = DateTime.MaxValue;
        static readonly Random rnd = new Random();

        static double SegundosParado()
        {
            LASTINPUTINFO li = new LASTINPUTINFO { cbSize = 8 };
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
        }

        static IntPtr JanelaDoJogo(string nome)
        {
            foreach (Process p in Process.GetProcessesByName(nome))
                try { if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle; } catch { } finally { p.Dispose(); }
            return IntPtr.Zero;
        }

        const int AfkPasso = 150; // ms segurando cada tecla (a mesma nas duas, para voltar ao mesmo ponto)

        static void Tecla(byte vk, byte scan, int ms)
        {
            keybd_event(vk, scan, 0x0008, UIntPtr.Zero);          // KEYEVENTF_SCANCODE: o jogo lê a tecla física
            System.Threading.Thread.Sleep(ms);
            keybd_event(vk, scan, 0x0008 | 0x0002, UIntPtr.Zero); // solta
        }

        void CheckAntiAfk()
        {
            if (!opts["anti_afk"] || currentGame == null || DateTime.Now < nextAfk || SegundosParado() < 60) return;
            IntPtr jogo = JanelaDoJogo(currentGame);
            if (jogo == IntPtr.Zero) return;
            nextAfk = DateTime.Now.AddMinutes(12 + rnd.Next(0, 6));
            IntPtr antes = Native.GetForegroundWindow();
            bool trocou = antes != jogo;
            if (trocou)
            {
                keybd_event(0x12, 0x38, 0, UIntPtr.Zero); keybd_event(0x12, 0x38, 2, UIntPtr.Zero); // Alt: o Windows deixa trocar a janela da frente
                SetForegroundWindow(jogo);
                System.Threading.Thread.Sleep(250);
            }
            if (Native.GetForegroundWindow() == jogo)
            {
                // Um passinho pra frente (W) e o mesmo pra trás (S): o boneco volta ao mesmo lugar e o farm continua.
                Tecla(0x57, 0x11, AfkPasso);
                System.Threading.Thread.Sleep(120);
                Tecla(0x53, 0x1F, AfkPasso);
                AddActivity(DateTime.Now, "Anti-AFK: mexeu no " + Bonito(currentGame), true);
            }
            else Engine.Log("Anti-AFK: não consegui trazer o jogo para a frente");
            if (trocou && antes != IntPtr.Zero) { System.Threading.Thread.Sleep(150); SetForegroundWindow(antes); }
            Invalidate();
        }

        // ---- PC acordado: enquanto o jogo está aberto, o Windows não suspende/dorme por inatividade ----
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        bool acordado;

        void Acordado(bool on)
        {
            if (on == acordado) return;
            acordado = on;
            SetThreadExecutionState(on ? 0x80000001u : 0x80000000u); // ES_CONTINUOUS | ES_SYSTEM_REQUIRED
        }

        void ChipEditor(Graphics g, RectangleF card, List<string> data, string cfgKey, string hint)
        {
            Font f = Reg(10f);
            float x = card.X + 20, y = card.Y + 58, maxX = card.Right - 20;
            foreach (string s in data.ToList())
            {
                string item = s;
                float w = Measure(g, Bonito(item), f) + 72;
                if (x + w > maxX) { x = card.X + 20; y += 42; }
                RectangleF r = new RectangleF(x, y, w, 34);
                Fill(g, r, 17, Color.FromArgb(64, CPurple));
                Stroke(g, r, 17, Color.FromArgb(150, CLilac));
                DrawAppIcon(g, item, new RectangleF(r.X + 8, y + 7, 20, 20));
                Txt(g, Bonito(item), f, CText, new RectangleF(r.X + 34, y, w - 62, 34), StringAlignment.Near, StringAlignment.Center);
                RectangleF xr = new RectangleF(r.Right - 30, y + 5, 24, 24);
                bool h = AddHit(xr, "rm" + cfgKey + item, delegate { RemoveItem(data, cfgKey, item); }, "Tirar da lista");
                if (h) Fill(g, xr, 12, Color.FromArgb(50, CBad));
                Txt(g, GClose, Ico(8f), h ? CBad : CMuted, xr, StringAlignment.Center, StringAlignment.Center);
                x += w + 8;
            }
            float aw = Measure(g, "Adicionar", f) + 46;
            if (x + aw > maxX) { x = card.X + 20; y += 42; }
            RectangleF ar = new RectangleF(x, y, aw, 34);
            bool ha = AddHit(ar, "add" + cfgKey, delegate { ShowAddMenu(data, cfgKey); }, "Escolher entre os apps abertos");
            if (ha) Fill(g, ar, 17, Color.FromArgb(24, 255, 255, 255));
            using (GraphicsPath p = RR(ar, 17))
            using (Pen pen = new Pen(Color.FromArgb(ha ? 140 : 80, 255, 255, 255)) { DashStyle = DashStyle.Dash })
                g.DrawPath(pen, p);
            Txt(g, GAdd, Ico(9f), CText, new RectangleF(ar.X + 12, y, 20, 34), StringAlignment.Near, StringAlignment.Center);
            Txt(g, "Adicionar", f, CText, new RectangleF(ar.X + 32, y, aw - 36, 34), StringAlignment.Near, StringAlignment.Center);
            if (data.Count == 0 && cfgKey == "fechar")
                Txt(g, "Nada por enquanto.", Reg(9.5f), CFaint, new RectangleF(ar.Right + 12, y, 200, 34), StringAlignment.Near, StringAlignment.Center);
            Txt(g, hint, Reg(9f), CMuted, new RectangleF(card.X + 20, card.Bottom - 50, card.Width - 40, 40), StringAlignment.Near, StringAlignment.Near, true);
        }

        void OptionRow(Graphics g, RectangleF card, ref float y, string key, string title, string desc)
        {
            RectangleF row = new RectangleF(card.X + 10, y, card.Width - 20, 48);
            bool h = AddHit(row, "opt" + key, delegate { ToggleOpt(key); }, null);
            if (h) Fill(g, row, 12, Color.FromArgb(18, 255, 255, 255));
            Txt(g, title, Reg(10.5f), CText, new RectangleF(row.X + 10, y + 3, row.Width - 90, 22));
            Txt(g, desc, Reg(8.8f), CMuted, new RectangleF(row.X + 10, y + 25, row.Width - 90, 20));
            Toggle(g, new RectangleF(row.Right - 58, y + 12, 46, 24), opts[key], h);
            y += 50;
        }

        // "Fôlego programado": botões Desligado / 15 min / 30 min / 1 hora / 2 horas.
        void PeriodRow(Graphics g, RectangleF card, ref float y)
        {
            Txt(g, "Fôlego programado", Reg(10.5f), CText, new RectangleF(card.X + 20, y + 3, card.Width - 40, 22));
            Txt(g, periodo > 0 ? "Próximo às " + nextRun.ToString("HH:mm") + " (nunca esvazia a lixeira sozinho)" : "De tempos em tempos, sozinho (nunca esvazia a lixeira)",
                Reg(8.8f), CMuted, new RectangleF(card.X + 20, y + 25, card.Width - 40, 20));
            Font f = Reg(9.2f);
            float x = card.X + 20, py = y + 52;
            for (int i = 0; i < Periodos.Length; i++)
            {
                int v = Periodos[i];
                float w = Measure(g, PeriodoNomes[i], f) + 24;
                RectangleF r = new RectangleF(x, py, w, 30);
                bool on = periodo == v;
                bool h = AddHit(r, "per" + v, delegate { SetPeriodo(v); }, null);
                if (on) FillGrad(g, r, 15, CPurple, CBlue);
                else Fill(g, r, 15, Color.FromArgb(h ? 40 : 22, 255, 255, 255));
                if (!on) Stroke(g, r, 15, Color.FromArgb(50, 255, 255, 255));
                Txt(g, PeriodoNomes[i], f, on ? Color.White : CText, r, StringAlignment.Center, StringAlignment.Center);
                x += w + 6;
            }
            y += 92;
        }

        void ActionRow(Graphics g, RectangleF card, ref float y, string id, string title, string desc, string btn, Action click, Color? btnColor = null)
        {
            Txt(g, title, Reg(10.5f), CText, new RectangleF(card.X + 20, y + 3, card.Width - 160, 22));
            Txt(g, desc, Reg(8.8f), CMuted, new RectangleF(card.X + 20, y + 25, card.Width - 160, 20));
            if (btn != null)
            {
                Font f = Reg(9.5f);
                float w = Measure(g, btn, f) + 30;
                RectangleF b = new RectangleF(card.Right - 20 - w, y + 9, w, 30);
                bool h = click != null && AddHit(b, id, click, null);
                if (click == null) Txt(g, btn, f, btnColor ?? CTeal, b, StringAlignment.Far, StringAlignment.Center);
                else
                {
                    if (h) FillGrad(g, b, 15, CPurple, CBlue); else Fill(g, b, 15, Color.FromArgb(30, 255, 255, 255));
                    Stroke(g, b, 15, Color.FromArgb(h ? 0 : 60, 255, 255, 255));
                    Txt(g, btn, f, CText, b, StringAlignment.Center, StringAlignment.Center);
                }
            }
            y += 54;
        }

        // ---------------- Mais FPS ----------------
        // Ajustes do Windows que ajudam o jogo (no registro do usuário; dá para desligar de novo aqui).
        class RegOpt { public string Title, Desc; public Func<bool> Get; public Action<bool> Set; }
        List<RegOpt> winOpts;
        string gpuName, gpuDriver, frameGenKey, adrenalin;
        bool gpuAmd, gpuNvidia;

        static int RegInt(string key, string name, int def)
        {
            try { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)) { object v = k == null ? null : k.GetValue(name); return v is int ? (int)v : def; } }
            catch { return def; }
        }
        static string RegStr(string key, string name)
        {
            try { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)) return k == null ? null : k.GetValue(name) as string; }
            catch { return null; }
        }
        static void RegSetInt(string key, string name, int v)
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key))
                k.SetValue(name, v, Microsoft.Win32.RegistryValueKind.DWord);
        }

        // Políticas oficiais do Edge (as do computador valem mais que as do usuário; sem admin, grava no usuário).
        const string KEdge = @"SOFTWARE\Policies\Microsoft\Edge";
        static int EdgePolicy(string name)
        {
            foreach (Microsoft.Win32.RegistryKey root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                try { using (Microsoft.Win32.RegistryKey k = root.OpenSubKey(KEdge)) { object v = k == null ? null : k.GetValue(name); if (v is int) return (int)v; } }
                catch { }
            return 1;
        }
        static void SetEdgePolicy(string name, bool desligar)
        {
            Microsoft.Win32.RegistryKey root = Engine.IsAdmin() ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
            using (Microsoft.Win32.RegistryKey k = root.CreateSubKey(KEdge))
            {
                if (desligar) k.SetValue(name, 0, Microsoft.Win32.RegistryValueKind.DWord);
                else k.DeleteValue(name, false);
            }
        }

        const string KDx = @"Software\Microsoft\DirectX\UserGpuPreferences", KDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

        void BuildFps()
        {
            winOpts = new List<RegOpt> {
                new RegOpt { Title = "Modo de Jogo do Windows", Desc = "O Windows foca no jogo enquanto ele está aberto",
                    Get = () => RegInt(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1) == 1,
                    Set = v => RegSetInt(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", v ? 1 : 0) },
                new RegOpt { Title = "Sem gravação do Xbox em segundo plano", Desc = "A Xbox Game Bar para de gravar o jogo escondida",
                    Get = () => RegInt(KDvr, "AppCaptureEnabled", 1) == 0 && RegInt(@"System\GameConfigStore", "GameDVR_Enabled", 1) == 0,
                    Set = v => { RegSetInt(KDvr, "AppCaptureEnabled", v ? 0 : 1); RegSetInt(@"System\GameConfigStore", "GameDVR_Enabled", v ? 0 : 1); } },
                new RegOpt { Title = "Edge sem rodar escondido", Desc = "O navegador Edge para de ficar aberto em segundo plano",
                    Get = () => EdgePolicy("StartupBoostEnabled") == 0 && EdgePolicy("BackgroundModeEnabled") == 0,
                    Set = v => { SetEdgePolicy("StartupBoostEnabled", v); SetEdgePolicy("BackgroundModeEnabled", v); if (v) Engine.KillEdgeBackground(); } },
                new RegOpt { Title = "Otimizações para jogos em janela", Desc = "Menos atraso em jogos em janela sem borda, como o Roblox",
                    Get = () => (RegStr(KDx, "DirectXUserGlobalSettings") ?? "").Contains("SwapEffectUpgradeEnable=1"),
                    Set = v =>
                    {
                        string s = Regex.Replace(RegStr(KDx, "DirectXUserGlobalSettings") ?? "", @"SwapEffectUpgradeEnable=\d;?", "");
                        if (v) s += "SwapEffectUpgradeEnable=1;";
                        using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KDx))
                        {
                            if (s == "") k.DeleteValue("DirectXUserGlobalSettings", false);
                            else k.SetValue("DirectXUserGlobalSettings", s, Microsoft.Win32.RegistryValueKind.String);
                        }
                    } },
            };

            // Placa de vídeo: nome e driver direto do registro do driver.
            try
            {
                using (Microsoft.Win32.RegistryKey cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                           @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                        try
                        {
                            using (Microsoft.Win32.RegistryKey k = cls.OpenSubKey(sub))
                            {
                                string n = k.GetValue("DriverDesc") as string;
                                if (n == null || n.Contains("Basic") || n.Contains("Virtual") || n.Contains("Parsec")) continue;
                                gpuName = n; gpuDriver = k.GetValue("DriverVersion") as string;
                                if (Regex.IsMatch(n, "AMD|Radeon|NVIDIA|GeForce", RegexOptions.IgnoreCase)) break;
                            }
                        }
                        catch { }
                    }
            }
            catch { }
            gpuAmd = gpuName != null && Regex.IsMatch(gpuName, "AMD|Radeon", RegexOptions.IgnoreCase);
            gpuNvidia = gpuName != null && Regex.IsMatch(gpuName, "NVIDIA|GeForce", RegexOptions.IgnoreCase);
            if (gpuAmd) { amdInstalada = AmdInstalada(); if (modo != Modo.Foto) ChecarDriverAmd(false); }
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\DVR"))
                    frameGenKey = k == null ? null : (k.GetValue("ToggleFrameGen") as string);
                if (frameGenKey != null) frameGenKey = frameGenKey.Replace(",", "+");
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\CN"))
                {
                    string dir = k == null ? null : k.GetValue("InstallDir") as string;
                    if (dir != null && File.Exists(Path.Combine(dir, "RadeonSoftware.exe"))) adrenalin = Path.Combine(dir, "RadeonSoftware.exe");
                }
            }
            catch { }
        }

        // ---- Driver da AMD: confere a página oficial da placa (no máximo a cada 12 h) ----
        string amdInstalada, amdRecomendada, amdOpcional;
        DateTime amdChecado = DateTime.MinValue;

        string AmdPagina()
        {
            Match m = Regex.Match(gpuName ?? "", @"RX\s*(\d)(\d{3})\s*(XTX|XT|GRE)?", RegexOptions.IgnoreCase);
            if (!m.Success) return "https://www.amd.com/en/support/download/drivers.html";
            string d = m.Groups[1].Value, modelo = "amd-radeon-rx-" + d + m.Groups[2].Value + (m.Groups[3].Success ? "-" + m.Groups[3].Value.ToLowerInvariant() : "");
            return "https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-" + d + "000-series/" + modelo + ".html";
        }

        static string AmdInstalada()
        {
            foreach (string k in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                try
                {
                    using (Microsoft.Win32.RegistryKey u = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(k))
                        foreach (string s in u.GetSubKeyNames())
                            using (Microsoft.Win32.RegistryKey e = u.OpenSubKey(s))
                                if ((e.GetValue("DisplayName") as string) == "AMD Software") return e.GetValue("DisplayVersion") as string;
                }
                catch { }
            return null;
        }

        // Versão nova em relação à instalada: "26.12.1 (recomendada)", "26.11.1 (opcional)" ou null.
        string DriverNovo()
        {
            Version inst, rec, opc;
            if (amdInstalada == null || !Version.TryParse(amdInstalada, out inst)) return null;
            if (amdRecomendada != null && Version.TryParse(amdRecomendada, out rec) && rec > inst) return amdRecomendada + " (recomendada)";
            if (amdOpcional != null && Version.TryParse(amdOpcional, out opc) && opc > inst) return amdOpcional + " (opcional)";
            return null;
        }

        async void ChecarDriverAmd(bool forcar)
        {
            if (!gpuAmd || (!forcar && (DateTime.Now - amdChecado).TotalHours < 12)) return;
            amdChecado = DateTime.Now;
            string url = AmdPagina();
            string[] r = await Task.Run(delegate
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Folego/" + Versao;
                    req.Timeout = 30000;
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    string html;
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream())) html = sr.ReadToEnd();
                    Match rec = Regex.Match(html, @"Adrenalin (\d+\.\d+(?:\.\d+)?) WHQL \(Recommended\)");
                    Match opc = Regex.Match(html, @"Adrenalin (\d+\.\d+(?:\.\d+)?) \(Optional\)");
                    return new[] { rec.Success ? rec.Groups[1].Value : null, opc.Success ? opc.Groups[1].Value : null };
                }
                catch { return null; }
            });
            if (r == null) { Engine.Log("Driver da AMD: não consegui conferir a versão nova"); return; }
            amdRecomendada = r[0]; amdOpcional = r[1];
            string nova = DriverNovo();
            if (nova != null && !Config.L("amd_avisado", "").Contains(nova))
            {
                Config.SetL("amd_avisado", new List<string> { nova });
                AddActivity(DateTime.Now, "Driver novo da AMD: " + nova, true);
            }
            Invalidate();
        }

        void DrawFps(Graphics g)
        {
            if (winOpts == null) BuildFps();
            float x0 = 116, W = ClientSize.Width - 24 - x0, half = (W - 16) / 2, top = 150, H1 = 262;
            RectangleF l1 = new RectangleF(x0, top, half, H1), r1 = new RectangleF(l1.Right + 16, top, half, H1);
            float top2 = l1.Bottom + 16, H2 = ClientSize.Height - 20 - top2;
            RectangleF es = new RectangleF(x0, top2, half, H2), b = new RectangleF(l1.Right + 16, top2, half, H2);

            Card(g, l1, "Windows pro jogo", GGame);
            float y = l1.Y + 52;
            foreach (RegOpt o in winOpts)
            {
                RegOpt opt = o;
                RectangleF row = new RectangleF(l1.X + 10, y, l1.Width - 20, 48);
                bool on = opt.Get();
                bool h = AddHit(row, "reg" + opt.Title, delegate
                {
                    try
                    {
                        bool now = !opt.Get();
                        opt.Set(now);
                        AddActivity(DateTime.Now, "Mais FPS: " + opt.Title + (now ? " ligado" : " desligado"), true);
                    }
                    catch (Exception ex) { Engine.Log("Erro ao mudar " + opt.Title + ": " + ex.Message); }
                    Invalidate();
                }, null);
                if (h) Fill(g, row, 12, Color.FromArgb(18, 255, 255, 255));
                Txt(g, opt.Title, Reg(10.5f), CText, new RectangleF(row.X + 10, y + 3, row.Width - 90, 22));
                Txt(g, opt.Desc, Reg(8.8f), CMuted, new RectangleF(row.X + 10, y + 25, row.Width - 90, 20));
                Toggle(g, new RectangleF(row.Right - 58, y + 12, 46, 24), on, h);
                y += 50;
            }

            Card(g, r1, "Placa de vídeo", GPulse);
            Txt(g, gpuName ?? "Não encontrada", Semi(15f), CText, new RectangleF(r1.X + 20, r1.Y + 54, r1.Width - 40, 30));
            if (gpuAmd && amdInstalada != null)
            {
                string nv = DriverNovo();
                string pre = "AMD Software " + amdInstalada + "  ·";
                float pw = Measure(g, pre, Reg(9.5f)) + 14; // a medida ignora espaços no fim: folga para não cortar
                Txt(g, pre, Reg(9.5f), CMuted, new RectangleF(r1.X + 20, r1.Y + 84, pw + 10, 20));
                Txt(g, nv != null ? "versão nova: " + nv : amdRecomendada != null ? "em dia" : "conferindo...", Reg(9.5f),
                    nv != null ? COrange : amdRecomendada != null ? CGood : CMuted, new RectangleF(r1.X + 20 + pw, r1.Y + 84, r1.Width - 40 - pw, 20));
            }
            else Txt(g, gpuDriver != null ? "Driver " + gpuDriver : "", Reg(9.5f), CMuted, new RectangleF(r1.X + 20, r1.Y + 84, r1.Width - 40, 20));
            float fy = r1.Y + 116;
            if (gpuAmd)
            {
                Txt(g, "Geração de quadros (AFMF): liga e desliga dentro do jogo com", Reg(9.8f), CText, new RectangleF(r1.X + 20, fy, r1.Width - 40, 22));
                float kx = r1.X + 20;
                foreach (string key in (frameGenKey ?? "Alt+Shift+G").Split('+'))
                {
                    float kw = Measure(g, key, Semi(9.5f)) + 20;
                    RectangleF kr = new RectangleF(kx, fy + 26, kw, 26);
                    Fill(g, kr, 7, Color.FromArgb(34, 255, 255, 255));
                    Stroke(g, kr, 7, Color.FromArgb(80, 255, 255, 255));
                    Txt(g, key, Semi(9.5f), CText, kr, StringAlignment.Center, StringAlignment.Center);
                    kx += kw + 6;
                }
                Txt(g, "Upscaling (RSR) e Anti-Lag: no AMD Adrenalin (passo a passo abaixo).", Reg(9.8f), CMuted, new RectangleF(r1.X + 20, fy + 62, r1.Width - 40, 22));
            }
            else
                Txt(g, gpuNvidia ? "Procure no painel da NVIDIA por DLSS, Frame Generation e NVIDIA Image Scaling."
                                 : "Procure no painel da sua placa por geração de quadros e upscaling.",
                    Reg(9.8f), CMuted, new RectangleF(r1.X + 20, fy, r1.Width - 40, 44), StringAlignment.Near, StringAlignment.Near, true);
            if (adrenalin != null)
            {
                RectangleF ab = new RectangleF(r1.X + 20, r1.Bottom - 54, 200, 36);
                bool ha = AddHit(ab, "adrenalin", delegate { try { Process.Start(adrenalin); } catch { } }, "Abre o painel da AMD");
                FillGrad(g, ab, 18, ha ? CLilac : CPurple, CBlue);
                Txt(g, "Abrir o AMD Adrenalin", Semi(10f), Color.White, ab, StringAlignment.Center, StringAlignment.Center);
            }
            if (gpuAmd)
            {
                string nova = DriverNovo();
                string bt = nova != null ? "Baixar a " + nova.Split(' ')[0] : "Site da AMD";
                float bw = Measure(g, bt, Semi(10f)) + 36;
                RectangleF db = new RectangleF(r1.X + 20 + (adrenalin != null ? 208 : 0), r1.Bottom - 54, bw, 36);
                bool hd = AddHit(db, "amdsite", delegate { OpenUrl(AmdPagina()); }, "Abre a página oficial de drivers da sua placa");
                if (nova != null) FillGrad(g, db, 18, hd ? Color.FromArgb(255, 180, 110) : COrange, CBad);
                else Fill(g, db, 18, Color.FromArgb(hd ? 50 : 28, 255, 255, 255));
                if (nova == null) Stroke(g, db, 18, Color.FromArgb(60, 255, 255, 255));
                Txt(g, bt, Semi(10f), nova != null ? Color.White : CText, db, StringAlignment.Center, StringAlignment.Center);
            }

            // Escala nítida
            Card(g, es, "Escala nítida", GSpeed);
            RectangleF badge = new RectangleF(es.X + 170, es.Y + 19, 86, 22);
            Stroke(g, badge, 11, Color.FromArgb(120, COrange));
            Txt(g, "experimental", Reg(8.5f), COrange, badge, StringAlignment.Center, StringAlignment.Center);
            Txt(g, "Jogue numa janela menor (mais leve, mais FPS) e o Fôlego estica para a tela toda. Saiu do jogo, desliga sozinha.",
                Reg(9.5f), CMuted, new RectangleF(es.X + 20, es.Y + 50, es.Width - 40, 40), StringAlignment.Near, StringAlignment.Near, true);
            Txt(g, "Liga e desliga com o jogo na frente:", Reg(10f), CText, new RectangleF(es.X + 20, es.Y + 100, 260, 28), StringAlignment.Near, StringAlignment.Center);
            float kx2 = es.X + 262;
            foreach (string key in new[] { "Ctrl", "Alt", "F" })
            {
                float kw = Measure(g, key, Semi(9.5f)) + 20;
                RectangleF kr = new RectangleF(kx2, es.Y + 101, kw, 26);
                Fill(g, kr, 7, Color.FromArgb(34, 255, 255, 255));
                Stroke(g, kr, 7, Color.FromArgb(80, 255, 255, 255));
                Txt(g, key, Semi(9.5f), CText, kr, StringAlignment.Center, StringAlignment.Center);
                kx2 += kw + 6;
            }
            float sx = es.X + 20;
            string[] modos = { "Pixel nítido", "Suave" };
            for (int i = 0; i < 2; i++)
            {
                bool nit = i == 0, on = opts["escala_nitido"] == nit;
                float w = Measure(g, modos[i], Reg(9.2f)) + 26;
                RectangleF r = new RectangleF(sx, es.Y + 142, w, 30);
                bool h = AddHit(r, "esc" + i, delegate { if (opts["escala_nitido"] != nit) ToggleOpt("escala_nitido"); },
                    nit ? "Multiplica os pixels: imagem bem definida" : "Estica com suavização: imagem mais macia");
                if (on) FillGrad(g, r, 15, CPurple, CBlue); else Fill(g, r, 15, Color.FromArgb(h ? 40 : 22, 255, 255, 255));
                if (!on) Stroke(g, r, 15, Color.FromArgb(50, 255, 255, 255));
                Txt(g, modos[i], Reg(9.2f), on ? Color.White : CText, r, StringAlignment.Center, StringAlignment.Center);
                sx += w + 6;
            }
            string est; Color ec;
            if (Escala.Ligada) { est = string.Format(Pt, "● Ligada no {0} · {1:0.#}×", Bonito(Escala.Jogo), Escala.Fator); ec = CGood; }
            else if (escMsg != null) { est = escMsg; ec = COrange; }
            else { est = "Desligada"; ec = CFaint; }
            Txt(g, est, Reg(9.5f), ec, new RectangleF(es.X + 20, es.Y + 186, es.Width - 40, 44), StringAlignment.Near, StringAlignment.Near, true);

            Card(g, b, gpuAmd ? "Como ligar no AMD Adrenalin" : "Dicas", GInfo);
            string[] passos = gpuAmd
                ? new[] {
                    "Abra o Adrenalin (botão acima) ou aperte Alt+R",
                    "Vá em Jogos → Gráficos (ou escolha o Roblox)",
                    "Ligue AMD Fluid Motion Frames: o FPS quase dobra",
                    "Ligue Radeon Anti-Lag junto, para não atrasar",
                    "Opcional: Radeon Super Resolution" }
                : new[] {
                    "Deixe o driver da placa sempre atualizado",
                    "No jogo, prefira tela cheia ou janela sem borda",
                    "Use o Modo Jogo do Fôlego" };
            float py = b.Y + 52;
            for (int i = 0; i < passos.Length; i++)
            {
                RectangleF n = new RectangleF(b.X + 20, py + 5, 22, 22);
                FillGrad(g, n, 11, CPurple, CBlue);
                Txt(g, (i + 1).ToString(), Semi(9f), Color.White, n, StringAlignment.Center, StringAlignment.Center);
                Txt(g, passos[i], Reg(9.8f), CText, new RectangleF(b.X + 52, py, b.Width - 70, 32), StringAlignment.Near, StringAlignment.Center);
                py += 40;
            }
        }

        // ---- Escala nítida: atalho Ctrl+Alt+F (funciona com o Fôlego escondido) ----
        string escMsg;
        Timer escTimer;
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int HotkeyEscala = 0xF01;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyEscala) ToggleEscala(); // WM_HOTKEY
            base.WndProc(ref m);
        }

        void ToggleEscala()
        {
            if (Escala.Ligada)
            {
                Escala.Desligar();
                if (escTimer != null) escTimer.Stop();
                AddActivity(DateTime.Now, "Escala nítida desligada", true);
            }
            else
            {
                escMsg = Escala.Ligar(opts["escala_nitido"], Handle);
                if (escMsg == null)
                {
                    AddActivity(DateTime.Now, string.Format(Pt, "Escala nítida: {0} esticado {1:0.#}×", Bonito(Escala.Jogo), Escala.Fator), true);
                    if (escTimer == null)
                    {
                        escTimer = new Timer { Interval = 200 };
                        escTimer.Tick += delegate
                        {
                            if (!Escala.PrecisaDesligar()) return;
                            Escala.Desligar();
                            escTimer.Stop();
                            AddActivity(DateTime.Now, "Escala nítida desligada (saiu do jogo)", true);
                            Invalidate();
                        };
                    }
                    escTimer.Start();
                }
                else Engine.Log("Escala nítida: " + escMsg);
            }
            Invalidate();
        }

        // ---------------- Ajustes ----------------
        void DrawSettings(Graphics g)
        {
            float x0 = 116, W = ClientSize.Width - 24 - x0, half = (W - 16) / 2, top = 150;
            RectangleF l = new RectangleF(x0, top, half, 348), r = new RectangleF(l.Right + 16, top, half, 348);
            RectangleF s = new RectangleF(x0, l.Bottom + 16, W, ClientSize.Height - 20 - l.Bottom - 16);

            Card(g, l, "Dar fôlego", GBroom);
            float y = l.Y + 52;
            OptionRow(g, l, ref y, "temp", "Limpar arquivos temporários", "Só os com mais de 1 dia; os em uso ficam");
            OptionRow(g, l, ref y, "dns", "Limpar cache de DNS", "Ajuda quando um site não abre");
            OptionRow(g, l, ref y, "lixeira", "Esvaziar a lixeira", "Cuidado: não dá para desfazer");
            OptionRow(g, l, ref y, "auto", "Automático", "Dá fôlego sozinho quando a RAM passa de 85%");
            PeriodRow(g, l, ref y);

            Card(g, r, "Aparência e sistema", GSettings);
            y = r.Y + 52;
            OptionRow(g, r, ref y, "fundo_wall", "Papel de parede no fundo", "O do Windows, desfocado; desligado fica roxo");
            {
                RectangleF row = new RectangleF(r.X + 10, y, r.Width - 20, 48);
                bool hi = AddHit(row, "inicio", delegate { SetInicio(!inicioAtivo); }, null);
                if (hi) Fill(g, row, 12, Color.FromArgb(18, 255, 255, 255));
                Txt(g, "Abrir com o Windows", Reg(10.5f), CText, new RectangleF(row.X + 10, y + 3, row.Width - 90, 22));
                Txt(g, inicioMsg ?? "Abre escondido no cantinho, já no modo forte",
                    Reg(8.8f), inicioMsg != null ? COrange : CMuted, new RectangleF(row.X + 10, y + 25, row.Width - 90, 20));
                Toggle(g, new RectangleF(row.Right - 58, y + 12, 46, 24), inicioAtivo, hi);
                y += 50;
            }
            bool admin = Engine.IsAdmin();
            ActionRow(g, r, ref y, "sadmin", "Modo forte", admin ? "Limpa também o cache de memória e o Temp do Windows" : "Limpa mais, mas pede permissão de administrador",
                admin ? "Ligado" : "Ligar", admin ? null : (Action)RelaunchAdmin);
            ActionRow(g, r, ref y, "slog", "Registro de atividades", "Tudo que o Fôlego fez, com data e hora", "Abrir", OpenLog);
            if (novaVersao != null)
                ActionRow(g, r, ref y, "supd", "Versão " + Versao + " → " + novaVersao, updMsg ?? "Versão nova! As novidades estão no GitHub",
                    atualizando ? "Atualizando..." : "Atualizar", atualizando ? null : (Action)Atualizar, COrange);
            else
                ActionRow(g, r, ref y, "sgit", "Versão " + Versao, updChecado > DateTime.MinValue ? "Em dia · conferido às " + updChecado.ToString("HH:mm") : "Novidades e downloads no GitHub",
                    "GitHub", delegate { OpenUrl(Repo); });

            RectangleF sa = new RectangleF(s.X, s.Y, half, s.Height), sc = new RectangleF(s.X + half + 16, s.Y, half, s.Height);
            Card(g, sa, null, null);
            if (appIcon != null) g.DrawImage(appIcon, sa.X + 24, sa.Y + (sa.Height - 64) / 2, 64, 64);
            Txt(g, "Fôlego", Semi(18f), CText, new RectangleF(sa.X + 104, sa.Y + sa.Height / 2 - 44, 300, 36));
            Txt(g, "Dá um fôlego pro PC: libera RAM, limpa lixo e prepara o Windows pro jogo, sem fechar nada que importa.",
                Reg(9.5f), CMuted, new RectangleF(sa.X + 106, sa.Y + sa.Height / 2 - 6, sa.Width - 126, 60), StringAlignment.Near, StringAlignment.Near, true);

            // No celular
            Card(g, sc, "No celular", GPhone);
            RectangleF ctg = new RectangleF(sc.Right - 66, sc.Y + 17, 46, 24);
            Toggle(g, ctg, opts["celular"], AddHit(ctg, "tgcel", delegate { ToggleOpt("celular"); }, opts["celular"] ? "Desligar o acesso pelo celular" : "Ligar o acesso pelo celular"));
            bool demoCel = modo == Modo.Foto && Environment.GetCommandLineArgs().Contains("--demo");
            if ((remoto != null && remoto.Rodando) || demoCel)
            {
                string ip = Remoto.IpLocal() ?? "IP do PC";
                string cod = remoto != null ? (remoto.Codigo ?? "") : "123456";
                string url = "http://" + ip + ":" + Remoto.Porta;
                // QR code: já leva o código, então o celular abre e conecta sozinho.
                float qs = Math.Min(sc.Height - 62, 140);
                RectangleF qr = new RectangleF(sc.Right - 20 - qs, sc.Y + 48, qs, qs);
                string qrTxt = url + "/#c=" + cod;
                if (qrTxt != qrTexto) { if (qrImg != null) qrImg.Dispose(); qrImg = null; try { qrImg = Qr.Imagem(qrTxt, 400); } catch { } qrTexto = qrTxt; }
                if (qrImg != null)
                {
                    Fill(g, qr, 10, Color.White);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(qrImg, RectangleF.Inflate(qr, -3, -3));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                }
                float lw = qr.X - sc.X - 36;
                Txt(g, "Aponte a câmera do celular para o QR, ou abra no navegador (mesmo Wi-Fi):", Reg(8.8f), CMuted, new RectangleF(sc.X + 20, sc.Y + 48, lw, 34), StringAlignment.Near, StringAlignment.Near, true);
                Txt(g, url, Semi(11.5f), CLilac, new RectangleF(sc.X + 20, sc.Y + 82, lw, 24));
                Txt(g, "e digite o código:", Reg(8.8f), CMuted, new RectangleF(sc.X + 20, sc.Y + 106, lw, 18));
                Txt(g, cod.Length == 6 ? cod.Substring(0, 3) + " " + cod.Substring(3) : cod, Semi(18f), CText, new RectangleF(sc.X + 18, sc.Y + 122, lw, 34));
                Font bf = Reg(8.8f);
                string b1 = "Novo código", b2 = "Desconectar (" + tokens.Count + ")";
                float w1 = Measure(g, b1, bf) + 22, w2 = Measure(g, b2, bf) + 22;
                RectangleF r1b = new RectangleF(sc.X + 20, sc.Bottom - 38, w1, 26), r2b = new RectangleF(sc.X + 20 + w1 + 6, sc.Bottom - 38, w2, 26);
                bool h1 = AddHit(r1b, "celcod", delegate { if (remoto != null) remoto.NovoCodigo(); Invalidate(); }, "Gera outro código");
                bool h2 = AddHit(r2b, "celout", delegate { tokens.Clear(); Config.SetL("tokens", tokens); AddActivity(DateTime.Now, "Celulares desconectados", true); Invalidate(); },
                    "Os celulares precisam do código de novo");
                Fill(g, r1b, 14, Color.FromArgb(h1 ? 50 : 26, 255, 255, 255)); Txt(g, b1, bf, CText, r1b, StringAlignment.Center, StringAlignment.Center);
                Fill(g, r2b, 14, Color.FromArgb(h2 ? 50 : 26, 255, 255, 255)); Txt(g, b2, bf, CText, r2b, StringAlignment.Center, StringAlignment.Center);
            }
            else
                Txt(g, celMsg ?? "Controle o Fôlego pelo celular, no Wi-Fi de casa (fora de casa, pelo Tailscale). Pede o modo forte, para liberar no firewall só em redes privadas.",
                    Reg(9.5f), celMsg != null ? COrange : CMuted, new RectangleF(sc.X + 20, sc.Y + 52, sc.Width - 40, 90), StringAlignment.Near, StringAlignment.Near, true);
        }

        // ---------------- Internet: ping a cada 5 s (Cloudflare 1.1.1.1), últimas 12 medidas ----------------
        readonly List<long> pings = new List<long>(); // -1 = sem resposta
        bool pingando;

        async void MedirPing()
        {
            if (pingando) return;
            pingando = true;
            long ms = await Task.Run(delegate
            {
                try
                {
                    using (System.Net.NetworkInformation.Ping p = new System.Net.NetworkInformation.Ping())
                    {
                        System.Net.NetworkInformation.PingReply r = p.Send("1.1.1.1", 2000);
                        return r.Status == System.Net.NetworkInformation.IPStatus.Success ? r.RoundtripTime : -1L;
                    }
                }
                catch { return -1L; }
            });
            pings.Add(ms);
            if (pings.Count > 12) pings.RemoveAt(0);
            pingando = false;
        }

        void MedirPingSync() // só para a foto (testes)
        {
            try { using (System.Net.NetworkInformation.Ping p = new System.Net.NetworkInformation.Ping()) { var r = p.Send("1.1.1.1", 2000); pings.Add(r.Status == System.Net.NetworkInformation.IPStatus.Success ? r.RoundtripTime : -1); } }
            catch { pings.Add(-1); }
        }

        string PingResumo() { string t; Color c; PingTexto(out t, out c); return t; }

        void PingTexto(out string texto, out Color cor)
        {
            if (pings.Count == 0) { texto = "medindo a internet..."; cor = CFaint; return; }
            long ult = pings[pings.Count - 1];
            List<long> ok = pings.Where(p => p >= 0).ToList();
            if (ult < 0) { texto = ok.Count == 0 ? "sem internet" : "internet falhando"; cor = CBad; return; }
            double media = ok.Average(), desvio = Math.Sqrt(ok.Average(p => (p - media) * (p - media)));
            bool perdas = ok.Count < pings.Count, oscila = desvio > 25 || perdas;
            texto = "ping " + ult + " ms" + (oscila ? " · oscilando" : "");
            cor = oscila || ult >= 100 ? COrange : ult >= 60 ? CWarnSoft : CGood;
        }

        // ---------------- Histórico de RAM (últimas 2 h, uma amostra a cada 30 s) ----------------
        readonly List<float> hist = new List<float>();
        readonly List<DateTime> histT = new List<DateTime>();
        const int HistMax = 240;

        void AmostraRam()
        {
            hist.Add((float)ramTarget); histT.Add(DateTime.Now);
            if (hist.Count > HistMax) { hist.RemoveAt(0); histT.RemoveAt(0); }
        }

        void DrawHistorico(Graphics g, RectangleF r)
        {
            Fill(g, r, 8, Color.FromArgb(14, 255, 255, 255));
            if (hist.Count < 2) { Txt(g, "juntando dados...", Reg(8f), CFaint, r, StringAlignment.Center, StringAlignment.Center); return; }
            float lo = Math.Max(0f, Math.Min(0.5f, hist.Min() - 0.05f)); // escala começa perto do menor valor: mostra a variação
            float y85 = r.Bottom - 3 - (0.85f - lo) / (1f - lo) * (r.Height - 6);
            using (Pen p85 = new Pen(Color.FromArgb(80, CBad)) { DashStyle = DashStyle.Dash }) g.DrawLine(p85, r.X + 4, y85, r.Right - 4, y85);
            DateTime fim = DateTime.Now, ini = fim.AddMinutes(-120);
            Func<DateTime, float> X = t => r.X + 4 + (float)((t - ini).TotalMinutes / 120.0) * (r.Width - 8);
            Func<float, float> Y = v => r.Bottom - 3 - Math.Max(0, Math.Min(1, (v - lo) / (1f - lo))) * (r.Height - 6);
            List<PointF> pts = new List<PointF>();
            for (int i = 0; i < hist.Count; i++) pts.Add(new PointF(Math.Max(r.X + 4, X(histT[i])), Y(hist[i])));
            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLines(pts.ToArray());
                area.AddLine(pts[pts.Count - 1], new PointF(pts[pts.Count - 1].X, r.Bottom - 2));
                area.AddLine(new PointF(pts[pts.Count - 1].X, r.Bottom - 2), new PointF(pts[0].X, r.Bottom - 2));
                area.CloseFigure();
                using (LinearGradientBrush br = new LinearGradientBrush(r, Color.FromArgb(90, CPurple), Color.FromArgb(10, CBlue), 90f)) g.FillPath(br, area);
            }
            using (Pen ln = new Pen(CLilac, 1.6f) { LineJoin = LineJoin.Round }) g.DrawLines(ln, pts.ToArray());
            // Pontinhos onde teve fôlego.
            using (SolidBrush dot = new SolidBrush(CTeal))
                foreach (Act a in acts)
                    if (a.T > ini && a.Text.Contains("iberou")) { float x = X(a.T); g.FillEllipse(dot, x - 3, r.Bottom - 9, 6, 6); }
        }

        // ---------------- Aviso de programa pesado ----------------
        // Passou de 1,5 GB: uma linha na Atividade (sem janela). Só avisa de novo depois que ele baixar de 1,2 GB.
        const long LimitePesado = 1536L * 1048576;
        readonly HashSet<string> avisados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async void CheckPesados()
        {
            if (!opts["aviso_pesado"]) return;
            List<Engine.Consumidor> top = await Task.Run(() => Engine.TopConsumers(8));
            foreach (Engine.Consumidor c in top)
            {
                if (c.Bytes > LimitePesado && avisados.Add(c.Nome))
                    AddActivity(DateTime.Now, string.Format(Pt, "Pesado: {0} está usando {1:0.0} GB (no Início, × fecha)", Bonito(c.Nome), c.Bytes / 1073741824.0), true);
                else if (c.Bytes < LimitePesado * 0.8) avisados.Remove(c.Nome);
            }
            Invalidate();
        }

        // ---------------- Atualização dentro do app (Releases do GitHub) ----------------
        string novaVersao, novaUrl, updMsg;
        bool atualizando;
        DateTime updChecado = DateTime.MinValue;
        static readonly bool testeUpd = Environment.GetCommandLineArgs().Contains("--testar-atualizacao");

        async void ChecarAtualizacao()
        {
            Dictionary<string, object> rel = await Task.Run(delegate
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/anthonymb1204/folego/releases/latest");
                    req.UserAgent = "Folego/" + Versao;
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = 20000;
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                        return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(sr.ReadToEnd());
                }
                catch { return null; }
            });
            if (rel == null) return;
            updChecado = DateTime.Now;
            string tag = (Convert.ToString(rel["tag_name"]) ?? "").TrimStart('v');
            Version nv, atual;
            if (!Version.TryParse(tag, out nv) || !Version.TryParse(Versao, out atual) || nv <= atual) { novaVersao = null; Invalidate(); return; }
            string url = null;
            System.Collections.ArrayList assets = rel["assets"] as System.Collections.ArrayList;
            if (assets != null)
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a != null && Convert.ToString(a["name"]) == "Folego.exe") url = Convert.ToString(a["browser_download_url"]);
                }
            if (url == null) return;
            if (novaVersao != tag) AddActivity(DateTime.Now, "Versão nova do Fôlego: " + tag + " (Ajustes → Atualizar)", true);
            novaVersao = tag; novaUrl = url;
            Invalidate();
        }

        // Baixa a versão nova, confere que é ela mesma, troca o .exe (depois que este fechar) e abre de novo.
        async void Atualizar()
        {
            if (novaUrl == null || atualizando) return;
            if (currentGame != null) { updMsg = "Feche o jogo primeiro: não atualizo no meio da partida."; Invalidate(); return; }
            atualizando = true; updMsg = "Baixando a " + novaVersao + "..."; Invalidate();
            string versao = novaVersao, tmp = Path.Combine(Path.GetTempPath(), "Folego_" + versao + ".exe");
            bool ok = await Task.Run(delegate
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "Folego/" + Versao;
                        wc.DownloadFile(novaUrl, tmp);
                    }
                    return FileVersionInfo.GetVersionInfo(tmp).ProductVersion == versao;
                }
                catch { return false; }
            });
            if (!ok) { atualizando = false; updMsg = "Não deu para baixar. Tente de novo mais tarde."; Invalidate(); return; }

            string exe = Application.ExecutablePath, cmd = Path.Combine(Path.GetTempPath(), "folego_atualizar.cmd");
            int pid = Process.GetCurrentProcess().Id;
            File.WriteAllText(cmd,
                "@echo off\r\n" +
                ":espera\r\n" +
                "tasklist /fi \"PID eq " + pid + "\" | find \"" + pid + "\" >nul && (timeout /t 1 /nobreak >nul & goto espera)\r\n" +
                "copy /y \"" + tmp + "\" \"" + exe + "\" >nul\r\n" +
                "start \"\" \"" + exe + "\"" + (Visible ? "" : " --bandeja") + "\r\n" +
                "del \"" + tmp + "\"\r\n" +
                "del \"%~f0\"\r\n", System.Text.Encoding.Default);
            AddActivity(DateTime.Now, "Atualizando o Fôlego para a " + versao, true);
            if (remoto != null) remoto.Parar(); // solta a porta do celular para a versão nova
            // UseShellExecute: o cmd não herda nada deste processo (com herança, ele segurava a porta do celular).
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            exiting = true;
            Close();
        }

        // ---------------- Abrir com o Windows ----------------
        // Com admin: tarefa agendada no logon com privilégio alto (abre no modo forte sem pedir permissão).
        // Sem admin: entrada "Run" do usuário (abre sem modo forte).
        const string Tarefa = "Folego";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        bool inicioAtivo;
        string inicioMsg;

        static int Oculto(string exe, string args)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false,
                           WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        void LerInicio()
        {
            bool run = RegStr(RunKey, "Folego") != null;
            inicioAtivo = run || Oculto("schtasks.exe", "/query /tn \"" + Tarefa + "\"") == 0;
        }

        void SetInicio(bool on)
        {
            inicioMsg = null;
            string exe = Application.ExecutablePath;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue("Folego", false);
                if (Engine.IsAdmin()) Oculto("schtasks.exe", "/delete /f /tn \"" + Tarefa + "\"");
                if (on)
                {
                    bool ok = Engine.IsAdmin() && Oculto("schtasks.exe", "/create /f /tn \"" + Tarefa + "\" /sc onlogon /rl highest /tr \"\\\"" + exe + "\\\" --bandeja\"") == 0;
                    if (!ok)
                    {
                        using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                            k.SetValue("Folego", "\"" + exe + "\" --bandeja");
                        if (!Engine.IsAdmin()) inicioMsg = "Vai abrir sem o modo forte (ligue o modo forte e ative de novo)";
                    }
                }
                AddActivity(DateTime.Now, on ? "Abrir com o Windows: ligado" : "Abrir com o Windows: desligado", true);
            }
            catch (Exception ex) { Engine.Log("Abrir com o Windows: " + ex.Message); }
            LerInicio();
            Invalidate();
        }

        // ---------------- Celular ----------------
        Remoto remoto;
        List<string> tokens;
        string celMsg;
        byte[] iconePng;
        string qrTexto;
        Bitmap qrImg;

        string Saudacao()
        {
            int hr = DateTime.Now.Hour;
            return hr < 5 ? "Boa noite" : hr < 12 ? "Bom dia" : hr < 18 ? "Boa tarde" : "Boa noite";
        }

        static string NomeUsuario()
        {
            string n = Environment.UserName;
            return n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : n;
        }

        void StartCelular()
        {
            celMsg = null;
            if (remoto != null && remoto.Rodando) return;
            if (!Engine.IsAdmin()) { celMsg = "Precisa do modo forte (o escudo no menu) para liberar o Fôlego no firewall."; Invalidate(); return; }
            if (!Remoto.LiberarFirewall()) Engine.Log("Celular: não consegui criar a regra do firewall");
            remoto = new Remoto(this)
            {
                Estado = EstadoCelular,
                Acao = AcaoCelular,
                TokenValido = t => !string.IsNullOrEmpty(t) && tokens.Contains(t),
                Icone = () =>
                {
                    if (iconePng == null && Icon != null)
                        using (Icon ic = new Icon(Icon, 256, 256))
                        using (Bitmap b = new Bitmap(192, 192))
                        using (MemoryStream ms = new MemoryStream())
                        {
                            using (Graphics g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawIcon(ic, new Rectangle(0, 0, 192, 192)); }
                            b.Save(ms, ImageFormat.Png);
                            iconePng = ms.ToArray();
                        }
                    return iconePng;
                },
                Fundo = () =>
                {
                    if (bg == null) { wpStamp = WallStamp(); bg = BuildBackground(ClientSize); }
                    using (MemoryStream ms = new MemoryStream()) { bg.Save(ms, ImageFormat.Jpeg); return ms.ToArray(); }
                },
            };
            try
            {
                remoto.Iniciar();
                Engine.Log("Celular: ligado em http://" + (Remoto.IpLocal() ?? "?") + ":" + Remoto.Porta);
            }
            catch (Exception ex) { celMsg = "Não deu para ligar: a porta " + Remoto.Porta + " está ocupada."; Engine.Log("Celular: " + ex.Message); remoto = null; }
            Invalidate();
        }

        void StopCelular()
        {
            if (remoto != null) remoto.Parar();
            remoto = null;
            celMsg = null;
            if (Engine.IsAdmin()) Remoto.FecharFirewall();
            Engine.Log("Celular: desligado");
            Invalidate();
        }

        static string Hex(Color c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); }

        Dictionary<string, object> EstadoCelular()
        {
            string st;
            if (currentGame != null) st = "● " + (gameStatus ?? Bonito(currentGame) + " aberto");
            else if (opts["jogo"]) st = "Esperando " + string.Join(", ", jogos.Select(Bonito)) + " abrir";
            else st = "Desligado";
            return new Dictionary<string, object> {
                { "pc", Environment.MachineName }, { "nome", NomeUsuario() }, { "saudacao", Saudacao() },
                { "hora", DateTime.Now.ToString("HH:mm") }, { "data", DateTime.Now.ToString("dddd, d 'de' MMMM", Pt) },
                { "ram", ramTarget }, { "ramLivre", ramFree }, { "ramTotal", ramTotal }, { "cpu", cpuTarget },
                { "discoLivre", diskFree }, { "discoUso", diskTotal > 0 ? 1 - diskFree / diskTotal : 0 },
                { "liberado", freedTodayMB }, { "vezes", runsToday }, { "ocupado", busy },
                { "msg", lastMsg }, { "msgOk", lastMsgColor == CGood },
                { "opts", opts.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "periodo", periodo }, { "periodoTxt", PeriodoNome(periodo) }, { "proximo", periodo > 0 ? nextRun.ToString("HH:mm") : "" },
                { "jogo", currentGame != null }, { "jogoStatus", st },
                { "internet", PingResumo() },
                { "top", consumers.Select(c => new Dictionary<string, object> { { "nome", c.Nome }, { "mb", c.Bytes / 1048576.0 } }).ToList() },
                { "atividades", acts.AsEnumerable().Reverse().Take(8).Select(a => new Dictionary<string, object> {
                    { "hora", a.T.ToString("HH:mm") }, { "texto", a.Text }, { "cor", Hex(a.C) } }).ToList() },
            };
        }

        static readonly string[] OpcoesCelular = { "temp", "dns", "lixeira", "auto", "jogo", "jogo_ram", "jogo_prio", "jogo_reabrir", "jogo_acordado" };

        object AcaoCelular(string acao, Dictionary<string, object> d)
        {
            Dictionary<string, object> ok = new Dictionary<string, object> { { "ok", true } };
            object v;
            switch (acao)
            {
                case "novo_token":
                    tokens.Add(Convert.ToString(d["token"]));
                    Config.SetL("tokens", tokens);
                    AddActivity(DateTime.Now, "Celular conectado", true);
                    Invalidate();
                    return ok;
                case "sair":
                    if (d.TryGetValue("_token", out v)) { tokens.Remove(Convert.ToString(v)); Config.SetL("tokens", tokens); }
                    Invalidate();
                    return ok;
                case "folego":
                    RunOptimize("Celular");
                    return ok;
                case "opcao":
                    {
                        string k = d.TryGetValue("chave", out v) ? Convert.ToString(v) : "";
                        if (!OpcoesCelular.Contains(k) || !d.TryGetValue("valor", out v)) return Erro("opção inválida");
                        if (opts[k] != Convert.ToBoolean(v)) ToggleOpt(k);
                        return ok;
                    }
                case "periodo":
                    {
                        int min = d.TryGetValue("min", out v) ? Convert.ToInt32(v) : -1;
                        if (!Periodos.Contains(min)) return Erro("intervalo inválido");
                        SetPeriodo(min);
                        return ok;
                    }
                case "fechar":
                    {
                        string nome = d.TryGetValue("nome", out v) ? Convert.ToString(v) : "";
                        if (!consumers.Any(c => c.Nome == nome)) return Erro("Esse programa não está na lista.");
                        int n = Engine.Kill(nome);
                        if (n == 0) return Erro("Não deu para fechar " + nome + ".");
                        AddActivity(DateTime.Now, "Fechou " + nome + " (pelo celular)", true);
                        RefreshList();
                        return ok;
                    }
                case "inicializacao_lista":
                    return new Dictionary<string, object> { { "ok", true }, { "itens", Engine.StartupItems().Select(i => new Dictionary<string, object> {
                        { "nome", i.Nome }, { "ligado", i.Ligado }, { "todos", i.Todos }, { "cmd", i.Comando } }).ToList() } };
                case "inicializacao":
                    {
                        string nome = d.TryGetValue("nome", out v) ? Convert.ToString(v) : "";
                        bool ligado = d.TryGetValue("ligado", out v) && Convert.ToBoolean(v);
                        Engine.StartupItem it = Engine.StartupItems().FirstOrDefault(i => i.Nome == nome);
                        if (it == null) return Erro("Não achei esse programa.");
                        try { Engine.SetStartup(it, ligado); }
                        catch { return Erro("Esse abre para todos os usuários: ligue o modo forte no PC."); }
                        AddActivity(DateTime.Now, "Inicialização: " + nome + (ligado ? " ligado" : " desligado") + " (pelo celular)", true);
                        LoadStartup();
                        return ok;
                    }
            }
            return Erro("ação desconhecida");
        }

        static Dictionary<string, object> Erro(string msg) { return new Dictionary<string, object> { { "ok", false }, { "erro", msg } }; }

        // ---------------- Mouse ----------------
        Hit HitAt(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Hit h = HitAt(e.Location);
            string id = h == null ? null : h.Id;
            Cursor want = h != null && h.Click != null ? Cursors.Hand : Cursors.Default;
            if (Cursor != want) Cursor = want;
            if (id != hoverId) { hoverId = id; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverId != null) { hoverId = null; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Hit h = HitAt(e.Location);
            if (h != null && h.Click != null) h.Click();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (page != 1) return;
            startScroll -= e.Delta / 120f * 58 * 2;
            if (startScroll < 0) startScroll = 0;
            Invalidate();
            BeginInvoke((MethodInvoker)delegate { OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, PointToClient(Cursor.Position).X, PointToClient(Cursor.Position).Y, 0)); });
        }

        // ---------------- Animação e números ----------------
        void StartAnim() { if (anim != null && !anim.Enabled) anim.Start(); }

        void Animate()
        {
            ramDisp += (ramTarget - ramDisp) * 0.18;
            if (busy) spin = (spin + 14) % 360;
            bool moving = Math.Abs(ramTarget - ramDisp) > 0.003;
            if (!moving) ramDisp = ramTarget;
            if (Visible && WindowState != FormWindowState.Minimized) Invalidate();
            if (!moving && !busy) anim.Stop();
        }

        void ReadStats()
        {
            Native.MEMORYSTATUSEX m = Native.Mem();
            ramTotal = m.ullTotalPhys / 1073741824.0;
            ramFree = m.ullAvailPhys / 1073741824.0;
            ramTarget = m.dwMemoryLoad / 100.0;
            long idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                long sys = (kernel - lastKernel) + (user - lastUser), id = idle - lastIdle;
                if (sys > 0) cpuTarget = Math.Max(0, 1.0 - (double)id / sys);
                lastIdle = idle; lastKernel = kernel; lastUser = user;
            }
            try
            {
                DriveInfo c = new DriveInfo("C");
                diskFree = c.AvailableFreeSpace / 1073741824.0;
                diskTotal = c.TotalSize / 1073741824.0;
            }
            catch { }

            if (modo != Modo.Foto && opts["auto"] && !busy && m.dwMemoryLoad >= 85 && (DateTime.Now - lastAuto).TotalMinutes >= 3)
            {
                lastAuto = DateTime.Now;
                RunOptimize("Automático");
            }
        }

        // ---------------- Opções ----------------
        void ToggleOpt(string k)
        {
            if (k == "anti_afk" && !opts[k] && MessageBox.Show(this,
                    "O anti-AFK dá um passinho pra frente e outro igual pra trás no jogo (W e S) a cada ~15 minutos, quando você está parado, para o Roblox não te derrubar por inatividade.\n\nIsso é contra as regras do Roblox: sua conta pode ser punida. Ligar mesmo assim?",
                    "Fôlego: anti-AFK", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            opts[k] = !opts[k];
            Config.SetB(k, opts[k]);
            Engine.Log(k + ": " + (opts[k] ? "ligado" : "desligado"));
            if (k == "fundo_wall" && !bgBuilding) { if (bg != null) bg.Dispose(); bg = null; }
            if (k == "jogo" && !opts[k] && currentGame != null) EndGame();
            if (k == "celular") { if (opts[k]) StartCelular(); else StopCelular(); }
            if (k == "jogo_acordado") Acordado(opts[k] && currentGame != null);
            Invalidate();
        }

        void ResetSchedule() { nextRun = periodo > 0 ? DateTime.Now.AddMinutes(periodo) : DateTime.MaxValue; }

        void SetPeriodo(int min)
        {
            periodo = min;
            Config.SetI("periodo", min);
            ResetSchedule();
            Engine.Log("Fôlego programado: " + (min > 0 ? "a cada " + PeriodoNome(min) : "desligado"));
            Invalidate();
        }

        static string PeriodoNome(int min) { int i = Array.IndexOf(Periodos, min); return i >= 0 ? PeriodoNomes[i] : min + " min"; }

        void SyncJogos() { Engine.Jogos = new HashSet<string>(jogos, StringComparer.OrdinalIgnoreCase); }

        void RemoveItem(List<string> data, string cfgKey, string item)
        {
            data.RemoveAll(x => x.Equals(item, StringComparison.OrdinalIgnoreCase));
            Config.SetL(cfgKey, data); SyncJogos(); Invalidate();
        }

        void ShowAddMenu(List<string> data, string cfgKey)
        {
            ContextMenuStrip menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
                BackColor = Color.FromArgb(30, 27, 48), ForeColor = CText, Font = Reg(10f), ShowImageMargin = true };
            List<string> apps = Engine.WindowedApps().Where(a => !data.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (string a in apps)
            {
                string app = a;
                Bitmap ic = IconForProcess(app);
                ToolStripMenuItem it = new ToolStripMenuItem(Bonito(app), ic, delegate
                {
                    data.Add(app);
                    Config.SetL(cfgKey, data); SyncJogos(); Invalidate();
                }) { ForeColor = CText };
                menu.Items.Add(it);
            }
            if (apps.Count == 0) menu.Items.Add(new ToolStripMenuItem("Nenhum outro app aberto agora") { Enabled = false });
            menu.Closed += delegate { BeginInvoke((MethodInvoker)menu.Dispose); };
            menu.Show(Cursor.Position);
        }

        class DarkMenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Color.FromArgb(70, 60, 120); } }
            public override Color MenuItemBorder { get { return Color.FromArgb(110, 90, 200); } }
            public override Color MenuBorder { get { return Color.FromArgb(60, 56, 90); } }
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(30, 27, 48); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(30, 27, 48); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(30, 27, 48); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(30, 27, 48); } }
        }

        // ---------------- Ícones dos programas ----------------
        Bitmap IconForProcess(string name)
        {
            Bitmap b;
            if (TryIcon(name, out b)) return b;
            b = IconFromFile(Engine.PathOf(name));
            lock (icons) icons[name] = b;
            return b;
        }

        bool TryIcon(string key, out Bitmap b) { lock (icons) return icons.TryGetValue(key, out b); }

        static Bitmap IconFromFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (Icon ic = Icon.ExtractAssociatedIcon(path)) return ic == null ? null : ic.ToBitmap();
            }
            catch { return null; }
        }

        static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 1) return cmd.Substring(1, end - 1);
            }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i > 0) return cmd.Substring(0, i + 4);
            return File.Exists(cmd) ? cmd : null;
        }

        // ---------------- Ações ----------------
        async void RefreshList()
        {
            List<Consumer> top = await Task.Run(delegate
            {
                List<Consumer> r = Engine.TopConsumers(8).Select(x => new Consumer { Nome = x.Nome, Bytes = x.Bytes }).ToList();
                foreach (Consumer c in r) IconForProcess(c.Nome);
                foreach (string j in jogos.Concat(fechar)) IconForProcess(j);
                return r;
            });
            consumers = top;
            Invalidate();
        }

        async void LoadStartup()
        {
            List<Engine.StartupItem> items = await Task.Run(delegate
            {
                List<Engine.StartupItem> r = Engine.StartupItems();
                foreach (Engine.StartupItem it in r)
                    { Bitmap x; if (!TryIcon("start:" + it.Nome, out x)) { x = IconFromFile(ExeFromCommand(it.Comando)); lock (icons) icons["start:" + it.Nome] = x; } }
                return r;
            });
            startItems = items;
            Invalidate();
        }

        void ToggleStartup(Engine.StartupItem it)
        {
            try
            {
                Engine.SetStartup(it, !it.Ligado);
                AddActivity(DateTime.Now, "Inicialização: " + it.Nome + (it.Ligado ? " ligado" : " desligado"), true);
                Invalidate();
            }
            catch (Exception)
            {
                MessageBox.Show(this, "\"" + it.Nome + "\" abre para todos os usuários do PC: para mudar, ligue o Modo forte (o escudo no menu).",
                    "Fôlego", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void AskKill(string nome)
        {
            if (MessageBox.Show(this, "Fechar \"" + nome + "\"?\n\nO que não estiver salvo nele será perdido.", "Fôlego",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int n = Engine.Kill(nome);
            if (n > 0) AddActivity(DateTime.Now, "Fechou " + nome, true);
            else { lastMsg = "Não deu para fechar " + nome + " (tente o modo forte)."; lastMsgColor = CBad; }
            RefreshList();
        }

        // origem: null = botão; "Automático" = RAM acima de 85% (só RAM); "Programado" = de tempos em tempos.
        async void RunOptimize(string origem)
        {
            if (busy) return;
            busy = true;
            ResetSchedule();
            bool auto = origem == "Automático", manual = origem == null;
            StartAnim();
            Invalidate();
            bool temp = !auto && opts["temp"], dns = !auto && opts["dns"], bin = manual && opts["lixeira"];
            double ram = 0; long bytes = 0; bool purged = false;
            await Task.Run(delegate
            {
                ram = Engine.TrimRam();
                purged = Engine.PurgeStandby();
                if (temp) bytes = Engine.CleanTemp();
                if (dns) Engine.FlushDns();
                if (bin) Engine.EmptyRecycleBin();
            });
            string msg = string.Format(Pt, "{0}Liberou {1:0} MB de RAM", manual ? "" : origem + ": ", Math.Max(0, ram));
            if (purged) msg += " e limpou o cache de memória";
            if (temp) msg += string.Format(Pt, " · apagou {0:0} MB de temporários", bytes / 1048576.0);
            if (bin) msg += " · lixeira vazia";
            busy = false;
            Notify(msg + ".");
            ReadStats();
            RefreshList();
        }

        void Notify(string msg)
        {
            lastMsg = msg;
            lastMsgColor = CGood;
            AddActivity(DateTime.Now, msg, true); // sem aviso na tela: nada pode atrapalhar o jogo
            Invalidate();
        }

        void LoadActivity()
        {
            try
            {
                if (!File.Exists(Engine.LogPath)) return;
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                foreach (string line in File.ReadAllLines(Engine.LogPath))
                {
                    if (line.Length < 22 || !line.StartsWith(today)) continue;
                    DateTime t;
                    if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                    AddActivity(t, line.Substring(19).Trim(), false);
                }
            }
            catch { }
        }

        void AddActivity(DateTime t, string msg, bool log)
        {
            msg = Regex.Replace(msg, @"[\uD800-\uDFFF☀-➿]", "").Trim();
            if (log) Engine.Log(msg);
            Match m = Regex.Match(msg, @"[Ll]iberou (\d+) MB");
            if (m.Success) { freedTodayMB += int.Parse(m.Groups[1].Value); runsToday++; }
            if (msg.EndsWith(": ligado") || msg.EndsWith(": desligado")) return; // troca de opção: só no registro
            Color c = msg.Contains("Modo Jogo") || msg.Contains("jogo") ? CPurple
                : msg.Contains("iberou") ? CTeal : msg.StartsWith("Inicialização") ? COrange : msg.StartsWith("Fechou") ? CBad : CBlue;
            acts.Add(new Act { T = t, Text = msg, C = c });
            if (acts.Count > 60) acts.RemoveAt(0);
        }

        void OpenLog()
        {
            try { if (File.Exists(Engine.LogPath)) Process.Start("notepad.exe", "\"" + Engine.LogPath + "\""); } catch { }
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        void RelaunchAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--reaberto") { Verb = "runas", UseShellExecute = true });
                exiting = true;
                Close();
            }
            catch (System.ComponentModel.Win32Exception) { } // a pessoa clicou em "Não" no aviso do Windows
        }

        // ---------------- Modo Jogo (lógica) ----------------
        void CheckGame()
        {
            if (gameBusy) return;
            string found = null;
            if (opts["jogo"])
                foreach (string j in jogos)
                    if (Engine.IsRunning(j)) { found = j; break; }
            if (found != null && currentGame == null) StartGame(found);
            else if (found == null && currentGame != null) EndGame();
        }

        async void StartGame(string j)
        {
            currentGame = j;
            gameBusy = true;
            if (opts["jogo_acordado"]) Acordado(true);
            nextAfk = DateTime.Now.AddMinutes(12);
            gameStatus = "Preparando o PC...";
            Invalidate();
            bool ram = opts["jogo_ram"], prio = opts["jogo_prio"];
            List<string> alvo = new List<string>(fechar), fechados = new List<string>(), caminhos = new List<string>();
            double mb = 0; int prioN = 0;
            await Task.Run(delegate
            {
                System.Threading.Thread.Sleep(3000); // deixa o jogo terminar de abrir
                if (prio) prioN = Engine.SetHighPriority(j);
                foreach (string n in alvo)
                {
                    string path = Engine.PathOf(n);
                    if (Engine.Kill(n) > 0) { fechados.Add(n); if (path != null) caminhos.Add(path); }
                }
                if (ram) { mb = Engine.TrimRam(); Engine.PurgeStandby(); }
            });
            reabrir.Clear();
            reabrir.AddRange(caminhos);
            List<string> partes = new List<string>();
            if (prioN > 0) partes.Add("prioridade alta");
            if (acordado) partes.Add("PC acordado");
            if (fechados.Count > 0) partes.Add("fechou " + string.Join(", ", fechados));
            if (ram) partes.Add(string.Format(Pt, "liberou {0:0} MB", Math.Max(0, mb)));
            gameStatus = Bonito(j) + " aberto" + (partes.Count > 0 ? " · " + string.Join(" · ", partes) : "");
            Notify("Modo Jogo: " + gameStatus);
            gameBusy = false;
        }

        void EndGame()
        {
            string j = currentGame;
            currentGame = null;
            Acordado(false);
            gameStatus = null;
            List<string> abertos = new List<string>();
            if (opts["jogo_reabrir"])
                foreach (string path in reabrir)
                    if (Engine.OpenAsUser(path)) abertos.Add(Path.GetFileNameWithoutExtension(path));
            reabrir.Clear();
            string msg = Bonito(j) + " fechou: Modo Jogo encerrado";
            if (abertos.Count > 0) msg += " · reabriu " + string.Join(", ", abertos);
            Notify(msg + ".");
        }
    }
}
