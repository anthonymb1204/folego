// Desenha o ícone do Fôlego: um "F" em que os traços são um sopro de vento (o de cima enrola).
// Uso: icone\build-icone.bat [versão]  ->  ..\folego.ico (da versão escolhida) e icone\previa.png (todas)
// Versões: 1 = F colorido em fundo escuro, 2 = F branco em fundo colorido, 3 = só o F colorido (sem fundo)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class Icone
{
    static readonly Color Agua = Color.FromArgb(45, 212, 191), Azul = Color.FromArgb(59, 110, 246);

    static Bitmap Draw(int s, int versao)
    {
        Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float pad = s * 0.04f, w = s - 2 * pad;
            if (versao == 1)
                using (GraphicsPath bg = Rounded(pad, pad, w, w, s * 0.24f))
                using (LinearGradientBrush br = new LinearGradientBrush(new PointF(0, 0), new PointF(0, s),
                           Color.FromArgb(40, 43, 54), Color.FromArgb(22, 23, 29)))
                    g.FillPath(br, bg);
            else if (versao == 2)
                using (GraphicsPath bg = Rounded(pad, pad, w, w, s * 0.24f))
                using (LinearGradientBrush br = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s), Agua, Azul))
                    g.FillPath(br, bg);

            // Sem fundo, o F ocupa mais o quadrado.
            float k = versao == 3 ? 1.18f : 1f, off = s * (1 - k) / 2;
            float stroke = Math.Max(1.8f, s * k * (s <= 24 ? 0.12f : 0.088f));
            Brush tinta = versao == 2 ? (Brush)new SolidBrush(Color.White)
                : new LinearGradientBrush(new PointF(s * 0.2f, s * 0.2f), new PointF(s * 0.8f, s * 0.8f), Agua, Azul);
            using (tinta)
            using (Pen pen = new Pen(tinta, stroke))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                Func<float, float> X = v => off + s * k * v, Y = v => off + s * k * v;
                float cr = s * k * 0.085f;

                // Haste + traço de cima, que termina enrolando para cima (o "sopro").
                using (GraphicsPath f = new GraphicsPath())
                {
                    f.AddLine(X(0.32f), Y(0.78f), X(0.32f), Y(0.34f));
                    f.AddLine(X(0.32f), Y(0.34f), X(0.62f), Y(0.34f));
                    f.AddArc(X(0.62f) - cr, Y(0.34f) - 2 * cr, 2 * cr, 2 * cr, 90, -270);
                    g.DrawPath(pen, f);
                }
                // Traço do meio, mais comprido, como o ar saindo.
                g.DrawLine(pen, X(0.32f), Y(0.56f), X(0.70f), Y(0.56f));
            }
        }
        return bmp;
    }

    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        float d = 2 * r;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void Main(string[] args)
    {
        int versao = args.Length > 0 ? int.Parse(args[0]) : 1;
        string ico = args.Length > 1 ? args[1] : "folego.ico";
        string previa = args.Length > 2 ? args[2] : "previa.png";
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int s in sizes)
            using (Bitmap b = Draw(s, versao))
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }

        // Arquivo .ico com cada tamanho guardado como PNG (aceito pelo Windows desde o Vista).
        using (BinaryWriter w = new BinaryWriter(File.Create(ico)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] p in pngs) w.Write(p);
        }

        // Prévia: as 3 versões (colunas), em fundo escuro (em cima) e claro (embaixo), grande e pequeno.
        const int col = 240, row = 230;
        using (Bitmap prev = new Bitmap(col * 3, row * 2))
        using (Graphics g = Graphics.FromImage(prev))
        using (Font font = new Font("Segoe UI", 13f, FontStyle.Bold))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.FromArgb(24, 25, 30));
            g.FillRectangle(new SolidBrush(Color.FromArgb(240, 240, 244)), 0, row, col * 3, row);
            for (int v = 1; v <= 3; v++)
                for (int r = 0; r < 2; r++)
                {
                    int x0 = (v - 1) * col, y0 = r * row;
                    g.DrawString(v.ToString(), font, new SolidBrush(r == 0 ? Color.White : Color.Black), x0 + 10, y0 + 8);
                    using (Bitmap big = Draw(128, v)) g.DrawImage(big, x0 + 56, y0 + 14);
                    int x = x0 + 26;
                    foreach (int s in new[] { 16, 24, 32, 48 })
                    {
                        using (Bitmap b = Draw(s, v)) g.DrawImage(b, x, y0 + row - s - 14);
                        x += s + 16;
                    }
                }
            prev.Save(previa, ImageFormat.Png);
        }
    }
}
