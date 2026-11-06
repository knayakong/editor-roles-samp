// Documento por capas: Fondo (la captura) + grupos de chat; cada grupo tiene trozos de texto.
// Un trozo = líneas seguidas del chat; donde hay líneas no usadas en medio, el chat se parte solo.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace EditorRoles
{
    public class Estilo
    {
        public float Tam = 12f;          // px
        public float Contorno = 1f;      // px
        public Color ColorContorno = Color.Black;
        public float Interlineado = 1.25f;
        public bool Suave = true;
        public Estilo Copia() { return (Estilo)MemberwiseClone(); }
    }

    public abstract class Capa
    {
        public string Nombre = "";
        public bool Visible = true;
    }

    public class CapaFondo : Capa
    {
        public Bitmap Img;
        public float X, Y, Esc = 1f;
        public RectangleF Caja() { return new RectangleF(X, Y, Img.Width * Esc, Img.Height * Esc); }
        public CapaFondo Copia() { return (CapaFondo)MemberwiseClone(); }
    }

    public class Pieza : Capa
    {
        public GrupoChat Grupo;
        public List<int> Indices = new List<int>(); // líneas de Grupo.Todas
        public float X, Y;
        public Estilo Est = new Estilo();

        public List<Linea> Lineas()
        {
            var r = new List<Linea>();
            foreach (int i in Indices) r.Add(Grupo.Todas[i]);
            return r;
        }

        public float Paso() { return Est.Tam * Est.Interlineado; }

        // caminos de texto (uno por línea), en coordenadas del lienzo
        public List<GraphicsPath> Caminos()
        {
            var res = new List<GraphicsPath>();
            var ls = Lineas();
            for (int i = 0; i < ls.Count; i++)
            {
                var p = new GraphicsPath();
                p.AddString(ls[i].Texto, Fuente.Familia(), (int)FontStyle.Bold, Est.Tam,
                    new PointF(X, Y + i * Paso()), StringFormat.GenericTypographic);
                res.Add(p);
            }
            return res;
        }

        public RectangleF Caja()
        {
            RectangleF r = RectangleF.Empty; bool primero = true;
            foreach (var p in Caminos())
            {
                var b = p.GetBounds();
                if (b.Width > 0) { r = primero ? b : RectangleF.Union(r, b); primero = false; }
                p.Dispose();
            }
            if (primero) return new RectangleF(X, Y, 40, Est.Tam);
            r.Inflate(Est.Contorno, Est.Contorno);
            return r;
        }

        public void Dibujar(Graphics g)
        {
            var ls = Lineas();
            var cs = Caminos();
            g.SmoothingMode = Est.Suave ? SmoothingMode.AntiAlias : SmoothingMode.None;
            if (Est.Contorno > 0)
                using (var pen = new Pen(Est.ColorContorno, Est.Contorno * 2f))
                {
                    pen.LineJoin = LineJoin.Round;
                    foreach (var c in cs) g.DrawPath(pen, c);
                }
            for (int i = 0; i < cs.Count; i++)
                using (var br = new SolidBrush(ls[i].Color)) g.FillPath(br, cs[i]);
            foreach (var c in cs) c.Dispose();
        }

        public string Resumen()
        {
            var ls = Lineas();
            if (ls.Count == 0) return "(vacío)";
            string t = ls[0].Texto;
            if (t.Length > 34) t = t.Substring(0, 33) + "…";
            return ls.Count == 1 ? t : t + "  (+" + (ls.Count - 1) + ")";
        }
    }

    public class GrupoChat : Capa
    {
        public List<Linea> Todas = new List<Linea>();
        public Bitmap Chat;
        public List<Pieza> Piezas = new List<Pieza>();
        public bool Abierto = true;

        public RectangleF Caja()
        {
            RectangleF r = RectangleF.Empty; bool primero = true;
            foreach (var p in Piezas)
            {
                if (!p.Visible) continue;
                var b = p.Caja();
                r = primero ? b : RectangleF.Union(r, b); primero = false;
            }
            return primero ? RectangleF.Empty : r;
        }

        // Rearma los trozos según qué líneas están marcadas. Conserva posiciones si la cantidad no cambia.
        public void Rearmar(PointF origen, Estilo est)
        {
            var tramos = new List<List<int>>();
            List<int> cur = null;
            for (int i = 0; i < Todas.Count; i++)
            {
                if (Todas[i].Usar) { if (cur == null) { cur = new List<int>(); tramos.Add(cur); } cur.Add(i); }
                else cur = null;
            }
            var viejas = Piezas;
            if (viejas.Count > 0) { origen = new PointF(viejas[0].X, viejas[0].Y); est = viejas[0].Est; }
            Piezas = new List<Pieza>();
            float y = origen.Y;
            for (int k = 0; k < tramos.Count; k++)
            {
                var p = new Pieza();
                p.Grupo = this; p.Indices = tramos[k];
                if (viejas.Count == tramos.Count)
                {
                    p.X = viejas[k].X; p.Y = viejas[k].Y; p.Est = viejas[k].Est.Copia(); p.Visible = viejas[k].Visible;
                }
                else
                {
                    p.Est = est.Copia(); p.X = origen.X; p.Y = y;
                    y += tramos[k].Count * p.Paso();
                }
                Piezas.Add(p);
            }
        }

        public GrupoChat Clonar()
        {
            var g = new GrupoChat();
            g.Nombre = Nombre; g.Visible = Visible; g.Abierto = Abierto; g.Chat = Chat;
            foreach (var l in Todas) g.Todas.Add(l.Copia());
            foreach (var p in Piezas)
            {
                var q = new Pieza();
                q.Grupo = g; q.Nombre = p.Nombre; q.Visible = p.Visible; q.X = p.X; q.Y = p.Y;
                q.Est = p.Est.Copia(); q.Indices = new List<int>(p.Indices);
                g.Piezas.Add(q);
            }
            return g;
        }
    }

    public class Documento
    {
        public int W = 800, H = 600;
        public CapaFondo Fondo;
        public List<GrupoChat> Grupos = new List<GrupoChat>(); // [0] = el de más abajo
        public int Contador = 0; // para nombrar "Chat 1, Chat 2…"

        public Documento Clonar()
        {
            var d = new Documento();
            d.W = W; d.H = H; d.Contador = Contador;
            if (Fondo != null) d.Fondo = Fondo.Copia();
            foreach (var g in Grupos) d.Grupos.Add(g.Clonar());
            return d;
        }

        // ubica el objeto equivalente en otro documento (para conservar la selección al deshacer)
        public object Equivalente(object sel, Documento otro)
        {
            if (sel == null) return null;
            if (sel is CapaFondo) return otro.Fondo;
            var g = sel as GrupoChat;
            if (g != null) { int i = Grupos.IndexOf(g); return i >= 0 && i < otro.Grupos.Count ? otro.Grupos[i] : null; }
            var p = sel as Pieza;
            if (p != null)
            {
                int i = Grupos.IndexOf(p.Grupo); if (i < 0 || i >= otro.Grupos.Count) return null;
                int j = p.Grupo.Piezas.IndexOf(p); var og = otro.Grupos[i];
                return j >= 0 && j < og.Piezas.Count ? og.Piezas[j] : (object)og;
            }
            return null;
        }
    }

    public static class Fuente
    {
        static FontFamily familia;
        public static FontFamily Familia()
        {
            if (familia == null)
            {
                try { familia = new FontFamily("Arial"); } catch { familia = FontFamily.GenericSansSerif; }
            }
            return familia;
        }
    }
}
