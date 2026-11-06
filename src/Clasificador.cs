// Lectura del chat: 1) quita la hora por POSICIÓN, 2) descarta lo que no es chat (nombres sobre las
// cabezas, HUD, calles), 3) decide el tipo por COLOR y confirma con el formato.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EditorRoles
{
    public enum Tipo { Me, Do, Habla, Otra }
    public enum Familia { Morado, Amarillo, Verde, Gris, Otro }

    public class Linea
    {
        public bool Usar;
        public Tipo Tipo;
        public string Texto;
        public Color Color;
        public RectangleF Caja;
        public Color ColorCrudo;
        public Familia Fam;
        // lo que detectó el OCR, para "Restaurar detección"
        public bool UsarDet; public Tipo TipoDet; public string TextoDet; public Color ColorDet;

        public Linea Copia() { return (Linea)MemberwiseClone(); }

        public static string Nombre(Tipo t)
        {
            switch (t) { case Tipo.Me: return "me"; case Tipo.Do: return "do"; case Tipo.Habla: return "habla"; default: return "otra"; }
        }
        public static Tipo DeNombre(string s)
        {
            switch (s) { case "me": return Tipo.Me; case "do": return Tipo.Do; case "habla": return Tipo.Habla; default: return Tipo.Otra; }
        }
    }

    // Copia de los píxeles de una imagen para leer colores rápido.
    public class Pixeles
    {
        public int W, H; public int[] Argb;
        public Pixeles(Bitmap b)
        {
            W = b.Width; H = b.Height; Argb = new int[W * H];
            using (var c = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(c)) { g.Clear(Color.Black); g.DrawImage(b, 0, 0, W, H); }
                var d = c.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < H; y++)
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), Argb, y * W, W);
                }
                finally { c.UnlockBits(d); }
            }
        }
        public int Max(int x, int y)
        {
            int p = Argb[y * W + x];
            return Math.Max((p >> 16) & 255, Math.Max((p >> 8) & 255, p & 255));
        }
    }

    public class ResultadoLectura
    {
        public List<Linea> Lineas = new List<Linea>();
        public int Descartadas;
    }

    public static class Clasificador
    {
        public static readonly Color MORADO = Color.FromArgb(0xC2, 0xA2, 0xDA); // /me y /do de SA-MP

        static readonly Color[] GRISES = {
            Color.FromArgb(255,255,255), Color.FromArgb(0xE6,0xE6,0xE6), Color.FromArgb(0xC8,0xC8,0xC8),
            Color.FromArgb(0xAA,0xAA,0xAA), Color.FromArgb(0x8C,0x8C,0x8C), Color.FromArgb(0x6E,0x6E,0x6E)
        };

        // un trozo de hora: "[22:47:57]", "[22<4803]", "22:47", ":15]"…
        static readonly Regex TOKEN_HORA = new Regex(@"^([\[\(]?[\d:;.,<>lIoO|•·]+[\]\)]?)(.*)$");
        // el verbo de habla tiene que estar ANTES de los primeros dos puntos
        static readonly Regex VERBO = new Regex(@"\b(d[iíl1I]ce|grita|susurra|susurras|murmura|exclama|responde|pregunta|en voz alta)\b", RegexOptions.IgnoreCase);
        static readonly Regex DOS_MAYUS = new Regex(@"^[A-ZÁÉÍÓÚÑ][\wáéíóúñ']+[ _\-][A-ZÁÉÍÓÚÑ][\wáéíóúñ'\-]+\b");
        static readonly Regex NOMBRE_FINAL = new Regex(@"\s([A-ZÁÉÍÓÚÑ][\wáéíóúñ'\-]+[ _][A-ZÁÉÍÓÚÑ][\wáéíóúñ'\-]+)\s*\)*\s*$");
        const string ESTRELLAS = "*•·'\"`´’‘“”";

        public static Action<string> Log; // sólo para verificar

        static void L(string m) { if (Log != null) Log(m); }

        class Pre
        {
            public LineaCruda Cruda; public bool Hora; public float Izq; public float Alto;
            public string Texto; public RectangleF Caja;
        }

        public static ResultadoLectura Procesar(List<LineaCruda> crudas, Pixeles px)
        {
            var res = new ResultadoLectura();
            var pres = new List<Pre>();
            foreach (var cr in crudas)
            {
                var p = Partir(cr);
                if (p == null) { res.Descartadas++; L("vacia: " + cr.Texto); continue; }
                pres.Add(p);
            }
            if (pres.Count == 0) return res;

            // --- filtro 1: altura de letra (el HUD, el dinero y las calles son mucho más grandes) ---
            var altos = new List<float>(); foreach (var p in pres) altos.Add(p.Alto);
            float hMed = Mediana(altos);
            var f1 = new List<Pre>();
            foreach (var p in pres)
                if (p.Cruda.Banda != null || (p.Alto <= hMed * 1.6f && p.Alto >= hMed * 0.55f)) f1.Add(p); else { res.Descartadas++; L("altura " + p.Alto + "/" + hMed + ": " + p.Texto); }

            // --- filtro 2: la columna del chat (los nombres sobre las cabezas quedan fuera) ---
            // Dos columnas válidas: donde empieza la hora y donde empieza el texto (el OCR a veces no lee la hora).
            int conHora = 0; foreach (var p in f1) if (p.Hora) conHora++;
            float tol = hMed * 2.5f;
            float colHora = float.NaN, colTexto;
            if (conHora >= 2)
            {
                var iz = new List<float>(); var tx = new List<float>();
                foreach (var p in f1) if (p.Hora) { iz.Add(p.Izq); tx.Add(p.Caja.X); }
                colHora = Mediana(iz); colTexto = Mediana(tx);
            }
            else colTexto = ModaIzquierda(f1, tol);
            var f2 = new List<Pre>();
            foreach (var p in f1)
            {
                // hora ilegible ("(23'00'5zlt]"): empieza en la columna de la hora → se corta por POSICIÓN
                if (!p.Hora && !float.IsNaN(colHora) && Math.Abs(p.Izq - colHora) <= tol && Math.Abs(p.Caja.X - colTexto) > tol * 0.5f)
                {
                    var q = CortarPorPosicion(p.Cruda, colTexto - hMed * 0.3f);
                    if (q != null) { q.Hora = true; p.Texto = q.Texto; p.Caja = q.Caja; p.Hora = true; }
                }
                bool enColumna = (p.Hora && Math.Abs(p.Izq - colHora) <= tol) || Math.Abs(p.Caja.X - colTexto) <= tol;
                if (enColumna || p.Cruda.Banda != null) f2.Add(p); // las filas del lector por color ya están en el chat
                else { res.Descartadas++; L("columna x=" + p.Izq + " hora=" + p.Hora + ": " + p.Cruda.Texto); }
            }

            // --- nombres aprendidos de la captura + diccionario de Windows ---
            var todos = new List<string>(MotorOcr.TextosExtra);
            foreach (var p in f2) todos.Add(p.Texto);
            var nombres = Correccion.Nombres(todos);
            foreach (var kv in nombres) L("nombre: " + kv.Key + " → " + kv.Value);

            // --- clasificar por color + formato ---
            Linea prev = null;
            foreach (var p in f2)
            {
                string t = Corregir(Correccion.AplicarNombres(Correccion.Texto(Corregir(p.Texto)), nombres));
                if (t.Length < 2) { res.Descartadas++; continue; }
                var l = new Linea();
                l.Caja = p.Caja;
                l.ColorCrudo = p.Cruda.ColorLinea.IsEmpty ? ColorDe(px, p.Caja) : p.Cruda.ColorLinea;
                l.Fam = FamiliaDe(l.ColorCrudo);
                Decidir(l, t, prev);
                l.Usar = l.Tipo != Tipo.Otra;
                l.UsarDet = l.Usar; l.TipoDet = l.Tipo; l.TextoDet = l.Texto; l.ColorDet = l.Color;
                res.Lineas.Add(l);
                prev = l;
            }
            return res;
        }

        // Separa la hora (por palabras, no por texto) y devuelve el contenido con su caja.
        static Pre Partir(LineaCruda cr)
        {
            var ws = cr.Palabras;
            if (ws.Count == 0) return null;
            int k = 0; string hora = ""; string resto = null;
            for (; k < Math.Min(4, ws.Count); k++)
            {
                var m = TOKEN_HORA.Match(ws[k].Texto);
                if (!m.Success) break;
                hora += m.Groups[1].Value;
                if (m.Groups[2].Value.Length > 0) { resto = m.Groups[2].Value; k++; break; }
                string g1 = m.Groups[1].Value;
                if (g1.EndsWith("]") || g1.EndsWith(")")) { k++; break; }
            }
            int dig = 0; foreach (char c in hora) if (char.IsDigit(c)) dig++;
            bool tieneHora = dig >= 3 && (hora.IndexOfAny(new[] { ':', ']', ';', '<' }) >= 0);
            if (!tieneHora) { k = 0; resto = null; }

            var partes = new List<string>();
            if (resto != null) partes.Add(resto);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            var altos = new List<float>();
            for (int i = (resto != null ? k - 1 : k); i < ws.Count; i++)
            {
                var r = ws[i].Caja;
                if (i >= k) partes.Add(ws[i].Texto);
                x0 = Math.Min(x0, r.X); y0 = Math.Min(y0, r.Y); x1 = Math.Max(x1, r.Right); y1 = Math.Max(y1, r.Bottom);
                altos.Add(r.Height);
            }
            if (partes.Count == 0 || x0 == float.MaxValue) return null;
            var p = new Pre();
            p.Cruda = cr; p.Hora = tieneHora;
            p.Izq = ws[0].Caja.X;
            p.Alto = Mediana(altos);
            p.Texto = string.Join(" ", partes.ToArray());
            p.Caja = new RectangleF(x0, y0, x1 - x0, y1 - y0);
            return p;
        }

        static Pre CortarPorPosicion(LineaCruda cr, float xTexto)
        {
            var partes = new List<string>();
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var w in cr.Palabras)
            {
                if (w.Caja.Right <= xTexto) continue; // palabra de la hora
                string t = w.Texto;
                if (partes.Count == 0)
                {
                    int cierre = t.IndexOf(']');
                    if (cierre >= 0 && cierre < t.Length - 1 && Regex.IsMatch(t.Substring(0, cierre), @"\d")) t = t.Substring(cierre + 1);
                    else if (cierre == t.Length - 1) continue;
                }
                partes.Add(t);
                x0 = Math.Min(x0, w.Caja.X); y0 = Math.Min(y0, w.Caja.Y); x1 = Math.Max(x1, w.Caja.Right); y1 = Math.Max(y1, w.Caja.Bottom);
            }
            if (partes.Count == 0) return null;
            var p = new Pre();
            p.Texto = string.Join(" ", partes.ToArray());
            p.Caja = new RectangleF(x0, y0, x1 - x0, y1 - y0);
            return p;
        }

        static void Decidir(Linea l, string t, Linea prev)
        {
            string cuerpo = t.TrimStart(ESTRELLAS.ToCharArray()).TrimStart();
            bool continua = cuerpo.StartsWith("...") || cuerpo.StartsWith("…");
            bool mismoColor = prev != null && prev.Fam == l.Fam && Distancia(prev.ColorCrudo, l.ColorCrudo) < 50 * 50;
            l.Color = l.ColorCrudo;

            // segunda mitad de la línea anterior: "... en el bolsillo." (aunque el OCR se coma los puntos:
            // la anterior terminaba en "..." y ésta sigue en minúscula con el mismo color)
            if (!continua && mismoColor && prev.Texto.TrimEnd().EndsWith("...") && cuerpo.Length > 0 && char.IsLower(cuerpo[0]))
            { continua = true; cuerpo = "... " + cuerpo; }
            if (continua && mismoColor && prev.Tipo != Tipo.Otra)
            {
                l.Tipo = prev.Tipo; l.Color = prev.Color;
                l.Texto = l.Tipo == Tipo.Do ? ArreglarDo(cuerpo) : cuerpo;
                return;
            }
            if (cuerpo.StartsWith("(("))
            {
                l.Tipo = Tipo.Otra; l.Texto = cuerpo; l.Color = Ajustar(l.ColorCrudo); return; // OOC, MP
            }
            switch (l.Fam)
            {
                case Familia.Morado:
                    l.Color = MORADO;
                    if (cuerpo.StartsWith(">")) { l.Tipo = Tipo.Me; l.Texto = cuerpo; return; }
                    // acción automática del servidor con el ">" perdido: morada, SIN "*" y en minúscula ("apaga el motor…")
                    if (t.Length > 0 && ESTRELLAS.IndexOf(t[0]) < 0 && char.IsLower(cuerpo[0]) && !cuerpo.Contains("(("))
                    { l.Tipo = Tipo.Me; l.Texto = "> " + cuerpo; return; }
                    if (EsHabla(cuerpo) && Regex.IsMatch(cuerpo, "susurr", RegexOptions.IgnoreCase)) { l.Tipo = Tipo.Habla; l.Texto = "* " + cuerpo; return; }
                    if (cuerpo.Contains("((") || cuerpo.TrimEnd().EndsWith("))")) l.Tipo = Tipo.Do;
                    else if (DOS_MAYUS.IsMatch(cuerpo)) l.Tipo = Tipo.Me;
                    else l.Tipo = Tipo.Do;
                    l.Texto = "* " + (l.Tipo == Tipo.Do ? ArreglarDo(cuerpo) : cuerpo);
                    return;
                case Familia.Amarillo:
                    if (Regex.IsMatch(cuerpo, "susurr", RegexOptions.IgnoreCase))
                    { l.Tipo = Tipo.Habla; l.Texto = "* " + cuerpo; return; }
                    // llamada: "[Teléfono: Hollow]: ¿Qué pasa?" / "Fulano dice (por teléfono): …"
                    if (Regex.IsMatch(cuerpo, @"^\[Tel[eé]fono|\(por tel[eé]fono\)", RegexOptions.IgnoreCase))
                    { l.Tipo = Tipo.Habla; l.Texto = cuerpo; return; }
                    break;
                case Familia.Gris:
                    l.Color = Ajustar(l.ColorCrudo);
                    if (EsHabla(cuerpo)) { l.Tipo = Tipo.Habla; l.Texto = cuerpo; return; }
                    break;
                case Familia.Verde:
                    if (Saturacion(l.ColorCrudo) >= 0.5f && AccionVerde(cuerpo)) { l.Tipo = Tipo.Do; l.Texto = cuerpo.Contains("((") ? ArreglarDo(cuerpo) : cuerpo; return; }
                    break;
            }
            l.Tipo = Tipo.Otra; l.Texto = t;
        }

        // "Nombre Apellido dice [bajo]: …", "… en voz alta: …", "Le susurras a X: …"
        static bool EsHabla(string s)
        {
            if (s.StartsWith("[")) return false; // [TVR] Fulano: … = aviso del servidor
            // "Nombre Apellido dice …" aunque el OCR haya perdido los dos puntos
            if (Regex.IsMatch(s, @"^[A-ZÁÉÍÓÚÑ][\wáéíóúñ']+[ _][A-ZÁÉÍÓÚÑ][\wáéíóúñ']+ (dice|grita|susurra|murmura|exclama)")) return true;
            int dp = s.IndexOf(':');
            if (dp <= 0 || dp > 80) return false;
            return VERBO.IsMatch(s.Substring(0, dp));
        }

        public static Familia FamiliaDe(Color c)
        {
            int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
            if (mx < 70) return Familia.Otro;
            float s = mx == 0 ? 0 : (mx - mn) / (float)mx;
            float h = c.GetHue();
            if (s < 0.14f) return Familia.Gris;
            if (h >= 250 && h <= 315 && s < 0.65f) return Familia.Morado;
            if (h >= 45 && h <= 70 && s > 0.45f) return Familia.Amarillo;
            if (h >= 72 && h <= 165 && s > 0.3f) return Familia.Verde;
            return Familia.Otro;
        }

        static float Saturacion(Color c)
        {
            int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
            return mx == 0 ? 0 : (mx - mn) / (float)mx;
        }

        public static bool EsMorado(Color c) { return FamiliaDe(c) == Familia.Morado; }

        // Verde sirve para /do y para avisos del servidor: los avisos llevan [etiqueta], ":", un comando, $ o un link.
        static bool AccionVerde(string s)
        {
            if (Regex.IsMatch(s, @"^Has\s")) return false; // "Has entregado / activado / recuperado…" = aviso
            if (s.StartsWith("[") || s.Contains("http") || s.Contains("/") || s.Contains("$") || s.Contains("_")) return false;
            if (Regex.IsMatch(s, @"\d{3,}")) return false; // códigos, direcciones, archivos
            if (Regex.IsMatch(s, @"(Avenue|Street|Boulevard|Road|Drive|Lane|Way|Place)")) return false; // dirección
            if (s.TrimEnd().EndsWith("))")) return true;
            return !s.Contains(":");
        }

        // errores típicos del OCR
        // Windows sin el lector en español lee con el de otro idioma (inglés): no conoce las tildes del español y
        // las cambia por las suyas (å ä à → á, ë → é, ö ò → ó, ì ï → í, ù → ú), lee "lo" como "10" y "¡" como "j".
        // Son errores fijos, así que se devuelven a español. (Medido 2026-11-06: lector inglés 97.0 % en fondo negro.)
        static string Espanol(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case 'å': case 'ä': case 'à': case 'â': case 'ã': c = 'á'; break;
                    case 'Å': case 'Ä': case 'À': case 'Â': case 'Ã': c = 'Á'; break;
                    case 'ë': case 'è': case 'ê': c = 'é'; break;
                    case 'Ë': case 'È': case 'Ê': c = 'É'; break;
                    case 'ï': case 'ì': case 'î': c = 'í'; break;
                    case 'ö': case 'ò': case 'ô': case 'õ': c = 'ó'; break;
                    case 'Ö': case 'Ò': case 'Ô': case 'Õ': c = 'Ó'; break;
                    case 'ù': case 'û': c = 'ú'; break;
                    case 'ü':
                        // la ü existe en español sólo en "güe/güi" (pingüino); si no, es una ú mal leída
                        bool gue = i > 0 && (s[i - 1] == 'g' || s[i - 1] == 'G') && i + 1 < s.Length && "eéiíEI".IndexOf(s[i + 1]) >= 0;
                        if (!gue) c = 'ú'; break;
                }
                sb.Append(c);
            }
            s = sb.ToString();
            s = Regex.Replace(s, @"(?<=\b(se|me|te|le|no|y|a|que|de|por|lo|todo|es)\s)10\b", "lo");   // "se 10 pone" → "se lo pone"
            s = Regex.Replace(s, @"\b10(?=\s+(que|qué|mismo|más|mas|siento|sé|se|tengo|hago|dije|veo)\b)", "lo");
            s = Regex.Replace(s, @"(^|[\s(:])j(?=[A-ZÁÉÍÓÚÑ])", "$1¡");                                   // "jQue" → "¡Que"
            return s;
        }

        static string Corregir(string s)
        {
            s = s.Trim();
            // restos de una hora mal leída: "1:59]", "[22<4803]" (sin letras antes del primer "]")
            s = Regex.Replace(s, @"^[\s\*•'""`]*[\d:;.,<>\[\(\s']{1,12}\]\s*", "");
            // hora entre corchetes con basura del OCR: "[21. •20:45]", "[2l:20 :45]" → fuera (≥4 cifras dentro)
            var mh = Regex.Match(s, @"^[\s\*•·'""`]*[\[\(]([^\]\)A-Za-zÁÉÍÓÚÑáéíóúñ]{0,6}[\d lIoO:;.,•·<>'|\s]{3,16})[\]\)]\s*");
            if (mh.Success && Regex.Matches(mh.Groups[1].Value, @"\d").Count >= 4) s = s.Substring(mh.Length);
            s = Regex.Replace(s, "^[" + Regex.Escape(ESTRELLAS) + "]+\\s*", "* ");
            s = Regex.Replace(s, @"(^|[\s(])i(?=[A-ZÁÉÍÓÚÑ])", "$1¡");
            s = Regex.Replace(s, @"\bIa\b", "la");
            s = Regex.Replace(s, @"\bIJ(?=[a-záéíóúñ])", "U"); // "IJhum" → "Uhum", "IJsa" → "Usa"
            s = Espanol(s);
            s = Regex.Replace(s, @"\bI(me|do|ame|pm|low|shout)\b", "/$1"); // "usar Ime y Ido" → "/me", "/do" (la barra leída como I)
            s = Regex.Replace(s, @"\bIas\b", "las");
            s = Regex.Replace(s, @"\(\s*\(", "((");
            s = Regex.Replace(s, @"\)\s*\)", "))");
            s = Regex.Replace(s, @"\(\(\s*", "(( ");
            s = Regex.Replace(s, @"\s*\)\)", " ))");
            s = Regex.Replace(s, @"\s+([,.;:!?])", "$1");
            s = Regex.Replace(s, @"\s{2,}", " ");
            return s.Trim();
        }

        // /do: "* texto (( Nombre Apellido ))". Si el OCR perdió los paréntesis y termina en un nombre, se reponen.
        static string ArreglarDo(string c)
        {
            c = c.Trim();
            if (c.Contains("((") && !c.EndsWith("))")) c = c + " ))";
            else if (!c.Contains("((") && c.EndsWith("))")) c = c.Substring(0, c.Length - 2).TrimEnd();
            if (!c.Contains("(("))
            {
                var m = NOMBRE_FINAL.Match(c);
                if (m.Success && m.Index > 0) c = c.Substring(0, m.Index).TrimEnd() + " (( " + m.Groups[1].Value + " ))";
            }
            return c;
        }

        static Color Ajustar(Color c)
        {
            Color mejor = c; int dmin = int.MaxValue;
            foreach (var p in GRISES)
            {
                int d = Distancia(p, c);
                if (d < dmin) { dmin = d; mejor = p; }
            }
            return dmin <= 22 * 22 ? mejor : c;
        }

        static int Distancia(Color a, Color b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return dr * dr + dg * dg + db * db;
        }

        static float Mediana(List<float> v)
        {
            if (v.Count == 0) return 0;
            var c = new List<float>(v); c.Sort();
            return c[c.Count / 2];
        }

        static float ModaIzquierda(List<Pre> ps, float tol)
        {
            float mejor = 0; int nMejor = -1;
            foreach (var a in ps)
            {
                int n = 0; foreach (var b in ps) if (Math.Abs(a.Izq - b.Izq) <= tol) n++;
                if (n > nMejor || (n == nMejor && a.Izq < mejor)) { nMejor = n; mejor = a.Izq; }
            }
            return mejor;
        }

        // Color del texto. Las letras del chat tienen contorno/sombra negra: el cuerpo de la letra es lo claro
        // que tiene oscuro a AMBOS lados; si no alcanza, se relaja la condición.
        public static Color ColorDe(Pixeles px, RectangleF caja)
        {
            int x0 = Math.Max(0, (int)caja.X - 1), y0 = Math.Max(0, (int)caja.Y - 1);
            int x1 = Math.Min(px.W - 1, (int)(caja.Right + 1)), y1 = Math.Min(px.H - 1, (int)(caja.Bottom + 1));
            for (int modo = 0; modo < 3; modo++)
            {
                var cuenta = new Dictionary<int, int>();
                var suma = new Dictionary<int, long[]>();
                int total = 0;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int p = px.Argb[y * px.W + x];
                        int r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
                        if (Math.Max(r, Math.Max(g, b)) < 90) continue;
                        if (modo == 0 && !EntreOscuros(px, x, y)) continue;
                        if (modo == 1 && !TocaOscuro(px, x, y)) continue;
                        int k = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                        int n; cuenta.TryGetValue(k, out n); cuenta[k] = n + 1;
                        long[] s; if (!suma.TryGetValue(k, out s)) { s = new long[3]; suma[k] = s; }
                        s[0] += r; s[1] += g; s[2] += b; total++;
                    }
                if (total < 12 && modo < 2) continue;
                if (cuenta.Count == 0) continue;
                int mk = -1, mn = -1;
                foreach (var kv in cuenta)
                    if (kv.Value > mn || (kv.Value == mn && Brillo(kv.Key) > Brillo(mk))) { mn = kv.Value; mk = kv.Key; }
                var sm = suma[mk];
                return Color.FromArgb((int)(sm[0] / mn), (int)(sm[1] / mn), (int)(sm[2] / mn));
            }
            return Color.White;
        }

        static int Brillo(int k) { return k < 0 ? -1 : ((k >> 8) & 15) + ((k >> 4) & 15) + (k & 15); }

        static bool Oscuro(Pixeles px, int x, int y)
        {
            if (x < 0 || y < 0 || x >= px.W || y >= px.H) return false;
            return px.Max(x, y) < 60;
        }

        static bool EntreOscuros(Pixeles px, int x, int y)
        {
            bool iz = false, de = false, ar = false, ab = false;
            for (int d = 1; d <= 3; d++)
            {
                iz |= Oscuro(px, x - d, y); de |= Oscuro(px, x + d, y);
                ar |= Oscuro(px, x, y - d); ab |= Oscuro(px, x, y + d);
            }
            return (iz && de) || (ar && ab) || (de && ab);
        }

        static bool TocaOscuro(Pixeles px, int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if (Oscuro(px, x + dx, y + dy)) return true;
            return false;
        }
    }
}
