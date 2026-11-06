// Motor OCR: usa SOLO el OCR que trae Windows 10/11 (Windows.Media.Ocr). Sin red, sin librerías externas.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Ocr;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace EditorRoles
{
    public class PalabraCruda
    {
        public string Texto;
        public RectangleF Caja;
    }

    public class LineaCruda
    {
        public string Texto;
        public RectangleF Caja; // en coordenadas de la imagen original
        public List<PalabraCruda> Palabras = new List<PalabraCruda>();
        public Color ColorLinea = Color.Empty; // medido por el lector por color (exacto)
        public Banda Banda;                    // la fila aislada de donde salió (para comparar letras)
    }

    public static class MotorOcr
    {
        // Ampliar la imagen antes de leer mejora mucho el texto pequeño del chat de SA-MP (medido: x3 es lo mejor).
        const int ESCALA = 3;

        // Espera una operación asíncrona de WinRT sin depender de awaiters del SDK.
        static T Esperar<T>(IAsyncOperation<T> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(3);
            if (op.Status != AsyncStatus.Completed)
            {
                string msg = op.ErrorCode != null ? op.ErrorCode.Message : op.Status.ToString();
                throw new Exception("El OCR de Windows falló: " + msg);
            }
            return op.GetResults();
        }

        public static List<string> Idiomas()
        {
            var l = new List<string>();
            try
            {
                foreach (var lang in OcrEngine.AvailableRecognizerLanguages) l.Add(lang.LanguageTag);
            }
            catch { }
            return l;
        }

        // Imagen limpia para el OCR: lo claro que tiene la sombra/contorno negro del chat abajo-derecha
        // pasa a negro; todo lo demás (el juego) queda blanco. Así los fondos claros dejan de confundir.
        public static Bitmap Mascara(Bitmap original)
        {
            var px = new Pixeles(original);
            var m = new Bitmap(px.W, px.H, PixelFormat.Format24bppRgb);
            var d = m.LockBits(new Rectangle(0, 0, px.W, px.H), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var fila = new byte[d.Stride];
                for (int y = 0; y < px.H; y++)
                {
                    for (int x = 0; x < px.W; x++)
                    {
                        bool texto = false;
                        if (px.Max(x, y) >= 90)
                        {
                            for (int k = 1; k <= 3 && !texto; k++)
                                texto = Osc(px, x + k, y + k) || Osc(px, x + k, y) || Osc(px, x, y + k);
                        }
                        byte v = texto ? (byte)0 : (byte)255;
                        fila[x * 3] = v; fila[x * 3 + 1] = v; fila[x * 3 + 2] = v;
                    }
                    System.Runtime.InteropServices.Marshal.Copy(fila, 0, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), d.Stride);
                }
            }
            finally { m.UnlockBits(d); }
            return m;
        }

        static bool Osc(Pixeles px, int x, int y)
        {
            return x < px.W && y < px.H && px.Max(x, y) < 60;
        }

        // Entrada principal: primero el lector por color (fila por fila, sin fondo); si no encuentra
        // el chat (captura reescalada, otra fuente), respaldo con la doble lectura del OCR de Windows.
        // todo lo que se leyó en la captura (incluye nombres sobre las cabezas): fuente de nombres
        public static List<string> TextosExtra = new List<string>();

        public static List<LineaCruda> LeerChat(Bitmap original, string idioma)
        {
            List<Banda> bandas;
            var limpia = Lector.Leer(original, idioma, out bandas);
            var doble = Leer(original, idioma);
            TextosExtra = new List<string>(); foreach (var l in doble) TextosExtra.Add(l.Texto);
            if (bandas.Count < 2) return doble; // no se encontró el chat: respaldo puro
            // control: el alto de fila tiene que coincidir con el espaciado de líneas que ve el OCR
            // (en capturas achicadas o borrosas el paso se puede equivocar y juntar dos líneas)
            var centros = new List<float>();
            foreach (var l in doble) if (Math.Abs(l.Caja.X - bandas[0].X0) < bandas[0].H * 3) centros.Add(l.Caja.Y + l.Caja.Height / 2);
            centros.Sort();
            var pasos = new List<float>();
            for (int i = 1; i < centros.Count; i++) { float d = centros[i] - centros[i - 1]; if (d > 4) pasos.Add(d); }
            if (pasos.Count >= 3)
            {
                pasos.Sort(); float paso = pasos[pasos.Count / 2], h = bandas[0].H;
                if (paso < h * 0.75f) return doble; // filas más altas que una línea = juntaría dos
            }
            // por cada fila del chat, la lectura más legible entre la limpia y la doble
            var res = new List<LineaCruda>();
            foreach (var b in bandas)
            {
                LineaCruda mejor = null; float sm = float.MinValue;
                foreach (var l in limpia) if (l.Banda == b) { float s = Puntaje(l.Texto); if (s > sm) { sm = s; mejor = l; } }
                foreach (var l in doble)
                {
                    float cy = l.Caja.Y + l.Caja.Height / 2;
                    // margen = el ancho de una hora "[19:02:11] " (el OCR a veces no la lee y la línea arranca más a la derecha)
                    if (cy < b.Y0 || cy >= b.Y1 || l.Caja.X > b.X0 + (b.Y1 - b.Y0) * 6) continue;
                    float s = Puntaje(l.Texto);
                    if (s > sm) { sm = s; mejor = l; }
                }
                // lectura débil (mucho más corta que lo que mide la fila): se lee esa fila sola,
                // limpia y también recortada del original, y gana la más legible
                int esperado = (int)(b.W / 8.5f);
                if (mejor == null || mejor.Texto.Length < esperado * 0.6f)
                {
                    var cands = new List<LineaCruda>();
                    List<int> offs; int ancho;
                    using (var una = Lector.ImagenLimpia(new List<Banda> { b }, out offs, out ancho))
                        foreach (var l in UnirFilas(LeerCrudo(una, idioma)))
                        {
                            foreach (var w in l.Palabras) w.Caja = new RectangleF(w.Caja.X - 12 + b.X0, w.Caja.Y - offs[0] + b.Y0, w.Caja.Width, w.Caja.Height);
                            cands.Add(l);
                        }
                    int m = 6, x0 = Math.Max(0, b.X0 - m), y0 = Math.Max(0, b.Y0 - m);
                    var rc = new Rectangle(x0, y0, Math.Min(original.Width - x0, b.W + 2 * m), Math.Min(original.Height - y0, b.H + 2 * m));
                    using (var rec = original.Clone(rc, original.PixelFormat))
                        foreach (var l in UnirFilas(LeerCrudo(rec, idioma)))
                        {
                            foreach (var w in l.Palabras) w.Caja = new RectangleF(w.Caja.X + x0, w.Caja.Y + y0, w.Caja.Width, w.Caja.Height);
                            cands.Add(l);
                        }
                    foreach (var l in cands) { float s = Puntaje(l.Texto); if (s > sm) { sm = s; mejor = l; } }
                    if (mejor == null || mejor.Texto.Trim().Length < 2) continue;
                    mejor.Caja = new RectangleF(b.X0, b.Y0, b.W, b.H);
                }
                mejor.ColorLinea = b.Principal; mejor.Banda = b;
                res.Add(mejor);
            }
            // árbitro de letras: aprende las letras de esta captura y decide las palabras dudosas (/ l I 1, ff/fff)
            try
            {
                var lr = new LectorLetras(); lr.Log = Lector.Log;
                lr.Calibrar(bandas);
                lr.Aprender(bandas, res);
                foreach (var l in res)
                {
                    if (l.Banda == null || l.Palabras.Count == 0) continue;
                    var partes = new List<string>();
                    foreach (var w in l.Palabras)
                    {
                        int x0 = (int)Math.Floor(w.Caja.X - l.Banda.X0) - 1, x1 = (int)Math.Ceiling(w.Caja.Right - l.Banda.X0) + 1;
                        w.Texto = lr.Arbitrar(l.Banda, x0, x1, w.Texto);
                        partes.Add(w.Texto);
                    }
                    l.Texto = string.Join(" ", partes.ToArray());
                }
            }
            catch { } // si algo falla, queda la lectura del OCR

            // rescate: una línea leída que empieza en la columna del chat pero no cayó en ninguna fila
            // por color nunca se pierde (su color se mide después, a la vieja usanza)
            float xCol = bandas[0].X0, hB = bandas[0].H;
            foreach (var l in doble)
            {
                float cy = l.Caja.Y + l.Caja.Height / 2;
                if (Math.Abs(l.Caja.X - xCol) > hB * 1.5f) continue;
                bool cubierta = false;
                foreach (var r in res) if (cy >= r.Caja.Y - 2 && cy < r.Caja.Bottom + 2) { cubierta = true; break; }
                if (!cubierta) res.Add(l);
            }
            res.Sort(delegate (LineaCruda x, LineaCruda y) { return x.Caja.Y.CompareTo(y.Caja.Y); });
            return res;
        }

        // Lee la imagen tal cual y la versión limpia, y por cada fila se queda con la lectura más legible.
        public static List<LineaCruda> Leer(Bitmap original, string idioma)
        {
            var a = UnirFilas(LeerCrudo(original, idioma));
            List<LineaCruda> b;
            using (var m = Mascara(original)) b = UnirFilas(LeerCrudo(m, idioma));
            var res = new List<LineaCruda>();
            var usadasB = new HashSet<LineaCruda>();
            foreach (var la in a)
            {
                LineaCruda mejor = la; float sm = Puntaje(la.Texto);
                foreach (var lb in b)
                {
                    if (!MismaFila(la, lb)) continue;
                    usadasB.Add(lb);
                    float s = Puntaje(lb.Texto);
                    if (s > sm) { sm = s; mejor = lb; }
                }
                res.Add(mejor);
            }
            foreach (var lb in b)
                if (!usadasB.Contains(lb))
                {
                    bool choca = false;
                    foreach (var r in res) if (MismaFila(r, lb)) { choca = true; break; }
                    if (!choca) res.Add(lb);
                }
            res.Sort(delegate (LineaCruda x, LineaCruda y) { return x.Caja.Y.CompareTo(y.Caja.Y); });
            return res;
        }

        static bool MismaFila(LineaCruda a, LineaCruda b)
        {
            float ca = a.Caja.Y + a.Caja.Height / 2, cb = b.Caja.Y + b.Caja.Height / 2;
            float h = Math.Max(4f, Math.Min(a.Caja.Height, b.Caja.Height));
            bool solapanX = a.Caja.X < b.Caja.Right && b.Caja.X < a.Caja.Right;
            return Math.Abs(ca - cb) < h * 0.5f && solapanX;
        }

        // Cuánto "parece español": palabras con vocal suman, basura (• » % 3E13) resta.
        static readonly System.Text.RegularExpressions.Regex PALABRA =
            new System.Text.RegularExpressions.Regex(@"^[A-Za-zÁÉÍÓÚÑÜáéíóúñü]+$");
        public static float Puntaje(string t)
        {
            float s = 0;
            foreach (var tok0 in t.Split(' '))
            {
                string tok = tok0.Trim(',', '.', ':', ';', '!', '?', '¡', '¿', '(', ')', '[', ']', '"', '*', '>');
                if (tok.Length == 0) continue;
                if (PALABRA.IsMatch(tok))
                {
                    bool vocal = tok.IndexOfAny("aeiouáéíóúAEIOUÁÉÍÓÚ".ToCharArray()) >= 0;
                    bool rara = System.Text.RegularExpressions.Regex.IsMatch(tok, @"[a-zñ][A-ZÑ]") ||
                                System.Text.RegularExpressions.Regex.IsMatch(tok, @"[bcdfghjklmnpqrstvwxyz]{4,}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    s += vocal && !rara ? tok.Length : -tok.Length * 0.5f;
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(tok, @"^[\d.,:$%/\-]+$")) s += tok.Length * 0.3f;
                else s -= tok.Length;
            }
            foreach (char c in t) if ("•»«%{}|~^=—\\".IndexOf(c) >= 0) s -= 3;
            return s;
        }

        // El OCR a veces parte una fila del chat en dos ("… recibe" / "el dinero..."): se vuelven a unir
        // si están a la misma altura y cerca. Los nombres sobre las cabezas quedan lejos y no se pegan.
        static List<LineaCruda> UnirFilas(List<LineaCruda> ls)
        {
            ls.Sort(delegate (LineaCruda x, LineaCruda y) { return x.Caja.X.CompareTo(y.Caja.X); });
            var res = new List<LineaCruda>();
            foreach (var l in ls)
            {
                LineaCruda dest = null;
                foreach (var r in res)
                {
                    float cr = r.Caja.Y + r.Caja.Height / 2, cl = l.Caja.Y + l.Caja.Height / 2;
                    float h = Math.Max(4f, Math.Min(r.Caja.Height, l.Caja.Height));
                    float hueco = l.Caja.X - r.Caja.Right;
                    if (Math.Abs(cr - cl) < h * 0.45f && hueco > -h && hueco < h * 4) { dest = r; break; }
                }
                if (dest == null) { res.Add(l); continue; }
                dest.Texto = dest.Texto + " " + l.Texto;
                dest.Palabras.AddRange(l.Palabras);
                dest.Caja = RectangleF.Union(dest.Caja, l.Caja);
            }
            res.Sort(delegate (LineaCruda x, LineaCruda y) { return x.Caja.Y.CompareTo(y.Caja.Y); });
            return res;
        }

        public static List<LineaCruda> LeerCrudo(Bitmap original, string idioma)
        {
            OcrEngine motor = null;
            if (!string.IsNullOrEmpty(idioma))
            {
                try { motor = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(idioma)); } catch { }
            }
            if (motor == null) motor = OcrEngine.TryCreateFromUserProfileLanguages();
            if (motor == null)
                throw new Exception("Windows no tiene ningún idioma de OCR instalado.\n\nConfiguración > Hora e idioma > Idioma > Agregar Español (incluye el reconocimiento óptico de caracteres).");

            int esc = ESCALA;
            uint max = OcrEngine.MaxImageDimension;
            while (esc > 1 && (original.Width * esc > max || original.Height * esc > max)) esc--;

            string tmp = Path.Combine(Path.GetTempPath(), "editor-roles-" + Guid.NewGuid().ToString("N") + ".png");
            var salida = new List<LineaCruda>();
            try
            {
                using (var grande = new Bitmap(original.Width * esc, original.Height * esc, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(grande))
                    using (var attr = new ImageAttributes())
                    {
                        g.Clear(Color.Black); // los PNG con transparencia quedan sobre negro
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        attr.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(original, new Rectangle(0, 0, grande.Width, grande.Height),
                            0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attr);
                    }
                    grande.Save(tmp, ImageFormat.Png);
                }

                var archivo = Esperar(StorageFile.GetFileFromPathAsync(tmp));
                OcrResult res;
                using (var flujo = Esperar(archivo.OpenAsync(FileAccessMode.Read)))
                {
                    var dec = Esperar(BitmapDecoder.CreateAsync(flujo));
                    using (var sb = Esperar(dec.GetSoftwareBitmapAsync()))
                        res = Esperar(motor.RecognizeAsync(sb));
                }

                foreach (var linea in res.Lines)
                {
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    foreach (var w in linea.Words)
                    {
                        var r = w.BoundingRect;
                        x0 = Math.Min(x0, (float)r.X); y0 = Math.Min(y0, (float)r.Y);
                        x1 = Math.Max(x1, (float)(r.X + r.Width)); y1 = Math.Max(y1, (float)(r.Y + r.Height));
                    }
                    if (x0 == float.MaxValue) continue;
                    var lc = new LineaCruda();
                    lc.Texto = linea.Text;
                    foreach (var w in linea.Words)
                    {
                        var pc = new PalabraCruda();
                        pc.Texto = w.Text;
                        pc.Caja = new RectangleF((float)w.BoundingRect.X / esc, (float)w.BoundingRect.Y / esc,
                            (float)w.BoundingRect.Width / esc, (float)w.BoundingRect.Height / esc);
                        lc.Palabras.Add(pc);
                    }
                    lc.Caja = new RectangleF(x0 / esc, y0 / esc, (x1 - x0) / esc, (y1 - y0) / esc);
                    salida.Add(lc);
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            salida.Sort(delegate (LineaCruda a, LineaCruda b) { return a.Caja.Y.CompareTo(b.Caja.Y); });
            return salida;
        }
    }
}
