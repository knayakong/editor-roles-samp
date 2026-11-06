// Controles con aspecto de editor profesional (tema oscuro): deslizadores, panel de capas, íconos.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EditorRoles
{
    public static class Tema
    {
        public static readonly Color Fondo = Color.FromArgb(40, 40, 42);
        public static readonly Color Panel = Color.FromArgb(50, 50, 53);
        public static readonly Color Cabecera = Color.FromArgb(32, 32, 34);
        public static readonly Color Borde = Color.FromArgb(24, 24, 26);
        public static readonly Color Texto = Color.FromArgb(220, 220, 222);
        public static readonly Color Tenue = Color.FromArgb(140, 140, 146);
        public static readonly Color Acento = Color.FromArgb(46, 140, 235);
        public static readonly Color Seleccion = Color.FromArgb(46, 82, 130);
        public static readonly Color Lienzo = Color.FromArgb(28, 28, 30);
        public static readonly Color Boton = Color.FromArgb(64, 64, 68);
        public static readonly Color BotonHover = Color.FromArgb(78, 78, 84);
        public static readonly Font Letra = new Font("Segoe UI", 9f);
        public static readonly Font LetraNegrita = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font LetraTitulo = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        // el ícono que va dentro del .exe (compilar.bat lo mete con -win32icon)
        static Icon icono; static bool iconoProbado;
        public static Icon IconoApp
        {
            get
            {
                if (!iconoProbado) { iconoProbado = true; try { icono = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { icono = null; } }
                return icono;
            }
        }

        public static Button Boton_(string texto)
        {
            var b = new Button();
            b.Text = texto; b.FlatStyle = FlatStyle.Flat; b.BackColor = Boton; b.ForeColor = Texto;
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 86);
            b.FlatAppearance.MouseOverBackColor = BotonHover;
            b.Font = Letra; b.Height = 28; b.Cursor = Cursors.Hand; b.TabStop = false;
            return b;
        }
    }

    // Barra superior oscura
    public class RenderOscuro : ToolStripProfessionalRenderer
    {
        public RenderOscuro() : base(new Colores()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Tema.Texto : Tema.Tenue;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Tema.Texto; base.OnRenderArrow(e);
        }
        class Colores : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin { get { return Tema.Cabecera; } }
            public override Color ToolStripGradientMiddle { get { return Tema.Cabecera; } }
            public override Color ToolStripGradientEnd { get { return Tema.Cabecera; } }
            public override Color ToolStripBorder { get { return Tema.Borde; } }
            public override Color ButtonSelectedHighlight { get { return Tema.BotonHover; } }
            public override Color ButtonSelectedGradientBegin { get { return Tema.BotonHover; } }
            public override Color ButtonSelectedGradientMiddle { get { return Tema.BotonHover; } }
            public override Color ButtonSelectedGradientEnd { get { return Tema.BotonHover; } }
            public override Color ButtonSelectedBorder { get { return Color.FromArgb(90, 90, 96); } }
            public override Color ButtonPressedGradientBegin { get { return Tema.Seleccion; } }
            public override Color ButtonPressedGradientMiddle { get { return Tema.Seleccion; } }
            public override Color ButtonPressedGradientEnd { get { return Tema.Seleccion; } }
            public override Color ButtonCheckedGradientBegin { get { return Tema.Seleccion; } }
            public override Color ButtonCheckedGradientMiddle { get { return Tema.Seleccion; } }
            public override Color ButtonCheckedGradientEnd { get { return Tema.Seleccion; } }
            public override Color ButtonCheckedHighlight { get { return Tema.Seleccion; } }
            public override Color ToolStripDropDownBackground { get { return Tema.Panel; } }
            public override Color ImageMarginGradientBegin { get { return Tema.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Tema.Panel; } }
            public override Color ImageMarginGradientEnd { get { return Tema.Panel; } }
            public override Color MenuItemSelected { get { return Tema.Seleccion; } }
            public override Color MenuItemSelectedGradientBegin { get { return Tema.Seleccion; } }
            public override Color MenuItemSelectedGradientEnd { get { return Tema.Seleccion; } }
            public override Color MenuItemBorder { get { return Tema.Acento; } }
            public override Color MenuBorder { get { return Tema.Borde; } }
            public override Color MenuItemPressedGradientBegin { get { return Tema.Panel; } }
            public override Color MenuItemPressedGradientEnd { get { return Tema.Panel; } }
            public override Color SeparatorDark { get { return Color.FromArgb(70, 70, 74); } }
            public override Color SeparatorLight { get { return Tema.Cabecera; } }
            public override Color CheckBackground { get { return Tema.Seleccion; } }
            public override Color CheckSelectedBackground { get { return Tema.Seleccion; } }
        }
    }

    // Deslizador: se maneja solo con el mouse (arrastrar, clic, rueda; doble clic = valor original).
    public class Deslizador : Control
    {
        public string Titulo = "";
        public float Min = 0, Max = 100, Paso = 1, PorDefecto = 0;
        public string Formato = "0";
        public string Unidad = "";
        float valor;
        bool arrastrando;
        public event EventHandler Cambio;   // el usuario cambió el valor
        public event EventHandler Empieza;  // empieza un cambio (para deshacer)

        public Deslizador()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 44; Cursor = Cursors.Hand; BackColor = Tema.Panel; ForeColor = Tema.Texto; Font = Tema.Letra;
        }

        public float Valor
        {
            get { return valor; }
            set { valor = Math.Max(Min, Math.Min(Max, value)); Invalidate(); }
        }

        RectangleF Pista() { return new RectangleF(8, 30, Width - 16, 4); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            using (var br = new SolidBrush(Enabled ? Tema.Texto : Tema.Tenue)) g.DrawString(Titulo, Font, br, 4, 5);
            string v = valor.ToString(Formato) + Unidad;
            var sz = g.MeasureString(v, Tema.LetraNegrita);
            using (var br = new SolidBrush(Enabled ? Color.White : Tema.Tenue)) g.DrawString(v, Tema.LetraNegrita, br, Width - sz.Width - 4, 5);
            var p = Pista();
            float f = Max > Min ? (valor - Min) / (Max - Min) : 0;
            using (var br = new SolidBrush(Color.FromArgb(30, 30, 32))) g.FillRectangle(br, p);
            using (var br = new SolidBrush(Enabled ? Tema.Acento : Tema.Tenue)) g.FillRectangle(br, p.X, p.Y, p.Width * f, p.Height);
            float cx = p.X + p.Width * f;
            using (var br = new SolidBrush(Enabled ? Color.White : Tema.Tenue)) g.FillEllipse(br, cx - 7, p.Y + 2 - 7, 14, 14);
            using (var pen = new Pen(Color.FromArgb(20, 20, 20))) g.DrawEllipse(pen, cx - 7, p.Y + 2 - 7, 14, 14);
        }

        void DesdeX(int x)
        {
            var p = Pista();
            float f = Math.Max(0, Math.Min(1, (x - p.X) / p.Width));
            float v = Min + f * (Max - Min);
            v = (float)Math.Round(v / Paso) * Paso;
            Fijar(v);
        }

        void Fijar(float v)
        {
            v = Math.Max(Min, Math.Min(Max, v));
            if (Math.Abs(v - valor) < 1e-6) return;
            valor = v; Invalidate();
            if (Cambio != null) Cambio(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (Empieza != null) Empieza(this, EventArgs.Empty);
            arrastrando = true; Capture = true; DesdeX(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (arrastrando) DesdeX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { arrastrando = false; Capture = false; }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (Empieza != null) Empieza(this, EventArgs.Empty);
            Fijar(PorDefecto);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Empieza != null) Empieza(this, EventArgs.Empty);
            Fijar(valor + (e.Delta > 0 ? Paso : -Paso));
            var hm = e as HandledMouseEventArgs; if (hm != null) hm.Handled = true;
        }
    }

    // Fila del panel de capas
    public class FilaCapa
    {
        public object Obj; public int Nivel; public string Texto; public bool Visible; public bool EsGrupo; public bool Abierto;
        public Color Muestra = Color.Empty; public Image Mini; public bool PadreOculto;
    }

    // Panel de capas al estilo Photoshop: ojo, flecha del grupo, nombre; arrastrar un grupo lo reordena.
    public class PanelCapas : Panel
    {
        public const int ALTO = 30;
        public List<FilaCapa> Filas = new List<FilaCapa>();
        public object Seleccion;
        public event Action<object> AlSeleccionar;
        public event Action<object> AlAlternarVisible;
        public event Action<GrupoChat> AlAlternarAbierto;
        public event Action<GrupoChat, int> AlReordenar; // grupo, nueva posición en la lista visual de grupos (0 = arriba)
        public event Action<object> AlDobleClic;

        int pulsada = -1; bool arrastrando; int destino = -1; Point inicio;

        public PanelCapas()
        {
            DoubleBuffered = true; AutoScroll = true; BackColor = Tema.Panel; ResizeRedraw = true;
        }

        public void Actualizar(List<FilaCapa> filas, object sel)
        {
            Filas = filas; Seleccion = sel;
            AutoScrollMinSize = new Size(0, filas.Count * ALTO);
            Invalidate();
        }

        int FilaEn(int y) { int i = (y - AutoScrollPosition.Y) / ALTO; return i >= 0 && i < Filas.Count ? i : -1; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            g.TranslateTransform(0, AutoScrollPosition.Y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Filas.Count; i++)
            {
                var f = Filas[i]; int y = i * ALTO;
                bool sel = f.Obj == Seleccion;
                bool hijoSel = Seleccion is Pieza && f.Obj == ((Pieza)Seleccion).Grupo;
                Color fondo = sel ? Tema.Seleccion : (hijoSel ? Color.FromArgb(56, 62, 72) : Tema.Panel);
                using (var br = new SolidBrush(fondo)) g.FillRectangle(br, 0, y, Width, ALTO);
                using (var pen = new Pen(Tema.Borde)) g.DrawLine(pen, 0, y + ALTO - 1, Width, y + ALTO - 1);
                // ojo
                DibujarOjo(g, 6, y + 8, f.Visible && !f.PadreOculto, f.Visible);
                using (var pen = new Pen(Tema.Borde)) g.DrawLine(pen, 30, y, 30, y + ALTO);
                int x = 36 + f.Nivel * 18;
                if (f.EsGrupo)
                {
                    DibujarFlecha(g, x, y + 10, f.Abierto); x += 14;
                    DibujarCarpeta(g, x, y + 8); x += 24;
                }
                else if (f.Mini != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(f.Mini, x, y + 4, 34, 22);
                    using (var pen = new Pen(Color.FromArgb(90, 90, 96))) g.DrawRectangle(pen, x, y + 4, 34, 22);
                    x += 42;
                }
                else
                {
                    using (var br = new SolidBrush(Color.FromArgb(30, 30, 32))) g.FillRectangle(br, x, y + 5, 20, 20);
                    using (var br = new SolidBrush(f.Muestra.IsEmpty ? Color.White : f.Muestra))
                    using (var ft = new Font("Arial", 10f, FontStyle.Bold))
                        g.DrawString("T", ft, br, x + 4, y + 7);
                    x += 28;
                }
                var txt = new RectangleF(x, y, Width - x - 4, ALTO);
                using (var br = new SolidBrush(f.Visible ? Tema.Texto : Tema.Tenue))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(f.Texto, f.EsGrupo ? Tema.LetraNegrita : Tema.Letra, br, txt, sf);
            }
            if (arrastrando && destino >= 0)
            {
                int y = destino * ALTO;
                using (var pen = new Pen(Tema.Acento, 3)) g.DrawLine(pen, 0, y, Width, y);
            }
        }

        static void DibujarOjo(Graphics g, int x, int y, bool activo, bool marcado)
        {
            if (!marcado) { using (var pen = new Pen(Color.FromArgb(80, 80, 86))) g.DrawRectangle(pen, x + 2, y + 2, 14, 10); return; }
            using (var pen = new Pen(activo ? Tema.Texto : Tema.Tenue, 1.4f))
            {
                g.DrawArc(pen, x, y + 1, 18, 14, 200, 140);
                g.DrawArc(pen, x, y - 1, 18, 14, 20, 140);
            }
            using (var br = new SolidBrush(activo ? Tema.Texto : Tema.Tenue)) g.FillEllipse(br, x + 6, y + 3, 6, 6);
        }

        static void DibujarFlecha(Graphics g, int x, int y, bool abierto)
        {
            PointF[] p = abierto
                ? new[] { new PointF(x, y), new PointF(x + 9, y), new PointF(x + 4.5f, y + 6) }
                : new[] { new PointF(x + 2, y - 2), new PointF(x + 8, y + 3), new PointF(x + 2, y + 8) };
            using (var br = new SolidBrush(Tema.Texto)) g.FillPolygon(br, p);
        }

        static void DibujarCarpeta(Graphics g, int x, int y)
        {
            using (var br = new SolidBrush(Color.FromArgb(214, 176, 92)))
            {
                g.FillRectangle(br, x, y + 2, 18, 12);
                g.FillRectangle(br, x, y, 8, 3);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int i = FilaEn(e.Y); if (i < 0) { if (AlSeleccionar != null) AlSeleccionar(null); return; }
            var f = Filas[i];
            if (e.X < 30) { if (AlAlternarVisible != null) AlAlternarVisible(f.Obj); return; }
            int xFlecha = 36 + f.Nivel * 18;
            if (f.EsGrupo && e.X >= xFlecha - 2 && e.X < xFlecha + 14) { if (AlAlternarAbierto != null) AlAlternarAbierto((GrupoChat)f.Obj); return; }
            if (AlSeleccionar != null) AlSeleccionar(f.Obj);
            pulsada = f.EsGrupo ? i : -1; inicio = e.Location; arrastrando = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pulsada < 0 || e.Button != MouseButtons.Left) return;
            if (!arrastrando && Math.Abs(e.Y - inicio.Y) > 5) arrastrando = true;
            if (!arrastrando) return;
            // destino: borde superior de la fila de grupo más cercana
            int fila = Math.Max(0, Math.Min(Filas.Count, (e.Y - AutoScrollPosition.Y + ALTO / 2) / ALTO));
            while (fila < Filas.Count && !(Filas[fila].EsGrupo)) fila++;
            if (fila >= Filas.Count || !(Filas[fila].Obj is GrupoChat))
            {
                // debajo del último grupo (encima del fondo)
                fila = Filas.Count; for (int k = 0; k < Filas.Count; k++) if (Filas[k].Obj is CapaFondo) { fila = k; break; }
            }
            destino = fila; Cursor = Cursors.SizeNS; Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (arrastrando && pulsada >= 0 && destino >= 0)
            {
                // posición visual entre grupos
                int pos = 0;
                for (int k = 0; k < destino && k < Filas.Count; k++) if (Filas[k].EsGrupo && k != pulsada) pos++;
                if (AlReordenar != null) AlReordenar((GrupoChat)Filas[pulsada].Obj, pos);
            }
            pulsada = -1; arrastrando = false; destino = -1; Cursor = Cursors.Default; Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = FilaEn(e.Y); if (i < 0 || e.X < 30) return;
            if (AlDobleClic != null) AlDobleClic(Filas[i].Obj);
        }
    }

    public static class Iconos
    {
        // 0 izq · 1 centro horiz · 2 der · 3 arriba · 4 medio vert · 5 abajo
        public static Bitmap Alinear(int tipo)
        {
            var b = new Bitmap(22, 22);
            using (var g = Graphics.FromImage(b))
            using (var pen = new Pen(Tema.Acento, 2))
            using (var br = new SolidBrush(Tema.Texto))
            {
                g.Clear(Color.Transparent);
                switch (tipo)
                {
                    case 0: g.DrawLine(pen, 3, 2, 3, 20); g.FillRectangle(br, 5, 5, 13, 4); g.FillRectangle(br, 5, 13, 8, 4); break;
                    case 1: g.DrawLine(pen, 11, 2, 11, 20); g.FillRectangle(br, 4, 5, 14, 4); g.FillRectangle(br, 7, 13, 8, 4); break;
                    case 2: g.DrawLine(pen, 19, 2, 19, 20); g.FillRectangle(br, 4, 5, 13, 4); g.FillRectangle(br, 9, 13, 8, 4); break;
                    case 3: g.DrawLine(pen, 2, 3, 20, 3); g.FillRectangle(br, 5, 5, 4, 13); g.FillRectangle(br, 13, 5, 4, 8); break;
                    case 4: g.DrawLine(pen, 2, 11, 20, 11); g.FillRectangle(br, 5, 4, 4, 14); g.FillRectangle(br, 13, 7, 4, 8); break;
                    case 5: g.DrawLine(pen, 2, 19, 20, 19); g.FillRectangle(br, 5, 4, 4, 13); g.FillRectangle(br, 13, 9, 4, 8); break;
                }
            }
            return b;
        }
    }

    public class PanelDoble : Panel
    {
        public PanelDoble() { DoubleBuffered = true; ResizeRedraw = true; }
    }
}
