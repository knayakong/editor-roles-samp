// Lector por COMPARACIÓN DE LETRAS: el chat de SA-MP es Arial Bold, así que cada letra se compara
// píxel a píxel con Arial Bold dibujada por Windows, sobre el mapa de letra aislado por color (Lector.cs).
//   1) calibra el tamaño y la línea base una vez por captura,
//   2) corta cada fila en tramos de tinta (columnas vacías entre letras),
//   3) cada tramo = la letra que mejor coincide; si es ancho (letras pegadas) prueba cortarlo en 2–4,
//   4) los huecos anchos son espacios (umbral aprendido de la propia captura).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text;

namespace EditorRoles
{
    public class Plantilla
    {
        public char C; public int W; public float[] A; // A: H filas × W columnas, alineada a la línea base
    }

    public class LectorLetras
    {
        public const string JUEGO = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZáéíóúñüÁÉÍÓÚÑ0123456789.,:;!?¡¿()[]\"'-/*>@$%&+=_#";
        public float Tam;      // px
        public int Base;       // fila de la línea base dentro de la banda
        public int H;          // alto de banda
        List<Plantilla> plant;
        public Action<string> Log;

        // ---------- plantillas ----------
        public static TextRenderingHint Modo = TextRenderingHint.AntiAliasGridFit;

        public static List<Plantilla> Plantillas(float tam, int H, int baseFila)
        {
            var res = new List<Plantilla>();
            using (var f = new Font("Arial", tam, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float asc = tam * f.FontFamily.GetCellAscent(FontStyle.Bold) / f.FontFamily.GetEmHeight(FontStyle.Bold);
                int ancho = (int)(tam * 2) + 8;
                foreach (char ch in JUEGO)
                {
                    using (var b = new Bitmap(ancho, H, PixelFormat.Format32bppArgb))
                    using (var g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.Black); g.TextRenderingHint = Modo;
                        g.DrawString(ch.ToString(), f, Brushes.White, 4, baseFila - asc, StringFormat.GenericTypographic);
                        var a = new float[ancho * H];
                        int x0 = ancho, x1 = -1;
                        for (int y = 0; y < H; y++)
                            for (int x = 0; x < ancho; x++)
                            {
                                float v = b.GetPixel(x, y).R / 255f; a[y * ancho + x] = v;
                                if (v > 0.35f) { if (x < x0) x0 = x; if (x > x1) x1 = x; }
                            }
                        if (x1 < x0) continue;
                        var p = new Plantilla(); p.C = ch; p.W = x1 - x0 + 1; p.A = new float[p.W * H];
                        for (int y = 0; y < H; y++) for (int x = 0; x < p.W; x++) p.A[y * p.W + x] = a[y * ancho + x0 + x];
                        Suavizar(p.A, p.W, H);
                        res.Add(p);
                    }
                }
            }
            return res;
        }

        static void Suavizar(float[] a, int w, int h)
        {
            var t = new float[a.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float s = a[y * w + x] * 2; float n = 2;
                    if (x > 0) { s += a[y * w + x - 1]; n++; }
                    if (x < w - 1) { s += a[y * w + x + 1]; n++; }
                    t[y * w + x] = s / n;
                }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float s = t[y * w + x] * 2; float n = 2;
                    if (y > 0) { s += t[(y - 1) * w + x]; n++; }
                    if (y < h - 1) { s += t[(y + 1) * w + x]; n++; }
                    a[y * w + x] = s / n;
                }
        }

