// Gerador de QR code (modo byte, correção M, versões 1 a 10), sem bibliotecas externas.
// Baseado no algoritmo de referência de Project Nayuki (QR Code generator library, licença MIT).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace Folego
{
    static class Qr
    {
        static readonly int[] EccPorBloco = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[] Blocos = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

        // Devolve a matriz [x, y] (true = quadradinho escuro).
        public static bool[,] Gerar(string texto)
        {
            byte[] dados = Encoding.UTF8.GetBytes(texto);
            int ver = 1;
            for (; ver <= 10; ver++)
                if (4 + (ver < 10 ? 8 : 16) + dados.Length * 8 <= DataCodewords(ver) * 8) break;
            if (ver > 10) throw new ArgumentException("Texto grande demais para o QR code.");

            List<bool> bits = new List<bool>();
            Bits(bits, 4, 4);
            Bits(bits, dados.Length, ver < 10 ? 8 : 16);
            foreach (byte b in dados) Bits(bits, b, 8);
            int cap = DataCodewords(ver) * 8;
            Bits(bits, 0, Math.Min(4, cap - bits.Count));
            Bits(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) Bits(bits, pad, 8);
            byte[] dc = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) dc[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            byte[] todos = EccEIntercala(dc, ver);
            int n = ver * 4 + 17;
            bool[,] m = new bool[n, n], fn = new bool[n, n];
            Funcoes(m, fn, ver, n);
            Codewords(m, fn, todos, n);
            for (int y = 0; y < n; y++)          // máscara 0: (x + y) par
                for (int x = 0; x < n; x++)
                    if (!fn[x, y] && (x + y) % 2 == 0) m[x, y] = !m[x, y];
            Formato(m, fn, 0, n);
            return m;
        }

        // Imagem com margem branca de 4 módulos.
        public static Bitmap Imagem(string texto, int px)
        {
            bool[,] m = Gerar(texto);
            int n = m.GetLength(0), tot = n + 8;
            int esc = Math.Max(1, px / tot);
            Bitmap b = new Bitmap(tot * esc, tot * esc);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (m[x, y]) g.FillRectangle(Brushes.Black, (x + 4) * esc, (y + 4) * esc, esc, esc);
            }
            return b;
        }

        static void Bits(List<bool> l, int v, int len) { for (int i = len - 1; i >= 0; i--) l.Add(((v >> i) & 1) != 0); }

        static int RawModules(int ver)
        {
            int r = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int na = ver / 7 + 2;
                r -= (25 * na - 10) * na - 55;
                if (ver >= 7) r -= 36;
            }
            return r;
        }

        static int DataCodewords(int ver) { return RawModules(ver) / 8 - EccPorBloco[ver] * Blocos[ver]; }

        static byte[] EccEIntercala(byte[] dados, int ver)
        {
            int nb = Blocos[ver], ecc = EccPorBloco[ver], raw = RawModules(ver) / 8;
            int curtos = nb - raw % nb, curto = raw / nb;
            byte[] div = RsDivisor(ecc);
            List<byte[]> blocos = new List<byte[]>();
            for (int i = 0, k = 0; i < nb; i++)
            {
                int len = curto - ecc + (i < curtos ? 0 : 1);
                byte[] dat = new byte[len];
                Array.Copy(dados, k, dat, 0, len);
                k += len;
                byte[] e = RsResto(dat, div);
                byte[] bloco = new byte[curto + 1];
                Array.Copy(dat, bloco, len);
                Array.Copy(e, 0, bloco, curto + 1 - ecc, ecc);
                blocos.Add(bloco);
            }
            List<byte> r = new List<byte>();
            for (int i = 0; i < blocos[0].Length; i++)
                for (int j = 0; j < blocos.Count; j++)
                    if (i != curto - ecc || j >= curtos) r.Add(blocos[j][i]);
            return r.ToArray();
        }

        static byte[] RsDivisor(int grau)
        {
            byte[] r = new byte[grau];
            r[grau - 1] = 1;
            int raiz = 1;
            for (int i = 0; i < grau; i++)
            {
                for (int j = 0; j < grau; j++)
                {
                    r[j] = Mul(r[j], raiz);
                    if (j + 1 < grau) r[j] ^= r[j + 1];
                }
                raiz = Mul(raiz, 2);
            }
            return r;
        }

        static byte[] RsResto(byte[] dados, byte[] div)
        {
            byte[] r = new byte[div.Length];
            foreach (byte b in dados)
            {
                int f = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= Mul(div[i], f);
            }
            return r;
        }

        static byte Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        static void Set(bool[,] m, bool[,] fn, int x, int y, bool escuro) { m[x, y] = escuro; fn[x, y] = true; }

        static void Funcoes(bool[,] m, bool[,] fn, int ver, int n)
        {
            for (int i = 0; i < n; i++) { Set(m, fn, 6, i, i % 2 == 0); Set(m, fn, i, 6, i % 2 == 0); }
            foreach (int[] c in new[] { new[] { 3, 3 }, new[] { n - 4, 3 }, new[] { 3, n - 4 } })
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = c[0] + dx, yy = c[1] + dy;
                        if (xx >= 0 && xx < n && yy >= 0 && yy < n) Set(m, fn, xx, yy, d != 2 && d != 4);
                    }
            if (ver > 1)
            {
                int na = ver / 7 + 2, passo = (ver * 4 + na * 2 + 1) / (na * 2 - 2) * 2;
                int[] pos = new int[na];
                pos[0] = 6;
                for (int i = na - 1, p = n - 7; i >= 1; i--, p -= passo) pos[i] = p;
                for (int i = 0; i < na; i++)
                    for (int j = 0; j < na; j++)
                    {
                        if ((i == 0 && j == 0) || (i == 0 && j == na - 1) || (i == na - 1 && j == 0)) continue;
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                                Set(m, fn, pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
            }
            Formato(m, fn, 0, n); // reserva a área do formato (redesenhada no fim)
            if (ver >= 7)
            {
                int rem = ver;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = ver << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = n - 11 + i % 3, b = i / 3;
                    Set(m, fn, a, b, bit);
                    Set(m, fn, b, a, bit);
                }
            }
        }

        static void Formato(bool[,] m, bool[,] fn, int mascara, int n)
        {
            int dados = (0 << 3) | mascara; // correção M = 00
            int rem = dados;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (dados << 10 | rem) ^ 0x5412;
            Func<int, bool> B = i => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) Set(m, fn, 8, i, B(i));
            Set(m, fn, 8, 7, B(6));
            Set(m, fn, 8, 8, B(7));
            Set(m, fn, 7, 8, B(8));
            for (int i = 9; i < 15; i++) Set(m, fn, 14 - i, 8, B(i));
            for (int i = 0; i < 8; i++) Set(m, fn, n - 1 - i, 8, B(i));
            for (int i = 8; i < 15; i++) Set(m, fn, 8, n - 15 + i, B(i));
            Set(m, fn, 8, n - 8, true);
        }

        static void Codewords(bool[,] m, bool[,] fn, byte[] dados, int n)
        {
            int i = 0;
            for (int dir = n - 1; dir >= 1; dir -= 2)
            {
                if (dir == 6) dir = 5;
                for (int v = 0; v < n; v++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = dir - j;
                        bool sobe = ((dir + 1) & 2) == 0;
                        int y = sobe ? n - 1 - v : v;
                        if (!fn[x, y] && i < dados.Length * 8)
                        {
                            m[x, y] = ((dados[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }
    }
}
