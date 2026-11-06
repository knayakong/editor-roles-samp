// Lector del chat por COLOR (el fondo deja de importar).
// La letra del chat de SA-MP es un color sólido que se oscurece hacia su contorno negro: cada píxel de
// letra es  α·color  (0<α≤1). Eso permite separar la letra de cualquier fondo, línea por línea:
//   1) se encuentran las filas del chat (paso regular) y su columna,
//   2) en cada fila se miden sus colores reales (por "tono normalizado", que no cambia con α),
//   3) se arma una imagen limpia (letra negra sobre blanco) y se lee,
//   4) cada línea sale con su color exacto.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EditorRoles
{
    public class Banda
    {
        public int Y0, Y1, X0, X1;
        public List<Color> Colores = new List<Color>();
        public Color Principal = Color.White;
        public float[] Alfa;      // (X1-X0) * (Y1-Y0)
        public byte[] Indice;     // qué color ganó cada píxel (para el color por palabra)
        public int[] Original;    // píxeles originales de la fila
        public bool[] Cerca;      // a ≤2 px de letra (lo que se conserva en la imagen limpia)
        public int W { get { return X1 - X0; } }
        public int H { get { return Y1 - Y0; } }
    }

    public static class Lector
    {
        public static Action<string> Log;
        static void L(string m) { if (Log != null) Log(m); }

        // ---------- 0. utilidades ----------
        static bool Oscuro(Pixeles px, int x, int y) { return x >= 0 && y >= 0 && x < px.W && y < px.H && px.Max(x, y) < 40; }

        static bool CercaDeOscuro(Pixeles px, int x, int y, int r)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Oscuro(px, x + dx, y + dy)) return true;
            return false;
        }

        // Candidato a letra = NÚCLEO de un trazo: claro y con contorno negro a AMBOS lados
        // (izq+der o arriba+abajo, a ≤3 px). El fondo sólo toca el contorno por un lado.
        static bool[] Candidatos(Pixeles px)
        {
            int W = px.W, H = px.H;
            var osc = new bool[W * H];
            for (int i = 0; i < osc.Length; i++)
            {
                int p = px.Argb[i];
                osc[i] = Math.Max((p >> 16) & 255, Math.Max((p >> 8) & 255, p & 255)) < 40;
            }
            var c = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int p = px.Argb[y * W + x];
                    if (Math.Max((p >> 16) & 255, Math.Max((p >> 8) & 255, p & 255)) < 70) continue;
                    bool iz = false, de = false, ar = false, ab = false;
                    for (int d = 1; d <= 3; d++)
                    {
                        if (x - d >= 0) iz |= osc[y * W + x - d];
                        if (x + d < W) de |= osc[y * W + x + d];
                        if (y - d >= 0) ar |= osc[(y - d) * W + x];
                        if (y + d < H) ab |= osc[(y + d) * W + x];
                    }
                    c[y * W + x] = (iz && de) || (ar && ab);
                }
            return c;
        }

        // ---------- 1. filas del chat ----------
        public static List<Banda> Bandas(Pixeles px, bool[] cand)
        {
            int xMax = (int)(px.W * 0.75f);
            var perfil = new float[px.H];
            for (int y = 0; y < px.H; y++) { int n = 0; for (int x = 0; x < xMax; x++) if (cand[y * px.W + x]) n++; perfil[y] = n; }

            // paso de línea por autocorrelación
            int mejorP = 0; double mejorV = 0;
            double media = 0; foreach (var v in perfil) media += v; media /= perfil.Length;
            for (int lag = 10; lag <= 48; lag++)
            {
                double s = 0, n1 = 0, n2 = 0;
                for (int y = 0; y + lag < px.H; y++) { double a = perfil[y] - media, b = perfil[y + lag] - media; s += a * b; n1 += a * a; n2 += b * b; }
                double v = s / Math.Sqrt(n1 * n2 + 1e-9);
                if (v > mejorV) { mejorV = v; mejorP = lag; }
            }
            L("paso=" + mejorP + " correl=" + mejorV.ToString("0.00"));
            if (mejorP == 0 || mejorV < 0.2) return new List<Banda>();
            int P = mejorP;

            // fase: los huecos entre líneas son los mínimos
            int fase = 0; double minS = double.MaxValue;
            for (int f = 0; f < P; f++)
            {
                double s = 0; for (int y = f; y < px.H; y += P) s += perfil[y];
                if (s < minS) { minS = s; fase = f; }
            }

            // filas candidatas y su borde izquierdo
            var filas = new List<Banda>();
            for (int y0 = fase - P; y0 < px.H; y0 += P)
            {
                int a = Math.Max(0, y0), b = Math.Min(px.H, y0 + P);
                if (b - a < P / 2) continue;
                int x0 = -1;
                for (int x = 0; x < xMax && x0 < 0; x++)
                {
                    int n = 0; for (int y = a; y < b; y++) if (cand[y * px.W + x]) n++;
                    if (n >= 2) x0 = x;
                }
                if (x0 < 0) continue;
                var bd = new Banda(); bd.Y0 = a; bd.Y1 = b; bd.X0 = x0; filas.Add(bd);
            }
            if (filas.Count == 0) return filas;

            // columna del chat = el borde izquierdo más repetido
            int tolCol = Math.Max(6, P / 2);
            int col = 0, nCol = -1;
            foreach (var f in filas)
            {
                int n = 0; foreach (var g in filas) if (Math.Abs(g.X0 - f.X0) <= tolCol) n++;
                if (n > nCol || (n == nCol && f.X0 < col)) { nCol = n; col = f.X0; }
            }
            var res = new List<Banda>();
            foreach (var f in filas)
            {
                if (f.X0 < col - tolCol || f.X0 > col + tolCol + P) continue; // " * " del /me arranca un poco más a la derecha
                // fin de la línea: último tramo con letra antes de un hueco grande (los nombres de las cabezas quedan fuera)
                int fin = f.X0, hueco = 0;
                for (int x = f.X0; x < px.W; x++)
                {
                    bool hay = false; for (int y = f.Y0; y < f.Y1 && !hay; y++) hay = cand[y * px.W + x];
                    if (hay) { fin = x; hueco = 0; } else if (++hueco > P * 2) break;
                }
                f.X0 = Math.Max(0, f.X0 - 3); f.X1 = Math.Min(px.W, fin + 4);
                res.Add(f);
            }
            // sólo el bloque continuo del chat (cortar filas sueltas lejos del resto)
            L("filas en la columna " + col + ": " + res.Count);
            return res;
        }

        // ---------- 2. colores de cada fila ----------
        static void Colores(Pixeles px, bool[] cand, Banda b)
        {
            // agrupar por tono normalizado (r/max, g/max, b/max): no cambia con α
            var grupos = new Dictionary<int, List<int>>();
            for (int y = b.Y0; y < b.Y1; y++)
                for (int x = b.X0; x < b.X1; x++)
                {
                    if (!cand[y * px.W + x]) continue;
                    int p = px.Argb[y * px.W + x];
                    int r = (p >> 16) & 255, g = (p >> 8) & 255, bl = p & 255, mx = Math.Max(r, Math.Max(g, bl));
                    if (mx < 90) continue;
                    int k = ((r * 15 / mx) << 8) | ((g * 15 / mx) << 4) | (bl * 15 / mx);
                    List<int> lst; if (!grupos.TryGetValue(k, out lst)) { lst = new List<int>(); grupos[k] = lst; }
                    lst.Add(p);
                }
            int total = 0; foreach (var g in grupos.Values) total += g.Count;
            // la letra es lo BRILLANTE y abundante: se ordena por cantidad × brillo², y lo apagado no es texto
            var peso = new Dictionary<int, double>();
            foreach (var kv in grupos)
            {
                var br = new List<int>();
                foreach (int p in kv.Value) br.Add(Math.Max((p >> 16) & 255, Math.Max((p >> 8) & 255, p & 255)));
                br.Sort(); double p85 = br[(int)(br.Count * 0.85)] / 255.0;
                peso[kv.Key] = p85 < 0.40 ? 0 : kv.Value.Count * p85 * p85;
            }
            var orden = new List<KeyValuePair<int, List<int>>>(grupos);
            orden.RemoveAll(delegate (KeyValuePair<int, List<int>> u) { return peso[u.Key] <= 0; });
            orden.Sort(delegate (KeyValuePair<int, List<int>> u, KeyValuePair<int, List<int>> v) { return peso[v.Key].CompareTo(peso[u.Key]); });
            b.Colores.Clear();
            foreach (var kv in orden)
            {
                if (b.Colores.Count >= 5) break;
                if (kv.Value.Count < Math.Max(8, total * 0.05)) break;
                // el color pleno = los más brillantes del grupo (percentil 85)
                var brillos = new List<int>();
                foreach (int p in kv.Value) brillos.Add(Math.Max((p >> 16) & 255, Math.Max((p >> 8) & 255, p & 255)));
                brillos.Sort();
                int umbral = brillos[(int)(brillos.Count * 0.85)];
                long sr = 0, sg = 0, sb = 0; int n = 0;
                foreach (int p in kv.Value)
                {
                    int r = (p >> 16) & 255, g = (p >> 8) & 255, bl = p & 255;
                    if (Math.Max(r, Math.Max(g, bl)) >= umbral) { sr += r; sg += g; sb += bl; n++; }
                }
                var c = Color.FromArgb((int)(sr / n), (int)(sg / n), (int)(sb / n));
                bool repetido = false;
                foreach (var o in b.Colores) if (Dist(o, c) < 30 * 30) { repetido = true; break; }
                if (!repetido) b.Colores.Add(c);
            }
            if (b.Colores.Count == 0) b.Colores.Add(Color.White);
        }

        static int Dist(Color a, Color b) { int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B; return dr * dr + dg * dg + db * db; }

        // ---------- 3. mapa de letra (α) ----------
        static void Alfa(Pixeles px, bool[] cand, Banda b)
        {
            b.Alfa = new float[b.W * b.H]; b.Indice = new byte[b.W * b.H]; b.Original = new int[b.W * b.H];
            var masa = new float[b.Colores.Count];
            for (int y = 0; y < b.H; y++)
                for (int x = 0; x < b.W; x++)
                {
                    int gx = b.X0 + x, gy = b.Y0 + y;
                    int p = px.Argb[gy * px.W + gx];
                    b.Original[y * b.W + x] = p;
                    int r = (p >> 16) & 255, g = (p >> 8) & 255, bl = p & 255;
                    float mejor = 0; int ki = 0;
                    for (int k = 0; k < b.Colores.Count; k++)
                    {
                        var c = b.Colores[k];
                        float cc = c.R * c.R + c.G * c.G + c.B * c.B; if (cc < 1) continue;
                        float t = (r * c.R + g * c.G + bl * c.B) / cc;
                        if (t < 0.22f) continue;
                        float tt = Math.Min(t, 1f);
                        float er = r - tt * c.R, eg = g - tt * c.G, eb = bl - tt * c.B;
                        float res = (float)Math.Sqrt(er * er + eg * eg + eb * eb);
                        if (res > 18 + 0.10f * tt * (float)Math.Sqrt(cc)) continue;
                        if (t > 1.25f) continue;
                        if (tt > mejor) { mejor = tt; ki = k; }
                    }
                    if (mejor > 0 && !CercaDeOscuro(px, gx, gy, 2)) mejor = 0; // la letra siempre toca su contorno
                    // núcleo del trazo (contorno a ambos lados): es letra aunque el fondo le cambie el tono
                    // (el texto del chat es apenas transparente)
                    if (cand[gy * px.W + gx]) mejor = 1f;
                    b.Alfa[y * b.W + x] = mejor; b.Indice[y * b.W + x] = (byte)ki;
                    if (mejor > 0) masa[ki] += mejor;
                }
            b.Cerca = new bool[b.W * b.H];
            for (int y = 0; y < b.H; y++)
                for (int x = 0; x < b.W; x++)
                {
                    if (b.Alfa[y * b.W + x] <= 0) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && yy >= 0 && xx < b.W && yy < b.H) b.Cerca[yy * b.W + xx] = true;
                        }
                }
            int kp = 0; for (int k = 1; k < masa.Length; k++) if (masa[k] > masa[kp]) kp = k;
            b.Principal = b.Colores[kp];
        }

        // ---------- 4. imagen limpia y lectura ----------
        const int PAD = 12;
        public static bool SoloLetra = false; // ver medición en CLAUDE.md
        public static int BordeMax = 70;
        public static int FondoLimpio = 255; // fondo blanco: el contorno negro da el contraste (medido: 0→29.6 %, 255→77 %)

        public static Bitmap ImagenLimpia(List<Banda> bandas, out List<int> offsets, out int ancho)
        {
            ancho = 0; foreach (var b in bandas) ancho = Math.Max(ancho, b.W);
            ancho += PAD * 2;
            int alto = PAD; offsets = new List<int>();
            foreach (var b in bandas) { offsets.Add(alto); alto += b.H + PAD; }
            var bmp = new Bitmap(ancho, alto, PixelFormat.Format24bppRgb);
            var d = bmp.LockBits(new Rectangle(0, 0, ancho, alto), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var fila = new byte[d.Stride];
                int bi = 0, yb = 0;
                for (int y = 0; y < alto; y++)
                {
                    for (int i = 0; i < fila.Length; i++) fila[i] = (byte)FondoLimpio;
                    // ¿qué banda cae en esta fila?
                    while (bi < bandas.Count && y >= offsets[bi] + bandas[bi].H) bi++;
                    if (bi < bandas.Count && y >= offsets[bi])
                    {
                        var b = bandas[bi]; yb = y - offsets[bi];
                        for (int x = 0; x < b.W; x++)
                        {
                            // la letra ORIGINAL (con su contorno y suavizado) + 2 px alrededor; el resto del
                            // fondo del juego pasa a negro. Queda como un chatlog, que el OCR lee muy bien.
                            int po = b.Original[yb * b.W + x];
                            int pr = FondoLimpio, pg = FondoLimpio, pb = FondoLimpio;
                            if (b.Cerca[yb * b.W + x])
                            {
                                int r0 = (po >> 16) & 255, g0 = (po >> 8) & 255, b0 = po & 255;
                                bool borde = Math.Max(r0, Math.Max(g0, b0)) < BordeMax;
                                // SoloLetra: sólo letra (α>0) y su borde negro; el fondo pegado también se pinta
                                if (!SoloLetra || borde || b.Alfa[yb * b.W + x] > 0) { pr = r0; pg = g0; pb = b0; }
                            }
                            int o = (PAD + x) * 3; fila[o] = (byte)pb; fila[o + 1] = (byte)pg; fila[o + 2] = (byte)pr;

                        }
                    }
                    Marshal.Copy(fila, 0, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), d.Stride);
                }
            }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }

        // filas del chat con su color y su mapa de letra (sin leer)
        public static List<Banda> Aislar(Bitmap original)
        {
            var px = new Pixeles(original);
            var cand = Candidatos(px);
            var bandas = Bandas(px, cand);
            foreach (var b in bandas) { Colores(px, cand, b); Alfa(px, cand, b); }
            return bandas;
        }

        public static List<LineaCruda> Leer(Bitmap original, string idioma, out List<Banda> bandas)
        {
            var px = new Pixeles(original);
            var cand = Candidatos(px);
            bandas = Bandas(px, cand);
            var res = new List<LineaCruda>();
            if (bandas.Count < 2) return res;
            foreach (var b in bandas) { Colores(px, cand, b); Alfa(px, cand, b); }

            List<int> offs; int ancho;
            List<LineaCruda> crudas;
            using (var limpia = ImagenLimpia(bandas, out offs, out ancho))
            {
                if (Log != null) limpia.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "editor-roles-limpia.png"));
                crudas = MotorOcr.LeerCrudo(limpia, idioma);
            }

            // devolver cada lectura a su banda y a coordenadas de la captura
            var porBanda = new List<List<LineaCruda>>();
            for (int i = 0; i < bandas.Count; i++) porBanda.Add(new List<LineaCruda>());
            foreach (var c in crudas)
            {
                float cy = c.Caja.Y + c.Caja.Height / 2;
                for (int i = 0; i < bandas.Count; i++)
                    if (cy >= offs[i] - PAD / 2 && cy < offs[i] + bandas[i].H + PAD / 2) { porBanda[i].Add(c); break; }
            }
            for (int i = 0; i < bandas.Count; i++)
            {
                var b = bandas[i];
                var ls = porBanda[i];
                if (ls.Count == 0) continue;
                ls.Sort(delegate (LineaCruda u, LineaCruda v) { return u.Caja.X.CompareTo(v.Caja.X); });
                var l = new LineaCruda();
                var textos = new List<string>();
                foreach (var c in ls)
                {
                    textos.Add(c.Texto);
                    foreach (var w in c.Palabras)
                    {
                        var pw = new PalabraCruda(); pw.Texto = w.Texto;
                        pw.Caja = new RectangleF(w.Caja.X - PAD + b.X0, w.Caja.Y - offs[i] + b.Y0, w.Caja.Width, w.Caja.Height);
                        l.Palabras.Add(pw);
                    }
                }
                l.Texto = string.Join(" ", textos.ToArray());
                l.Caja = new RectangleF(b.X0, b.Y0, b.W, b.H);
                l.ColorLinea = b.Principal;
                l.Banda = b;
                res.Add(l);
            }
            return res;
        }
    }
}