        // ---------- comparación ----------
        // NCC entre la ventana [xa, xa+ancho) de la banda (desplazada dy filas) y la plantilla
        static float Comparar(float[] alfa, int bw, int H, int xa, int wr, Plantilla p, int dy)
        {
            int w = Math.Max(wr, p.W);
            double sa = 0, sb = 0, sab = 0, saa = 0, sbb = 0; int n = 0;
            for (int y = 0; y < H; y++)
            {
                int yy = y + dy;
                for (int x = 0; x < w; x++)
                {
                    float a = 0, b = 0;
                    if (x < wr && yy >= 0 && yy < H && xa + x < bw) a = alfa[yy * bw + xa + x];
                    if (x < p.W) b = p.A[y * p.W + x];
                    sa += a; sb += b; sab += a * b; saa += a * a; sbb += b * b; n++;
                }
            }
            double cov = sab - sa * sb / n, va = saa - sa * sa / n, vb = sbb - sb * sb / n;
            if (va < 1e-6 || vb < 1e-6) return 0;
            return (float)(cov / Math.Sqrt(va * vb)) - 0.015f * Math.Abs(wr - p.W);
        }

        struct Acierto { public char C; public float S; }

        readonly Dictionary<long, Acierto> memo = new Dictionary<long, Acierto>();
        float[] memoDe;

        Acierto Mejor(float[] alfa, int bw, int xa, int wr)
        {
            if (memoDe != alfa) { memo.Clear(); memoDe = alfa; }
            long clave = ((long)xa << 20) | (long)wr;
            Acierto m;
            if (memo.TryGetValue(clave, out m)) return m;
            m = MejorSin(alfa, bw, xa, wr); memo[clave] = m; return m;
        }

