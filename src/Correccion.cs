// Corrección de lo que el lector duda, con dos fuentes LOCALES:
//   · el corrector ortográfico que trae Windows (ISpellChecker, el mismo de Office/Edge; sin descargas),
//   · los nombres que aparecen en la propia captura (si un nombre se repite, arregla las lecturas malas).
// Sólo se aceptan cambios de "confusión típica" del OCR: espacios pegados, acentos, I/l/1//, IJ/U, rn/m.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;

namespace EditorRoles
{
    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerFactory
    {
        [return: MarshalAs(UnmanagedType.Interface)] object get_SupportedLanguages();
        int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string lang);
        [return: MarshalAs(UnmanagedType.Interface)] ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string lang);
    }
    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellChecker
    {
        [return: MarshalAs(UnmanagedType.LPWStr)] string get_LanguageTag();
        [return: MarshalAs(UnmanagedType.Interface)] IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);
        [return: MarshalAs(UnmanagedType.Interface)] IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }
    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumSpellingError { [PreserveSig] int Next(out ISpellingError e); }
    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellingError { uint get_StartIndex(); uint get_Length(); int get_CorrectiveAction(); [return: MarshalAs(UnmanagedType.LPWStr)] string get_Replacement(); }
    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")] class SpellCheckerFactoryCom { }

    public static class Ortografia
    {
        static ISpellChecker sc; static bool probado;
        static readonly Dictionary<string, bool> memo = new Dictionary<string, bool>();

        static ISpellChecker Corrector()
        {
            if (probado) return sc;
            probado = true;
            try
            {
                var f = (ISpellCheckerFactory)new SpellCheckerFactoryCom();
                foreach (var l in new[] { "es-ES", "es-MX", "es" }) if (f.IsSupported(l) != 0) { sc = f.CreateSpellChecker(l); break; }
            }
            catch { sc = null; } // Windows sin corrector: se sigue sin él
            return sc;
        }

        public static bool Disponible { get { return Corrector() != null; } }

        public static bool Valida(string w)
        {
            var c = Corrector(); if (c == null) return true;
            bool v; if (memo.TryGetValue(w, out v)) return v;
            try { ISpellingError e; v = c.Check(w).Next(out e) != 0; } catch { v = true; }
            memo[w] = v; return v;
        }

        public static List<string> Sugerencias(string w)
        {
            var r = new List<string>();
            var c = Corrector(); if (c == null) return r;
            try
            {
                var en = c.Suggest(w);
                var buf = new string[1]; IntPtr n = Marshal.AllocCoTaskMem(4);
                try { while (en.Next(1, buf, n) == 0 && r.Count < 8) r.Add(buf[0]); }
                finally { Marshal.FreeCoTaskMem(n); }
            }
            catch { }
            return r;
        }
    }

    public static class Correccion
    {
        static string SinAcentos(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString();
        }

        // forma "confundible": lo que el OCR no distingue se iguala
        static string Forma(string s)
        {
            s = SinAcentos(s).Replace(" ", "");
            s = s.Replace("IJ", "U").Replace("rn", "m").Replace("I", "l").Replace("1", "l").Replace("|", "l").Replace("/", "l");
            return s.ToLowerInvariant();
        }

        static readonly string[] CORTAS = { "a", "y", "o", "e", "u", "de", "la", "el", "en", "lo", "se", "tu", "su", "mi", "al", "del", "los", "las", "un", "con", "por", "que", "le", "les", "me", "te", "ya", "no", "es" };

        static readonly Regex PALABRA = new Regex(@"[A-Za-zÁÉÍÓÚÑÜáéíóúñü/|]+");

        // corrige una palabra si el diccionario propone algo que sólo difiere en confusiones típicas
        public static string Palabra(string w, bool puedeSerComando) { return Palabra(w, puedeSerComando, false); }

        // reparar=true sólo cuando el OCR rompió la palabra de forma evidente (un signo metido en medio):
        // ahí sí se permite reponer acentos. En el resto NO se corrige la ortografía del jugador.
        public static string Palabra(string w, bool puedeSerComando, bool reparar)
        {
            if (w.Length < 3) return w;
            // comando: "Usa Iracha" / "(Ilogout)" → "/racha", "/logout" (el OCR lee la barra como I)
            if (puedeSerComando && w.Length > 3 && w[0] == 'I' && char.IsLower(w[1]))
            {
                string conL = "l" + w.Substring(1);
                return Ortografia.Valida(conL) ? conL : "/" + w.Substring(1);
            }
            if (Ortografia.Valida(w)) return w;
            // nombres propios (mayúscula inicial): no se les cambian acentos ni se parten
            if (char.IsUpper(w[0])) return w;
            string f = Forma(w);
            foreach (var s in Ortografia.Sugerencias(w))
            {
                if (Forma(s) != f) continue;
                if (s.Contains(" ")) continue;
                if (char.IsUpper(s[0]) != char.IsUpper(w[0])) continue;              // mayúsculas: como las escribió                                   // separar: sólo la regla de abajo
                if (!reparar && SinAcentos(s) == SinAcentos(w)) continue;        // acentos del jugador: no se tocan
                return s;
            }
            // dos palabras pegadas: "debilletes" → "de billetes" (las dos mitades tienen que existir)
            for (int i = 1; i < w.Length; i++)
            {
                string a = w.Substring(0, i), b = w.Substring(i);
                if (b.Length < 2 && (Array.IndexOf(CORTAS, b) < 0 || a.Length < 4)) continue; // "gee" no es "ge e"
                bool corta = a.Length <= 3 && Array.IndexOf(CORTAS, a) >= 0;
                if (!Ortografia.Valida(b) && Array.IndexOf(CORTAS, b) < 0) continue;
                bool bCorta = b.Length == 1 && Array.IndexOf(CORTAS, b) >= 0; // "-me", "-se" pegados son español correcto
                // sin reparar, sólo el espacio perdido junto a una palabra corta ("debilletes" → "de billetes");
                // dos palabras largas pegadas pueden ser cómo escribe el jugador ("instalandome")
                if (corta || (reparar && a.Length >= 2 && Ortografia.Valida(a)) || (bCorta && a.Length >= 4 && Ortografia.Valida(a))) return a + " " + b;
                // la mitad izquierda con el acento perdido: "rapidamentea" → "rápidamente a" (sólo reparando)
                if (reparar && a.Length >= 4)
                {
                    string fa = Forma(a);
                    foreach (var s in Ortografia.Sugerencias(a)) if (!s.Contains(" ") && Forma(s) == fa) return s + " " + b;
                }
            }
            return w;
        }

        // ¿todas las palabras de este texto existen (o se corrigen a algo que existe)?
        static bool TodoValido(string s)
        {
            foreach (Match m in PALABRA.Matches(s))
            {
                string w = m.Value;
                if (w.Length <= 3 && Array.IndexOf(CORTAS, w.ToLowerInvariant()) >= 0) continue;
                if (char.IsUpper(w[0])) continue; // nombres
                if (Ortografia.Valida(w)) continue;
                // sólo se acepta si le falta un acento (misma forma, sin partir)
                string c = Palabra(w, false, true);
                if (c.Contains(" ") || Forma(c) != Forma(w) || !Ortografia.Valida(c)) return false;
            }
            return true;
        }

        // Signos que el OCR mete donde no hay: "Sacas•tu", "ys.e", "rapidamen.tea", "el.motor-del•Sanchez".
        // Un signo pegado entre letras (con minúscula a la derecha) se prueba quitado o como espacio;
        // gana lo que el diccionario reconoce. Números, links y abreviaturas (S.H.S, v4.90.0) no se tocan.
        static readonly Regex SIGNO_PEGADO = new Regex(@"(?<![\w./:])([A-Za-zÁÉÍÓÚÑÜáéíóúñü]+)[.\-•·¬¦]+([a-záéíóúñü][A-Za-zÁÉÍÓÚÑÜáéíóúñü]*)(?![\w/:])");

        public static string Signos(string t)
        {
            if (t.Contains("http") || t.Contains("www")) return t.Replace("•", " ");
            t = Regex.Replace(t, @"(?<=\S)[•·¬¦]+(?=\S)", " ");   // un punto gordo entre palabras = espacio
            t = Regex.Replace(t, @"\s[•·¬¦]+\s", " ");
            for (int vuelta = 0; vuelta < 3; vuelta++)
            {
                string antes = t;
                t = SIGNO_PEGADO.Replace(t, delegate (Match m)
                {
                    string a = m.Groups[1].Value, b = m.Groups[2].Value;
                    string conEspacio = a + " " + b, junto = a + b;
                    if (TodoValido(conEspacio)) return conEspacio;
                    string jc = Palabra(junto, false, true);
                    if (TodoValido(jc)) return jc;
                    bool aOk = Ortografia.Valida(a) || char.IsUpper(a[0]) || Array.IndexOf(CORTAS, a) >= 0;
                    if (aOk && (Ortografia.Valida(b) || Array.IndexOf(CORTAS, b) >= 0)) return conEspacio;
                    return m.Value;
                });
                if (t == antes) break;
            }
            return t;
        }

        public static string Texto(string t)
        {
            t = Signos(t);
            return PALABRA.Replace(t, delegate (Match m)
            {
                string antes = t.Substring(0, m.Index).TrimEnd();
                bool cmd = antes.EndsWith("(") || antes.EndsWith("Usa", StringComparison.OrdinalIgnoreCase) || antes.EndsWith("usa");
                return Palabra(m.Value, cmd);
            });
        }

        // ---------- nombres aprendidos de la captura ----------
        static readonly Regex NOMBRE = new Regex(@"\b([A-ZÁÉÍÓÚÑ][a-záéíóúñ]+(?:[ _][A-ZÁÉÍÓÚÑ][a-záéíóúñ]+)+)\b");

        static int Lev(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i; for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++) for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        // textos = todas las lecturas de la captura (también los nombres sobre las cabezas, que suelen leerse bien)
        public static Dictionary<string, string> Nombres(IEnumerable<string> textos)
        {
            var cuenta = new Dictionary<string, int>();
            foreach (var t in textos)
                foreach (Match m in NOMBRE.Matches(t))
                {
                    string n = m.Groups[1].Value; int c; cuenta.TryGetValue(n, out c); cuenta[n] = c + 1;
                }
            // cada variante rara se lleva al nombre parecido más repetido (≥2 apariciones)
            var mapa = new Dictionary<string, string>();
            foreach (var v in cuenta.Keys)
            {
                string mejor = null; int mc = cuenta[v];
                foreach (var kv in cuenta)
                {
                    if (kv.Key == v || kv.Value < 2 || kv.Value <= mc) continue;
                    int lim = Math.Max(1, kv.Key.Length / 6);
                    if (Math.Abs(kv.Key.Length - v.Length) <= lim && Lev(kv.Key, v) <= lim) { mejor = kv.Key; mc = kv.Value; }
                }
                if (mejor != null) mapa[v] = mejor;
            }
            return mapa;
        }

        public static string AplicarNombres(string t, Dictionary<string, string> mapa)
        {
            if (mapa.Count == 0) return t;
            return NOMBRE.Replace(t, delegate (Match m) { string r; return mapa.TryGetValue(m.Groups[1].Value, out r) ? r : m.Value; });
        }
    }
}
