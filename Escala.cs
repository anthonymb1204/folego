// Escala nítida: o jogo roda numa janela menor (mais leve, mais FPS) e o Fôlego estica essa janela para a
// tela inteira usando a ampliação do próprio Windows (a mesma da Lupa), com a suavização desligada para os
// pixels ficarem nítidos. O mouse continua funcionando normalmente: o Windows faz a conta da ampliação.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Folego
{
    static class Escala
    {
        [DllImport("Magnification.dll")] static extern bool MagInitialize();
        [DllImport("Magnification.dll")] static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] static extern bool MagSetFullscreenTransform(float level, int xOffset, int yOffset);
        [DllImport("Magnification.dll")] static extern bool MagGetFullscreenTransform(out float level, out int xOffset, out int yOffset);
        [DllImport("Magnification.dll")] static extern bool MagSetFullscreenUseBitmapSmoothing(bool smooth);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);

        static bool iniciada;
        public static IntPtr Alvo { get; private set; }
        public static float Fator { get; private set; }
        public static string Jogo { get; private set; }
        public static bool Ligada { get { return Alvo != IntPtr.Zero; } }

        static bool Iniciar()
        {
            if (!iniciada) iniciada = MagInitialize();
            return iniciada;
        }

        // Liga no app da frente. Devolve null se deu certo, ou o motivo de não ter dado.
        public static string Ligar(bool nitido, IntPtr ignorar)
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == ignorar) return "Deixe o jogo na frente e use o atalho.";
            RECT rc;
            if (!GetClientRect(h, out rc) || rc.R - rc.L < 160 || rc.B - rc.T < 120) return "Não achei a janela do jogo.";
            POINT p = new POINT { X = 0, Y = 0 };
            ClientToScreen(h, ref p);
            int cw = rc.R - rc.L, ch = rc.B - rc.T;
            Rectangle mon = Screen.FromHandle(h).Bounds;

            float f = Math.Min((float)mon.Width / cw, (float)mon.Height / ch);
            if (nitido && f >= 2f) f = (float)Math.Floor(f); // pixel nítido: multiplica por um número inteiro
            if (f < 1.05f) return "O jogo já ocupa a tela toda: coloque-o numa janela menor (ex.: metade da tela).";
            if (!Iniciar()) return "O Windows não deixou usar a ampliação.";

            // Área da tela que vai ser esticada: centrada na janela do jogo, sem sair do monitor.
            int rw = (int)Math.Round(mon.Width / f), rh = (int)Math.Round(mon.Height / f);
            int x = p.X + cw / 2 - rw / 2, y = p.Y + ch / 2 - rh / 2;
            x = Math.Max(mon.Left, Math.Min(x, mon.Right - rw));
            y = Math.Max(mon.Top, Math.Min(y, mon.Bottom - rh));
            try { MagSetFullscreenUseBitmapSmoothing(!nitido); } catch (EntryPointNotFoundException) { }
            if (!MagSetFullscreenTransform(f, x, y)) return "O Windows não deixou ampliar a tela.";

            Alvo = h; Fator = f;
            int pid; GetWindowThreadProcessId(h, out pid);
            try { Jogo = System.Diagnostics.Process.GetProcessById(pid).ProcessName; } catch { Jogo = "jogo"; }
            return null;
        }

        public static void Desligar()
        {
            if (iniciada)
            {
                MagSetFullscreenTransform(1f, 0, 0);
                try { MagSetFullscreenUseBitmapSmoothing(true); } catch (EntryPointNotFoundException) { }
            }
            Alvo = IntPtr.Zero; Fator = 0; Jogo = null;
        }

        // Desliga se a pessoa saiu do jogo (outra janela na frente) ou se o jogo fechou.
        public static bool PrecisaDesligar()
        {
            return Ligada && (!IsWindow(Alvo) || GetForegroundWindow() != Alvo);
        }

        public static void Encerrar()
        {
            if (Ligada) Desligar();
            if (iniciada) { MagUninitialize(); iniciada = false; }
        }

        // Teste sem mexer na tela: amplia 1,0001x (invisível) e volta. Devolve se a ampliação funciona.
        public static bool Testar()
        {
            if (!Iniciar()) return false;
            bool ok = MagSetFullscreenTransform(1.0001f, 0, 0);
            float lv; int ox, oy;
            ok = ok && MagGetFullscreenTransform(out lv, out ox, out oy) && lv > 1f;
            MagSetFullscreenTransform(1f, 0, 0);
            return ok;
        }
    }
}