        Acierto MejorSin(float[] alfa, int bw, int xa, int wr)
        {
            var m = new Acierto(); m.S = -2;
            foreach (var p in plant)
            {
                if (p.W > wr * 2 + 3 || p.W * 2 + 3 < wr) continue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    float s = Comparar(alfa, bw, H, xa, wr, p, dy);
                    if (s > m.S) { m.S = s; m.C = p.C; }
                }
            }
            return m;
        }

        // tramo [xa, xb): una letra, o varias pegadas
        string Tramo(float[] alfa, int bw, int xa, int xb, int prof, out float score)
        {
            int wr = xb - xa;
            var uno = Mejor(alfa, bw, xa, wr);
            score = uno.S;
            string mejor = uno.C.ToString();
            if (prof >= 3 || wr < 6) return mejor;
            float mejorS = uno.S;
            for (int x = xa + 2; x <= xb - 2; x++)
            {
                float s1, s2;
                string a = Tramo(alfa, bw, xa, x, 3, out s1);   // la izquierda como una sola letra
                if (s1 < 0.62f) continue;
                string b = Tramo(alfa, bw, x, xb, prof + 1, out s2);
                float s = (s1 * (x - xa) + s2 * (xb - x)) / wr;
                if (s2 >= 0.62f && s > mejorS + 0.10f) { mejorS = s; mejor = a + b; }
            }
            score = mejorS;
            return mejor;
        }

        // ---------- tramos de tinta de una banda ----------
        static List<int[]> Tramos(Banda b)
        {
            var occ = new bool[b.W];
            for (int x = 0; x < b.W; x++)
                for (int y = 0; y < b.H; y++) if (b.Alfa[y * b.W + x] >= 0.62f) { occ[x] = true; break; }
            var res = new List<int[]>();
            int ini = -1;
            for (int x = 0; x <= b.W; x++)
            {
                bool o = x < b.W && occ[x];
                if (o && ini < 0) ini = x;
                if (!o && ini >= 0) { res.Add(new[] { ini, x }); ini = -1; }
            }
            return res;
        }

        // ---------- calibración (tamaño y línea base) ----------
        public void Calibrar(List<Banda> bandas)
        {
            H = bandas[0].H;
            float mejorS = -1; float mt = 15; int mb = H * 3 / 4;
            var muestras = new List<KeyValuePair<Banda, int[]>>();
            foreach (var b in bandas)
            {
                var ts = Tramos(b);
                for (int i = 0; i < ts.Count && i < 14; i += 2) if (ts[i][1] - ts[i][0] >= 3 && ts[i][1] - ts[i][0] <= 12) muestras.Add(new KeyValuePair<Banda, int[]>(b, ts[i]));
                if (muestras.Count > 40) break;
            }
            for (float t = 13; t <= 20.01f; t += 1f)
                for (int bl = H / 2; bl <= H - 2; bl++)
                {
                    plant = Plantillas(t, H, bl); memo.Clear(); memoDe = null;
                    float s = 0;
                    foreach (var m in muestras) s += Mejor(m.Key.Alfa, m.Key.W, m.Value[0], m.Value[1] - m.Value[0]).S;
                    s /= Math.Max(1, muestras.Count);
                    if (s > mejorS) { mejorS = s; mt = t; mb = bl; }
                }
            Tam = mt; Base = mb; plant = Plantillas(Tam, H, Base); memo.Clear(); memoDe = null;
            if (Log != null) Log("calibrado: tamaño " + Tam + " px, base " + Base + "/" + H + ", parecido medio " + mejorS.ToString("0.00"));
        }

        // ---------- aprender las letras reales del juego desde la propia captura ----------
        // En cada palabra que el OCR leyó y cuya cantidad de tramos coincide con sus letras, cada tramo
        // es una muestra real de esa letra. Se promedian por letra y reemplazan a la Arial de Windows.
        public int Aprendidas;
        public void Aprender(List<Banda> bandas, List<LineaCruda> ocr)
        {
            var muestras = new Dictionary<char, List<float[]>>();
            var anchos = new Dictionary<char, List<int>>();
            var huecos = new List<int>();
            foreach (var b in bandas)
            {
                var ts = Tramos(b);
                foreach (var l in ocr)
                    foreach (var w in l.Palabras)
                    {
                        float cy = w.Caja.Y + w.Caja.Height / 2;
                        if (cy < b.Y0 || cy >= b.Y1) continue;
                        float wx0 = w.Caja.X - b.X0, wx1 = w.Caja.Right - b.X0;
                        var dentro = new List<int[]>();
                        foreach (var t in ts) { float c = (t[0] + t[1]) / 2f; if (c >= wx0 - 1 && c <= wx1 + 1) dentro.Add(t); }
                        string txt = w.Texto;
                        if (dentro.Count == 0 || dentro.Count != txt.Length) continue;
                        for (int i = 1; i < dentro.Count; i++) huecos.Add(dentro[i][0] - dentro[i - 1][1]);
                        for (int i = 0; i < txt.Length; i++)
                        {
                            char ch = txt[i];
                            if (JUEGO.IndexOf(ch) < 0) continue;
                            int wr = dentro[i][1] - dentro[i][0];
                            var a = new float[wr * H];
                            for (int y = 0; y < H && y < b.H; y++) for (int x = 0; x < wr; x++) a[y * wr + x] = b.Alfa[y * b.W + dentro[i][0] + x];
                            List<float[]> lst; if (!muestras.TryGetValue(ch, out lst)) { lst = new List<float[]>(); muestras[ch] = lst; anchos[ch] = new List<int>(); }
                            lst.Add(a); anchos[ch].Add(wr);
                        }
                    }
            }
            Aprendidas = 0;
            foreach (var kv in muestras)
            {
                // ancho más repetido de esa letra; se promedian las muestras de ese ancho
                var cuenta = new Dictionary<int, int>();
                foreach (int wv in anchos[kv.Key]) { int n; cuenta.TryGetValue(wv, out n); cuenta[wv] = n + 1; }
                int wm = 0, nm = 0; foreach (var c in cuenta) if (c.Value > nm) { nm = c.Value; wm = c.Key; }
                var p = new Plantilla(); p.C = kv.Key; p.W = wm; p.A = new float[wm * H]; int n2 = 0;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (anchos[kv.Key][i] != wm) continue;
                    for (int j = 0; j < p.A.Length; j++) p.A[j] += kv.Value[i][j];
                    n2++;
                }
                for (int j = 0; j < p.A.Length; j++) p.A[j] /= n2;
                plant.RemoveAll(delegate (Plantilla q) { return q.C == kv.Key; });
                plant.Add(p); Aprendidas++;
            }
            if (huecos.Count > 0) { huecos.Sort(); Hueco = huecos[huecos.Count / 2]; }
            memo.Clear(); memoDe = null;
            if (Log != null) Log("letras aprendidas de la captura: " + Aprendidas + " · hueco entre letras " + Hueco);
        }

        // ---------- ÁRBITRO: decide las letras dudosas del OCR comparando píxeles ----------
        // El OCR lee bien casi todo, pero confunde / l I 1 | y se come letras repetidas (Bfff → Bff).
        // Cada variante de la palabra se dibuja con las letras aprendidas de la captura y se compara con la tinta real.
        const string DUDOSAS = "lI1|/!";
        public float Hueco = 1.5f; // separación típica entre letras (aprendida)

        float[] Dibujar(string w, out int ancho)
        {
            var ps = new List<Plantilla>();
            foreach (char ch in w)
            {
                Plantilla p = plant.Find(delegate (Plantilla q) { return q.C == ch; });
                if (p == null) { ancho = 0; return null; }
                ps.Add(p);
            }
            int g = (int)Math.Round(Hueco);
            ancho = 0; foreach (var p in ps) ancho += p.W; ancho += g * (ps.Count - 1);
            var a = new float[ancho * H];
            int x = 0;
            foreach (var p in ps)
            {
                for (int y = 0; y < H; y++) for (int i = 0; i < p.W; i++) a[y * ancho + x + i] = Math.Max(a[y * ancho + x + i], p.A[y * p.W + i]);
                x += p.W + g;
            }
            return a;
        }

        // parecido entre la palabra dibujada y la tinta real [x0, x1) de la banda (alineadas a la izquierda)
        float Parecido(Banda b, int x0, int x1, string w)
        {
            int ancho; var d = Dibujar(w, out ancho);
            if (d == null) return -2;
            int wr = x1 - x0, n = Math.Max(wr, ancho);
            float mejor = -2;
            for (int dx = -1; dx <= 1; dx++)
            {
                double sa = 0, sb = 0, sab = 0, saa = 0, sbb = 0; int k = 0;
                for (int y = 0; y < H && y < b.H; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int bx = x0 + x + dx;
                        float va = (x < wr && bx >= 0 && bx < b.W) ? b.Alfa[y * b.W + bx] : 0;
                        float vb = x < ancho ? d[y * ancho + x] : 0;
                        sa += va; sb += vb; sab += va * vb; saa += va * va; sbb += vb * vb; k++;
                    }
                double cov = sab - sa * sb / k, v1 = saa - sa * sa / k, v2 = sbb - sb * sb / k;
                if (v1 > 1e-6 && v2 > 1e-6) mejor = Math.Max(mejor, (float)(cov / Math.Sqrt(v1 * v2)));
            }
            return mejor;
        }

        static List<string> Variantes(string w)
        {
            var r = new List<string>();
            for (int i = 0; i < w.Length; i++)
            {
                if (DUDOSAS.IndexOf(w[i]) >= 0)
                    foreach (char c in DUDOSAS) if (c != w[i]) r.Add(w.Substring(0, i) + c + w.Substring(i + 1));
                // letra repetida: una más o una menos (ff ↔ fff)
                if (i > 0 && w[i] == w[i - 1] && char.IsLetter(w[i]) && (i + 1 >= w.Length || w[i + 1] != w[i]))
                {
                    r.Add(w.Substring(0, i + 1) + w[i] + w.Substring(i + 1));
                    r.Add(w.Substring(0, i) + w.Substring(i + 1));
                }
            }
            return r;
        }

        static bool Dudosa(string w)
        {
            if (w.IndexOfAny(DUDOSAS.ToCharArray()) >= 0) return true;
            for (int i = 1; i < w.Length; i++) if (w[i] == w[i - 1] && char.IsLetter(w[i])) return true;
            return false;
        }

        public int Cambios;
        // devuelve la palabra arbitrada (o la misma); x0/x1 en coordenadas de la banda
        public string Arbitrar(Banda b, int x0, int x1, string w)
        {
            if (w.Length == 0) return w;
            int a0 = Math.Max(0, x0), a1 = Math.Min(b.W, x1);
            while (a0 < a1 && !ColTinta(b, a0)) a0++;
            while (a1 > a0 && !ColTinta(b, a1 - 1)) a1--;
            if (a1 - a0 < 2) return w;
            // la caja del OCR cubre sólo lo que creyó ver: la palabra real sigue mientras haya tinta pegada
            int hueco = 0, x = a1;
            while (x < b.W) { if (ColTinta(b, x)) { a1 = x + 1; hueco = 0; } else if (++hueco > Hueco + 2) break; x++; }
            string r = w;

            // 1) barra inclinada al principio/fin de la palabra: "lblammyl" → "/blammy/", "Iracha" → "/racha"
            var ts = new List<int[]>();
            foreach (var t in Tramos(b)) if (t[1] > a0 && t[0] < a1) ts.Add(t);
            if (ts.Count >= 2)
            {
                if ("lI1|".IndexOf(r[0]) >= 0 && Inclinada(b, ts[0][0], ts[0][1])) r = "/" + r.Substring(1);
                int u = r.Length - 1; while (u > 0 && ".,;:!?)".IndexOf(r[u]) >= 0) u--;
                int tu = ts.Count - 1 - (r.Length - 1 - u);
                if (u > 0 && tu > 0 && "lI1|".IndexOf(r[u]) >= 0 && Inclinada(b, ts[tu][0], ts[tu][1])) r = r.Substring(0, u) + "/" + r.Substring(u + 1);
            }

            // 2) letras de palo repetidas (fff, ll, tt): en el juego van pegadas en un solo bloque y el OCR se
            //    come una. Se cuentan los PALOS verticales del bloque: si hay uno más que letras leídas, falta una.
            for (int i = 1; i < r.Length; i++)
            {
                char c = r[i];
                if (c != r[i - 1] || "fltij".IndexOf(c) < 0) continue;
                int k = 2; while (i + 1 < r.Length && r[i + 1] == c) { k++; i++; }
                // el bloque que está donde caen esas letras dentro de la palabra (posición proporcional)
                float centro = a0 + (a1 - a0) * ((i - (k - 1) / 2f) + 0.5f) / r.Length;
                int[] mejor = null; float dmin = float.MaxValue;
                foreach (var t in Tramos(b))
                {
                    if (t[1] <= a0 || t[0] >= a1) continue;
                    float d = t[0] <= centro && centro < t[1] ? 0 : Math.Min(Math.Abs(t[0] - centro), Math.Abs(t[1] - centro));
                    if (d < dmin) { dmin = d; mejor = t; }
                }
                if (mejor != null && Palos(b, mejor[0], mejor[1]) == k + 1) r = r.Substring(0, i + 1) + c + r.Substring(i + 1);
                break;
            }
            // 3) un bloque de "f" pegadas leído como UNA letra (Bfff → "BT"): la letra cae sobre un bloque
            //    con 2+ palos y separado de las vecinas → son tantas "f" como palos
            string letras = System.Text.RegularExpressions.Regex.Replace(r, @"[^A-Za-zÁÉÍÓÚÑÜáéíóúñü]", "");
            bool sospechosa = letras.Length > 0 && letras.Length <= 4 && !Ortografia.Valida(letras.ToLowerInvariant()); // "BT", nunca "Teléfono"
            for (int i = 0; i < r.Length && sospechosa; i++)
            {
                if ("TFt".IndexOf(r[i]) < 0) continue;
                float centro = a0 + (a1 - a0) * (i + 0.5f) / r.Length;
                foreach (var t in Tramos(b))
                {
                    if (!(t[0] <= centro && centro < t[1])) continue;
                    int pal = Palos(b, t[0], t[1]);
                    // ancho por palo como el de una "f" pegada (≈ un tercio del alto de letra), no una "m" ni una "T"
                    if (pal >= 2 && (t[1] - t[0]) <= pal * H * 0.45f) r = r.Substring(0, i) + new string('f', pal) + r.Substring(i + 1);
                    break;
                }
                break;
            }

            if (r != w) { Cambios++; if (Log != null) Log("árbitro: " + w + " → " + r); }
            return r;
        }

        // palos verticales de un tramo: columnas casi tan altas como la más alta, agrupadas
        static int Palos(Banda b, int x0, int x1)
        {
            var alto = new int[x1 - x0]; int mx = 0;
            for (int x = x0; x < x1; x++) { int n = 0; for (int y = 0; y < b.H; y++) if (b.Alfa[y * b.W + x] >= 0.5f) n++; alto[x - x0] = n; mx = Math.Max(mx, n); }
            if (mx < 6) return 0;
            int palos = 0; bool dentro = false;
            foreach (int n in alto) { bool p = n >= mx * 0.75f; if (p && !dentro) palos++; dentro = p; }
            return palos;
        }

        int AnchoDibujado(string w)
        {
            int ancho = 0;
            foreach (char ch in w)
            {
                Plantilla p = plant.Find(delegate (Plantilla q) { return q.C == ch; });
                if (p == null) return -1;
                ancho += p.W;
            }
            return ancho + (int)Math.Round(Hueco * (w.Length - 1));
        }

        // ¿el trazo [x0,x1) está inclinado como "/"? (arriba más a la derecha que abajo)
        static bool Inclinada(Banda b, int x0, int x1)
        {
            int ya = -1, yb = -1;
            for (int y = 0; y < b.H; y++) for (int x = x0; x < x1; x++) if (b.Alfa[y * b.W + x] >= 0.5f) { if (ya < 0) ya = y; yb = y; }
            if (ya < 0 || yb - ya < 5) return false;
            int alto = yb - ya, tercio = Math.Max(1, alto / 3);
            float cs = Centro(b, x0, x1, ya, ya + tercio), ci = Centro(b, x0, x1, yb - tercio, yb + 1);
            return cs - ci >= 1.5f && x1 - x0 >= 3;
        }

        static float Centro(Banda b, int x0, int x1, int y0, int y1)
        {
            double s = 0, n = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { float a = b.Alfa[y * b.W + x]; if (a >= 0.5f) { s += a * x; n += a; } }
            return n > 0 ? (float)(s / n) : 0;
        }

        static bool ColTinta(Banda b, int x)
        {
            for (int y = 0; y < b.H; y++) if (b.Alfa[y * b.W + x] >= 0.5f) return true;
            return false;
        }

        // ---------- lectura ----------
        public string Leer(Banda b, float umbralEspacio)
        {
            var ts = Tramos(b);
            var sb = new StringBuilder();
            for (int i = 0; i < ts.Count; i++)
            {
                if (i > 0 && ts[i][0] - ts[i - 1][1] >= umbralEspacio) sb.Append(' ');
                float s;
                sb.Append(Tramo(b.Alfa, b.W, ts[i][0], ts[i][1], 0, out s));
            }
            return sb.ToString();
        }

        // umbral de espacio: los huecos entre letras son chicos y entre palabras grandes (2 grupos)
        public static float UmbralEspacio(List<Banda> bandas, float tam)
        {
            var huecos = new List<int>();
            foreach (var b in bandas) { var ts = Tramos(b); for (int i = 1; i < ts.Count; i++) { int h = ts[i][0] - ts[i - 1][1]; if (h <= tam) huecos.Add(h); } }
            if (huecos.Count < 4) return tam * 0.22f;
            huecos.Sort();
            float a = huecos[0], c = huecos[huecos.Count - 1];
            for (int it = 0; it < 20; it++)
            {
                float t = (a + c) / 2; double sa = 0, sc = 0; int na = 0, nc = 0;
                foreach (int h in huecos) if (h < t) { sa += h; na++; } else { sc += h; nc++; }
                if (na > 0) a = (float)(sa / na); if (nc > 0) c = (float)(sc / nc);
            }
            return Math.Max(2.5f, Math.Min(tam * 0.4f, (a + c) / 2));
        }
    }
}
