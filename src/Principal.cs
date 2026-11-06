// Ventana principal, al estilo de un editor profesional:
//   arriba  = Exportar (PNG/JPG) · Abrir captura · Importar chat · Lienzo · Márgenes · Deshacer · Zoom
//   centro  = el lienzo (asas en las esquinas para escalar, guías inteligentes al mover, zoom con la rueda)
//   derecha = Capas (arriba) y Propiedades de la capa elegida (abajo)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EditorRoles
{
    public class Principal : Form
    {
        // ---------- documento ----------
        Documento doc = new Documento();
        object sel;
        readonly List<Documento> pasado = new List<Documento>();
        readonly List<Documento> futuro = new List<Documento>();
        bool mostrarMargen = true;
        int margen = 30;
        string idiomaOcr;

        // ---------- vista ----------
        PanelDoble vista;
        float zoom = 1f; PointF off; bool ajustarAuto = true;
        Bitmap render; bool sucio = true;
        Bitmap miniFondo;

        // ---------- interacción ----------
        enum Modo { Nada, Mover, Escalar, Paneo }
        Modo modo = Modo.Nada;
        PointF ini; Point iniPant; PointF offIni; bool registrado;
        RectangleF caja0;
        readonly List<PointF> pos0 = new List<PointF>();
        readonly List<float> tam0 = new List<float>();
        PointF ancla, esquina0;
        float? guiaX, guiaY;
        bool espacio;
        DateTime ultimoEmpuje = DateTime.MinValue;

        // ---------- UI ----------
        ToolStrip barra;
        ToolStripDropDownButton bLienzo, bChatIdioma;
        ToolStripButton bMargen, bDeshacer, bRehacer;
        ToolStripLabel lZoom;
        PanelCapas capas;
        Panel host, propNada, propFondo, propTexto;
        Deslizador dEscFondo, dTam, dCont, dInter;
        Button bColorCont; CheckBox chSuave; Label lTituloTexto;
        Label estado, estadoDer;
        readonly ToolTip tips = new ToolTip();
        bool sincronizando;

        static readonly int[,] PRESETS = { { 800, 600 }, { 1200, 500 }, { 1024, 768 }, { 1280, 720 }, { 1280, 1024 }, { 1366, 768 }, { 1600, 900 }, { 1920, 1080 } };

        public Principal()
        {
            Text = "Editor de roles SA-MP — por Knxyv";
            if (Tema.IconoApp != null) Icon = Tema.IconoApp;
            Size = new Size(1360, 860);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Tema.Fondo; ForeColor = Tema.Texto; Font = Tema.Letra;
            KeyPreview = true; AllowDrop = true;

            var idi = MotorOcr.Idiomas();
            idiomaOcr = idi.Find(delegate (string s) { return s.StartsWith("es"); });
            if (idiomaOcr == null && idi.Count > 0) idiomaOcr = idi[0];

            ConstruirLado();
            ConstruirVista();
            ConstruirBarra();
            ConstruirEstado();
            vista.BringToFront(); // el lienzo ocupa lo que dejan la barra, el pie y el panel derecho

            KeyDown += Teclas;
            KeyUp += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Space) { espacio = false; vista.Cursor = Cursors.Default; } };
            DragEnter += delegate (object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += Soltar;

            Refrescar();
            Estado("Empezá con «Abrir captura» (arriba) o arrastrá la imagen a la ventana.");
            Shown += delegate { if (!modoPrueba) ElegirTamanoInicial(); };
        }

        // =====================================================================
        // construcción de la interfaz
        // =====================================================================
        void ConstruirBarra()
        {
            barra = new ToolStrip();
            barra.Renderer = new RenderOscuro();
            barra.GripStyle = ToolStripGripStyle.Hidden;
            barra.Padding = new Padding(6, 4, 6, 4);
            barra.BackColor = Tema.Cabecera; barra.ForeColor = Tema.Texto;
            barra.Font = new Font("Segoe UI", 9.5f);
            barra.ImageScalingSize = new Size(18, 18);

            var bExp = new ToolStripDropDownButton("Exportar");
            bExp.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            bExp.DropDownItems.Add("PNG — máxima calidad…", null, delegate { Exportar(false); });
            bExp.DropDownItems.Add("JPG — pesa menos…", null, delegate { Exportar(true); });
            Oscurecer(bExp);
            barra.Items.Add(bExp);
            barra.Items.Add(new ToolStripSeparator());

            var bFondo = new ToolStripButton("Abrir captura");
            bFondo.ToolTipText = "La captura del juego que va de fondo (sin el chat)";
            bFondo.Click += delegate { AbrirFondo(null); };
            barra.Items.Add(bFondo);

            var bChat = new ToolStripButton("Importar chat");
            bChat.ToolTipText = "La imagen donde se ve el chat: se lee sola y elegís qué líneas van";
            bChat.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            bChat.Click += delegate { ImportarChat(null); };
            barra.Items.Add(bChat);

            bChatIdioma = new ToolStripDropDownButton();
            bChatIdioma.ToolTipText = "Idioma del lector de texto";
            foreach (var i in MotorOcr.Idiomas())
            {
                string tag = i;
                var it = new ToolStripMenuItem(tag, null, delegate { idiomaOcr = tag; ActualizarBarra(); });
                bChatIdioma.DropDownItems.Add(it);
            }
            Oscurecer(bChatIdioma);
            barra.Items.Add(bChatIdioma);
            barra.Items.Add(new ToolStripSeparator());

            barra.Items.Add(new ToolStripLabel("Lienzo:"));
            bLienzo = new ToolStripDropDownButton();
            for (int i = 0; i < PRESETS.GetLength(0); i++)
            {
                int w = PRESETS[i, 0], h = PRESETS[i, 1];
                string nom = w + " × " + h + (w == 800 ? "   (el de los foros)" : (w == 1200 ? "   (panorámico)" : (w == 1920 ? "   (Full HD)" : "")));
                bLienzo.DropDownItems.Add(nom, null, delegate { CambiarLienzo(w, h); });
            }
            bLienzo.DropDownItems.Add(new ToolStripSeparator());
            bLienzo.DropDownItems.Add("Igual que la captura", null, delegate { if (doc.Fondo != null) CambiarLienzo(doc.Fondo.Img.Width, doc.Fondo.Img.Height); });
            bLienzo.DropDownItems.Add("Otro tamaño…", null, delegate { OtroTamano(); });
            Oscurecer(bLienzo);
            barra.Items.Add(bLienzo);

            bMargen = new ToolStripButton("Márgenes seguros");
            bMargen.CheckOnClick = true; bMargen.Checked = true;
            bMargen.ToolTipText = "Muestra la zona segura (no sale en la imagen exportada)";
            bMargen.CheckedChanged += delegate { mostrarMargen = bMargen.Checked; vista.Invalidate(); };
            barra.Items.Add(bMargen);
            barra.Items.Add(new ToolStripSeparator());

            bDeshacer = new ToolStripButton("↶ Deshacer"); bDeshacer.ToolTipText = "Ctrl+Z"; bDeshacer.Click += delegate { Deshacer(); };
            bRehacer = new ToolStripButton("↷ Rehacer"); bRehacer.ToolTipText = "Ctrl+Y"; bRehacer.Click += delegate { Rehacer(); };
            barra.Items.Add(bDeshacer); barra.Items.Add(bRehacer);

            var bAjustar = new ToolStripButton("Ajustar a la ventana"); bAjustar.Alignment = ToolStripItemAlignment.Right;
            bAjustar.Click += delegate { ajustarAuto = true; vista.Invalidate(); };
            var bMas = new ToolStripButton("+"); bMas.Alignment = ToolStripItemAlignment.Right; bMas.ToolTipText = "Acercar (rueda del mouse)";
            bMas.Click += delegate { Zoom(1.25f, CentroVista()); };
            lZoom = new ToolStripLabel("100%"); lZoom.Alignment = ToolStripItemAlignment.Right;
            var bMenos = new ToolStripButton("−"); bMenos.Alignment = ToolStripItemAlignment.Right; bMenos.ToolTipText = "Alejar (rueda del mouse)";
            bMenos.Click += delegate { Zoom(1 / 1.25f, CentroVista()); };
            barra.Items.Add(bAjustar); barra.Items.Add(bMas); barra.Items.Add(lZoom); barra.Items.Add(bMenos);

            Controls.Add(barra);
        }

        static void Oscurecer(ToolStripDropDownItem d)
        {
            d.DropDown.BackColor = Tema.Panel; d.DropDown.ForeColor = Tema.Texto;
            foreach (ToolStripItem it in d.DropDownItems) { it.BackColor = Tema.Panel; it.ForeColor = Tema.Texto; }
        }

        void ConstruirEstado()
        {
            var pie = new Panel(); pie.Dock = DockStyle.Bottom; pie.Height = 24; pie.BackColor = Tema.Cabecera;
            estado = new Label(); estado.Dock = DockStyle.Fill; estado.ForeColor = Tema.Tenue; estado.TextAlign = ContentAlignment.MiddleLeft; estado.Padding = new Padding(8, 0, 0, 0);
            // créditos (siempre visibles, nunca en la imagen exportada)
            var creditos = new Label(); creditos.Dock = DockStyle.Right; creditos.Width = 300; creditos.TextAlign = ContentAlignment.MiddleRight;
            creditos.Padding = new Padding(0, 0, 10, 0); creditos.ForeColor = Color.FromArgb(170, 150, 210);
            creditos.Text = "Hecho por Knxyv  ·  ¿Sugerencias? Discord @knvyv";
            pie.Controls.Add(creditos);
            estadoDer = new Label(); estadoDer.Dock = DockStyle.Right; estadoDer.Width = 420; estadoDer.ForeColor = Tema.Tenue; estadoDer.TextAlign = ContentAlignment.MiddleRight; estadoDer.Padding = new Padding(0, 0, 8, 0);
            pie.Controls.Add(estado); pie.Controls.Add(estadoDer); estado.BringToFront(); // el texto de estado ocupa lo que dejan los de la derecha
            Controls.Add(pie);
        }

        void ConstruirVista()
        {
            vista = new PanelDoble();
            vista.Dock = DockStyle.Fill;
            vista.BackColor = Tema.Lienzo;
            vista.Paint += Pintar;
            vista.MouseDown += MDown;
            vista.MouseMove += MMove;
            vista.MouseUp += MUp;
            vista.MouseWheel += Rueda;
            vista.MouseDoubleClick += delegate (object s, MouseEventArgs e)
            {
                if (sel is GrupoChat || sel is Pieza) RevisarLineas();
            };
            vista.Resize += delegate { if (ajustarAuto) vista.Invalidate(); };
            vista.MouseEnter += delegate { vista.Focus(); };
            Controls.Add(vista);
        }

        Label Cabecera(string t)
        {
            var l = new Label(); l.Text = t; l.Dock = DockStyle.Top; l.Height = 26; l.Font = Tema.LetraTitulo;
            l.ForeColor = Tema.Tenue; l.BackColor = Tema.Cabecera; l.TextAlign = ContentAlignment.MiddleLeft; l.Padding = new Padding(8, 0, 0, 0);
            return l;
        }

        void ConstruirLado()
        {
            var lado = new Panel(); lado.Dock = DockStyle.Right; lado.Width = 310; lado.BackColor = Tema.Borde; lado.Padding = new Padding(1, 0, 0, 0);
            var split = new SplitContainer(); split.Dock = DockStyle.Fill; split.Orientation = Orientation.Horizontal;
            split.BackColor = Tema.Borde; split.SplitterWidth = 4;
            split.Panel1.BackColor = Tema.Panel; split.Panel2.BackColor = Tema.Panel;
            lado.Controls.Add(split);
            Controls.Add(lado);

            // ---- capas ----
            capas = new PanelCapas(); capas.Dock = DockStyle.Fill;
            capas.AlSeleccionar += delegate (object o) { Seleccionar(o); };
            capas.AlAlternarVisible += AlternarVisible;
            capas.AlAlternarAbierto += delegate (GrupoChat g) { g.Abierto = !g.Abierto; ActualizarCapas(); };
            capas.AlReordenar += Reordenar;
            capas.AlDobleClic += delegate (object o) { Seleccionar(o); if (o is GrupoChat || o is Pieza) RevisarLineas(); };
            var botonera = new FlowLayoutPanel(); botonera.Dock = DockStyle.Bottom; botonera.Height = 38; botonera.Padding = new Padding(4, 4, 0, 0);
            botonera.BackColor = Tema.Cabecera;
            var bSubir = Tema.Boton_("▲"); bSubir.Width = 36; tips.SetToolTip(bSubir, "Subir la capa"); bSubir.Click += delegate { Mover(+1); };
            var bBajar = Tema.Boton_("▼"); bBajar.Width = 36; tips.SetToolTip(bBajar, "Bajar la capa"); bBajar.Click += delegate { Mover(-1); };
            var bRev = Tema.Boton_("Revisar líneas"); bRev.Width = 116; tips.SetToolTip(bRev, "Volver a la lista de líneas que leyó el chat"); bRev.Click += delegate { RevisarLineas(); };
            var bBorrar = Tema.Boton_("Borrar"); bBorrar.Width = 76; tips.SetToolTip(bBorrar, "Borrar la capa elegida (Supr)"); bBorrar.Click += delegate { Borrar(); };
            botonera.Controls.AddRange(new Control[] { bSubir, bBajar, bRev, bBorrar });
            split.Panel1.Controls.Add(capas);
            split.Panel1.Controls.Add(botonera);
            split.Panel1.Controls.Add(Cabecera("CAPAS"));

            // ---- propiedades ----
            host = new Panel(); host.Dock = DockStyle.Fill; host.AutoScroll = true; host.BackColor = Tema.Panel;
            split.Panel2.Controls.Add(host);
            split.Panel2.Controls.Add(Cabecera("PROPIEDADES"));

            propNada = NuevoProp();
            var ln = Etiqueta(propNada, "Elegí una capa en la lista de arriba o hacé clic sobre algo en el lienzo.", 60);
            ln.ForeColor = Tema.Tenue;

            propFondo = NuevoProp();
            Etiqueta(propFondo, "Captura del juego", 22).Font = Tema.LetraNegrita;
            dEscFondo = NuevoDesliz(propFondo, "Tamaño de la captura", 2, 400, 1, 100, "0", " %");
            dEscFondo.Empieza += delegate { Registrar(); };
            dEscFondo.Cambio += delegate { EscalarFondoCentro(dEscFondo.Valor / 100f); };
            FilaBotones(propFondo, new[] { "Llenar lienzo", "Encajar", "Centrar" }, new EventHandler[] {
                delegate { Registrar(); AjustarFondo(true); }, delegate { Registrar(); AjustarFondo(false); }, delegate { Registrar(); CentrarFondo(); } });
            FilaAlinear(propFondo);
            Etiqueta(propFondo, "Tip: arrastrá las esquinas de la captura en el lienzo para agrandarla o achicarla.", 40).ForeColor = Tema.Tenue;

            propTexto = NuevoProp();
            lTituloTexto = Etiqueta(propTexto, "", 22); lTituloTexto.Font = Tema.LetraNegrita;
            dTam = NuevoDesliz(propTexto, "Tamaño de letra", 6, 60, 0.5f, 12, "0.#", " px");
            dCont = NuevoDesliz(propTexto, "Grosor del contorno", 0, 6, 0.5f, 1, "0.#", " px");
            dInter = NuevoDesliz(propTexto, "Separación entre líneas", 0.8f, 2.5f, 0.05f, 1.25f, "0.00", "×");
            foreach (var d in new[] { dTam, dCont, dInter })
            {
                d.Empieza += delegate { Registrar(); };
                d.Cambio += delegate { AplicarEstilo(); };
            }
            bColorCont = Tema.Boton_("Color del contorno");
            Ubicar(propTexto, bColorCont, 30);
            bColorCont.TextAlign = ContentAlignment.MiddleLeft; bColorCont.Padding = new Padding(30, 0, 0, 0);
            bColorCont.Paint += delegate (object s, PaintEventArgs e)
            {
                var est = EstiloSel();
                using (var br = new SolidBrush(est != null ? est.ColorContorno : Color.Black)) e.Graphics.FillRectangle(br, 8, 6, 16, 16);
                e.Graphics.DrawRectangle(Pens.Gray, 8, 6, 16, 16);
            };
            bColorCont.Click += delegate { ElegirColorContorno(); };
            chSuave = new CheckBox(); chSuave.Text = "Bordes suaves"; chSuave.ForeColor = Tema.Texto; chSuave.Checked = true;
            Ubicar(propTexto, chSuave, 26);
            chSuave.CheckedChanged += delegate { if (sincronizando) return; Registrar(); AplicarEstilo(); };
            FilaAlinear(propTexto);
            var bRev2 = Tema.Boton_("Revisar líneas del chat…"); Ubicar(propTexto, bRev2, 30);
            bRev2.Click += delegate { RevisarLineas(); };
            Etiqueta(propTexto, "Tip: arrastrá las esquinas del texto para agrandarlo. Doble clic sobre el texto para corregir líneas.", 44).ForeColor = Tema.Tenue;

            host.Controls.Add(propNada); host.Controls.Add(propFondo); host.Controls.Add(propTexto);
            split.SplitterDistance = 330;
        }

        // --- ayudantes de maquetado del panel de propiedades ---
        const int ANCHO = 270; // ancho útil del panel derecho (310 menos márgenes y barra de desplazamiento)
        Panel NuevoProp()
        {
            var p = new Panel(); p.Dock = DockStyle.Top; p.BackColor = Tema.Panel; p.Height = 10; p.Padding = new Padding(10, 8, 10, 8);
            p.Visible = false; p.Tag = 8; return p;
        }
        void Ubicar(Panel p, Control c, int alto)
        {
            int y = (int)p.Tag;
            c.SetBounds(10, y, ANCHO, alto); c.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            p.Controls.Add(c); p.Tag = y + alto + 6; p.Height = y + alto + 14;
        }
        Label Etiqueta(Panel p, string t, int alto)
        {
            var l = new Label(); l.Text = t; l.ForeColor = Tema.Texto; Ubicar(p, l, alto); return l;
        }
        Deslizador NuevoDesliz(Panel p, string titulo, float min, float max, float paso, float def, string fmt, string unidad)
        {
            var d = new Deslizador(); d.Titulo = titulo; d.Min = min; d.Max = max; d.Paso = paso; d.PorDefecto = def; d.Formato = fmt; d.Unidad = unidad; d.Valor = def;
            Ubicar(p, d, 44);
            tips.SetToolTip(d, "Arrastrá, hacé clic o usá la rueda. Doble clic = valor normal.");
            return d;
        }
        void FilaBotones(Panel p, string[] textos, EventHandler[] acciones)
        {
            int y = (int)p.Tag; int w = (ANCHO - (textos.Length - 1) * 6) / textos.Length;
            for (int i = 0; i < textos.Length; i++)
            {
                var b = Tema.Boton_(textos[i]); b.SetBounds(10 + i * (w + 6), y, w, 28); b.Click += acciones[i]; p.Controls.Add(b);
            }
            p.Tag = y + 34; p.Height = y + 42;
        }
        void FilaAlinear(Panel p)
        {
            var l = Etiqueta(p, "Alinear a la zona segura:", 18); l.ForeColor = Tema.Tenue;
            int y = (int)p.Tag;
            string[] ayuda = { "Izquierda", "Centro (horizontal)", "Derecha", "Arriba", "Centro (vertical)", "Abajo" };
            for (int i = 0; i < 6; i++)
            {
                int tipo = i;
                var b = Tema.Boton_(""); b.Image = Iconos.Alinear(i); b.SetBounds(10 + i * 43 + (i >= 3 ? 8 : 0), y, 38, 30);
                tips.SetToolTip(b, ayuda[i]);
                b.Click += delegate { Alinear(tipo); };
                p.Controls.Add(b);
            }
            p.Tag = y + 38; p.Height = y + 46;
        }

        void Estado(string s) { estado.Text = s; }

        // =====================================================================
        // refresco
        // =====================================================================
        void Refrescar()
        {
            sucio = true;
            vista.Invalidate();
            ActualizarCapas();
            ActualizarProps();
            ActualizarBarra();
        }

        void ActualizarBarra()
        {
            bLienzo.Text = doc.W + " × " + doc.H;
            bChatIdioma.Text = idiomaOcr ?? "sin OCR";
            foreach (ToolStripItem it in bChatIdioma.DropDownItems)
            {
                var mi = it as ToolStripMenuItem; if (mi != null) mi.Checked = mi.Text == idiomaOcr;
            }
            bDeshacer.Enabled = pasado.Count > 0; bRehacer.Enabled = futuro.Count > 0;
            lZoom.Text = Math.Round(zoom * 100) + "%";
            estadoDer.Text = "Lienzo " + doc.W + "×" + doc.H + "   ·   Zoom " + Math.Round(zoom * 100) + "%   ·   Rueda: zoom · Espacio+arrastrar: mover la vista";
        }

        void ActualizarCapas()
        {
            var filas = new List<FilaCapa>();
            for (int gi = doc.Grupos.Count - 1; gi >= 0; gi--)
            {
                var g = doc.Grupos[gi];
                int n = 0; foreach (var p in g.Piezas) n += p.Indices.Count;
                filas.Add(new FilaCapa { Obj = g, EsGrupo = true, Abierto = g.Abierto, Visible = g.Visible, Texto = g.Nombre + "   ·  " + n + " línea" + (n == 1 ? "" : "s") });
                if (g.Abierto)
                    foreach (var p in g.Piezas)
                    {
                        var ls = p.Lineas();
                        filas.Add(new FilaCapa { Obj = p, Nivel = 1, Visible = p.Visible, PadreOculto = !g.Visible, Texto = p.Resumen(), Muestra = ls.Count > 0 ? ls[0].Color : Color.White });
                    }
            }
            if (doc.Fondo != null)
            {
                if (miniFondo == null || miniFondo.Tag != (object)doc.Fondo.Img)
                {
                    if (miniFondo != null) miniFondo.Dispose();
                    miniFondo = new Bitmap(68, 44);
                    using (var g = Graphics.FromImage(miniFondo)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(doc.Fondo.Img, 0, 0, 68, 44); }
                    miniFondo.Tag = doc.Fondo.Img;
                }
                filas.Add(new FilaCapa { Obj = doc.Fondo, Visible = doc.Fondo.Visible, Texto = "Captura del juego", Mini = miniFondo });
            }
            capas.Actualizar(filas, sel);
        }

        Estilo EstiloSel()
        {
            var p = sel as Pieza; if (p != null) return p.Est;
            var g = sel as GrupoChat; if (g != null && g.Piezas.Count > 0) return g.Piezas[0].Est;
            return null;
        }

        void ActualizarProps()
        {
            sincronizando = true;
            host.SuspendLayout();
            propNada.Visible = sel == null;
            propFondo.Visible = sel is CapaFondo;
            propTexto.Visible = sel is GrupoChat || sel is Pieza;
            if (sel is CapaFondo) dEscFondo.Valor = doc.Fondo.Esc * 100f;
            var est = EstiloSel();
            if (est != null)
            {
                dTam.Valor = est.Tam; dCont.Valor = est.Contorno; dInter.Valor = est.Interlineado; chSuave.Checked = est.Suave;
                bColorCont.Invalidate();
                lTituloTexto.Text = sel is GrupoChat ? ((GrupoChat)sel).Nombre + " (todo el grupo)" : "Trozo de " + ((Pieza)sel).Grupo.Nombre;
            }
            host.ResumeLayout();
            sincronizando = false;
        }

        // =====================================================================
        // deshacer
        // =====================================================================
        void Registrar()
        {
            if (sincronizando) return;
            pasado.Add(doc.Clonar());
            if (pasado.Count > 80) pasado.RemoveAt(0);
            futuro.Clear();
            ActualizarBarra();
        }

        void Deshacer()
        {
            if (pasado.Count == 0) return;
            var prev = pasado[pasado.Count - 1]; pasado.RemoveAt(pasado.Count - 1);
            futuro.Add(doc);
            sel = doc.Equivalente(sel, prev); doc = prev;
            Refrescar(); Estado("Deshecho.");
        }

        void Rehacer()
        {
            if (futuro.Count == 0) return;
            var sig = futuro[futuro.Count - 1]; futuro.RemoveAt(futuro.Count - 1);
            pasado.Add(doc);
            sel = doc.Equivalente(sel, sig); doc = sig;
            Refrescar(); Estado("Rehecho.");
        }

        // =====================================================================
        // acciones
        // =====================================================================
        static Bitmap CargarImagen(string ruta)
        {
            using (var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var img = Image.FromStream(fs))
            {
                var b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(b)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                return b;
            }
        }

        string Pedir(string titulo)
        {
            using (var od = new OpenFileDialog())
            {
                od.Title = titulo;
                od.Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos los archivos|*.*";
                string sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GTA San Andreas User Files", "SAMP", "screens");
                if (Directory.Exists(sc)) od.InitialDirectory = sc;
                return od.ShowDialog(this) == DialogResult.OK ? od.FileName : null;
            }
        }

        void AbrirFondo(string ruta)
        {
            if (ruta == null) ruta = Pedir("Captura del juego (el fondo, sin chat)");
            if (ruta == null) return;
            Bitmap b;
            try { b = CargarImagen(ruta); }
            catch (Exception ex) { Aviso("No se pudo abrir la imagen.\n\n" + ex.Message); return; }
            Registrar();
            var f = new CapaFondo(); f.Img = b; f.Nombre = "Captura del juego";
            doc.Fondo = f;
            AjustarFondo(true);
            sel = f;
            Refrescar();
            Estado("Captura: " + Path.GetFileName(ruta) + " (" + b.Width + "×" + b.Height + ").  Ahora «Importar chat».");
        }

        async void ImportarChat(string ruta)
        {
            if (string.IsNullOrEmpty(idiomaOcr)) { Aviso("Windows no tiene ningún idioma con lector de texto (OCR).\n\nConfiguración > Hora e idioma > Idioma > agregar Español."); return; }
            if (ruta == null) ruta = Pedir("Imagen con el chat (fondo negro o la captura con el chat)");
            if (ruta == null) return;
            Bitmap chat;
            try { chat = CargarImagen(ruta); }
            catch (Exception ex) { Aviso("No se pudo abrir la imagen.\n\n" + ex.Message); return; }

            Estado("Leyendo el chat… (unos segundos)");
            UseWaitCursor = true; barra.Enabled = false;
            ResultadoLectura lec = null; Exception fallo = null;
            string idioma = idiomaOcr;
            try
            {
                var copia = (Bitmap)chat.Clone();
                lec = await Task.Run(delegate
                {
                    try { return Clasificador.Procesar(MotorOcr.LeerChat(copia, idioma), new Pixeles(copia)); }
                    finally { copia.Dispose(); }
                });
            }
            catch (Exception ex) { fallo = ex; }
            finally { UseWaitCursor = false; barra.Enabled = true; }

            if (fallo != null) { Aviso(fallo.Message); Estado(""); return; }
            if (lec.Lineas.Count == 0) { Aviso("No encontré chat en esa imagen.\n\nProbá con la captura original (sin achicar)."); Estado(""); return; }

            using (var rv = new Revision(chat, lec.Lineas, lec.Descartadas))
            {
                if (rv.ShowDialog(this) != DialogResult.OK) { Estado("Importación cancelada."); return; }
                Registrar();
                var g = new GrupoChat();
                doc.Contador++;
                g.Nombre = "Chat " + doc.Contador; g.Todas = rv.Resultado; g.Chat = chat;
                var est = new Estilo(); est.Tam = ProponerTam();
                PointF origen = new PointF(margen, margen);
                if (doc.Grupos.Count > 0)
                {
                    var r = doc.Grupos[doc.Grupos.Count - 1].Caja();
                    if (!r.IsEmpty) origen = new PointF(margen, Math.Min(doc.H - margen - 40, r.Bottom + est.Tam));
                }
                g.Rearmar(origen, est);
                // que el borde visible del texto quede justo en la zona segura
                var cg = g.Caja();
                if (!cg.IsEmpty) Desplazar(g, margen - cg.Left, origen.Y == margen ? margen - cg.Top : 0);
                doc.Grupos.Add(g);
                sel = g;
                Refrescar();
                Estado(g.Nombre + ": " + g.Piezas.Count + " trozo(s). Arrastralo a su lugar; las esquinas cambian el tamaño.");
            }
        }

        float ProponerTam()
        {
            float t = (float)Math.Round(doc.H / 600f * 12f * 2) / 2f;
            return Math.Max(8f, Math.Min(40f, t));
        }

        void RevisarLineas()
        {
            var g = sel as GrupoChat; if (g == null && sel is Pieza) g = ((Pieza)sel).Grupo;
            if (g == null) { Estado("Elegí un chat para revisar sus líneas."); return; }
            using (var rv = new Revision(g.Chat, g.Todas, -1))
            {
                if (rv.ShowDialog(this) != DialogResult.OK) return;
                Registrar();
                g.Todas = rv.Resultado;
                g.Rearmar(PointF.Empty, new Estilo());
                if (sel is Pieza) sel = g;
                Refrescar();
            }
        }

        void Borrar()
        {
            if (sel == null) return;
            Registrar();
            var p = sel as Pieza;
            if (p != null)
            {
                foreach (int i in p.Indices) p.Grupo.Todas[i].Usar = false;
                p.Grupo.Piezas.Remove(p);
                if (p.Grupo.Piezas.Count == 0) doc.Grupos.Remove(p.Grupo);
            }
            else if (sel is GrupoChat) doc.Grupos.Remove((GrupoChat)sel);
            else if (sel is CapaFondo) doc.Fondo = null;
            sel = null;
            Refrescar();
        }

        void Mover(int dir)
        {
            var g = sel as GrupoChat; if (g == null && sel is Pieza) g = ((Pieza)sel).Grupo;
            if (g == null) return;
            int i = doc.Grupos.IndexOf(g), j = i + dir;
            if (j < 0 || j >= doc.Grupos.Count) return;
            Registrar();
            doc.Grupos.RemoveAt(i); doc.Grupos.Insert(j, g);
            Refrescar();
        }

        void Reordenar(GrupoChat g, int posVisual)
        {
            Registrar();
            doc.Grupos.Remove(g);
            int idx = Math.Max(0, Math.Min(doc.Grupos.Count, doc.Grupos.Count - posVisual));
            doc.Grupos.Insert(idx, g);
            sel = g;
            Refrescar();
        }

        void AlternarVisible(object o)
        {
            Registrar();
            var c = o as Capa; if (c != null) c.Visible = !c.Visible;
            Refrescar();
        }

        void Seleccionar(object o)
        {
            if (o == sel) return;
            sel = o;
            vista.Invalidate(); ActualizarCapas(); ActualizarProps();
        }

        void CambiarLienzo(int w, int h)
        {
            if (w == doc.W && h == doc.H) return;
            Registrar();
            doc.W = w; doc.H = h;
            if (doc.Fondo != null) AjustarFondo(true);
            ajustarAuto = true;
            Refrescar();
            Estado("Lienzo: " + w + " × " + h);
        }

        public bool modoPrueba;

        // Al abrir: ¿con qué tamaño se editan las SS? (los dos más usados + personalizado)
        void ElegirTamanoInicial()
        {
            using (var f = new Form())
            {
                f.Text = "Tamaño de las screenshots"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(440, 244); f.BackColor = Tema.Panel; f.ForeColor = Tema.Texto;
                f.ShowInTaskbar = false; f.Font = Tema.Letra; if (Tema.IconoApp != null) f.Icon = Tema.IconoApp;
                var t = new Label(); t.Text = "¿Con qué tamaño vas a editar las SS?"; t.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
                t.SetBounds(20, 16, 400, 26); f.Controls.Add(t);
                int elegido = 0;
                Func<string, string, int, int, Button> boton = delegate (string titulo, string sub, int y, int valor)
                {
                    var b = Tema.Boton_(titulo + "     ·  " + sub); b.SetBounds(20, y, 400, 46); b.TextAlign = ContentAlignment.MiddleLeft;
                    b.Padding = new Padding(12, 0, 0, 0); b.Font = new Font("Segoe UI", 10f);
                    b.Click += delegate { elegido = valor; f.DialogResult = DialogResult.OK; };
                    f.Controls.Add(b); return b;
                };
                var b1 = boton("800 × 600", "el clásico de los foros", 54, 1);
                boton("1200 × 500", "panorámico", 106, 2);
                boton("Personalizado…", "elegís ancho y alto", 158, 3);
                var cr = new Label(); cr.Text = "Hecho por Knxyv  ·  ¿Sugerencias? Discord @knvyv"; cr.ForeColor = Color.FromArgb(170, 150, 210);
                cr.SetBounds(20, 214, 400, 20); cr.TextAlign = ContentAlignment.MiddleCenter; f.Controls.Add(cr);
                f.AcceptButton = b1;
                if (f.ShowDialog(this) != DialogResult.OK) return; // cerrar = queda 800 × 600
                if (elegido == 2) { doc.W = 1200; doc.H = 500; ajustarAuto = true; Refrescar(); }
                else if (elegido == 3) { OtroTamano(); pasado.Clear(); ActualizarBarra(); }
                Estado("Lienzo " + doc.W + " × " + doc.H + ". Empezá con «Abrir captura» (arriba) o arrastrá la imagen a la ventana.");
            }
        }

        void OtroTamano()
        {
            using (var f = new Form())
            {
                f.Text = "Tamaño del lienzo"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(260, 120); f.BackColor = Tema.Panel; f.ForeColor = Tema.Texto;
                var nw = new NumericUpDown(); nw.Maximum = 8000; nw.Minimum = 100; nw.Value = doc.W; nw.SetBounds(70, 14, 80, 24);
                var nh = new NumericUpDown(); nh.Maximum = 8000; nh.Minimum = 100; nh.Value = doc.H; nh.SetBounds(70, 46, 80, 24);
                var l1 = new Label(); l1.Text = "Ancho"; l1.SetBounds(14, 16, 50, 20);
                var l2 = new Label(); l2.Text = "Alto"; l2.SetBounds(14, 48, 50, 20);
                var ok = Tema.Boton_("Aceptar"); ok.SetBounds(160, 80, 86, 28); ok.DialogResult = DialogResult.OK;
                f.Controls.AddRange(new Control[] { nw, nh, l1, l2, ok }); f.AcceptButton = ok;
                if (f.ShowDialog(this) == DialogResult.OK) CambiarLienzo((int)nw.Value, (int)nh.Value);
            }
        }

        void AjustarFondo(bool llenar)
        {
            var f = doc.Fondo; if (f == null) return;
            float sx = (float)doc.W / f.Img.Width, sy = (float)doc.H / f.Img.Height;
            f.Esc = llenar ? Math.Max(sx, sy) : Math.Min(sx, sy);
            CentrarFondo();
        }

        void CentrarFondo()
        {
            var f = doc.Fondo; if (f == null) return;
            f.X = (doc.W - f.Img.Width * f.Esc) / 2f; f.Y = (doc.H - f.Img.Height * f.Esc) / 2f;
            Refrescar();
        }

        void EscalarFondoCentro(float nueva)
        {
            var f = doc.Fondo; if (f == null) return;
            nueva = Math.Max(0.02f, Math.Min(20f, nueva));
            float cx = f.X + f.Img.Width * f.Esc / 2f, cy = f.Y + f.Img.Height * f.Esc / 2f;
            f.Esc = nueva;
            f.X = cx - f.Img.Width * nueva / 2f; f.Y = cy - f.Img.Height * nueva / 2f;
            sucio = true; vista.Invalidate();
        }

        List<Pieza> PiezasSel()
        {
            var r = new List<Pieza>();
            var p = sel as Pieza; if (p != null) r.Add(p);
            var g = sel as GrupoChat; if (g != null) r.AddRange(g.Piezas);
            return r;
        }

        void AplicarEstilo()
        {
            if (sincronizando) return;
            foreach (var p in PiezasSel())
            {
                p.Est.Tam = dTam.Valor; p.Est.Contorno = dCont.Valor; p.Est.Interlineado = dInter.Valor; p.Est.Suave = chSuave.Checked;
            }
            // al cambiar tamaño o interlineado de un grupo, los trozos siguen pegados como en el chat
            var g = sel as GrupoChat;
            if (g != null && g.Piezas.Count > 1) Reapilar(g);
            sucio = true; vista.Invalidate(); ActualizarCapas();
        }

        // si los trozos de un grupo estaban uno debajo del otro, los vuelve a apilar con el nuevo tamaño
        void Reapilar(GrupoChat g)
        {
            for (int k = 1; k < g.Piezas.Count; k++)
            {
                var a = g.Piezas[k - 1]; var b = g.Piezas[k];
                if (Math.Abs(b.X - a.X) < 0.5f) b.Y = a.Y + a.Indices.Count * a.Paso();
            }
        }

        void ElegirColorContorno()
        {
            var est = EstiloSel(); if (est == null) return;
            using (var cd = new ColorDialog())
            {
                cd.Color = est.ColorContorno; cd.FullOpen = true;
                if (cd.ShowDialog(this) != DialogResult.OK) return;
                Registrar();
                foreach (var p in PiezasSel()) p.Est.ColorContorno = cd.Color;
                bColorCont.Invalidate(); sucio = true; vista.Invalidate();
            }
        }

        RectangleF CajaDe(object o)
        {
            if (o is CapaFondo) return ((CapaFondo)o).Caja();
            if (o is GrupoChat) return ((GrupoChat)o).Caja();
            if (o is Pieza) return ((Pieza)o).Caja();
            return RectangleF.Empty;
        }

        void Alinear(int tipo)
        {
            if (sel == null) return;
            var r = CajaDe(sel); if (r.IsEmpty) return;
            var zona = mostrarMargen ? new RectangleF(margen, margen, doc.W - 2 * margen, doc.H - 2 * margen) : new RectangleF(0, 0, doc.W, doc.H);
            float dx = 0, dy = 0;
            switch (tipo)
            {
                case 0: dx = zona.Left - r.Left; break;
                case 1: dx = zona.Left + zona.Width / 2 - (r.Left + r.Width / 2); break;
                case 2: dx = zona.Right - r.Right; break;
                case 3: dy = zona.Top - r.Top; break;
                case 4: dy = zona.Top + zona.Height / 2 - (r.Top + r.Height / 2); break;
                case 5: dy = zona.Bottom - r.Bottom; break;
            }
            Registrar();
            Desplazar(sel, dx, dy);
            Refrescar();
        }

        void Desplazar(object o, float dx, float dy)
        {
            var f = o as CapaFondo; if (f != null) { f.X += dx; f.Y += dy; }
            foreach (var p in PiezasDe(o)) { p.X += dx; p.Y += dy; }
            sucio = true;
        }

        static List<Pieza> PiezasDe(object o)
        {
            var r = new List<Pieza>();
            if (o is Pieza) r.Add((Pieza)o);
            if (o is GrupoChat) r.AddRange(((GrupoChat)o).Piezas);
            return r;
        }

        void Exportar(bool jpg)
        {
            if (doc.Grupos.Count == 0 && doc.Fondo == null) { Aviso("Todavía no hay nada que exportar."); return; }
            using (var sd = new SaveFileDialog())
            {
                sd.Filter = jpg ? "JPG|*.jpg" : "PNG|*.png";
                sd.FileName = "rol_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (jpg ? ".jpg" : ".png");
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Guardar(sd.FileName, jpg);
                    Estado("Exportado: " + sd.FileName + "  (" + (new FileInfo(sd.FileName).Length / 1024) + " KB)");
                }
                catch (Exception ex) { Aviso("No se pudo guardar.\n\n" + ex.Message); }
            }
        }

        void Guardar(string ruta, bool jpg)
        {
            using (var bmp = Renderizar(doc))
            {
                if (!jpg) { bmp.Save(ruta, ImageFormat.Png); return; }
                using (var rgb = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(rgb)) g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                    ImageCodecInfo codec = null;
                    foreach (var c in ImageCodecInfo.GetImageEncoders()) if (c.FormatID == ImageFormat.Jpeg.Guid) codec = c;
                    using (var ps = new EncoderParameters(1))
                    {
                        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                        rgb.Save(ruta, codec, ps);
                    }
                }
            }
        }

        void Soltar(object s, DragEventArgs e)
        {
            var arch = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (arch == null || arch.Length == 0) return;
            if (doc.Fondo == null) { AbrirFondo(arch[0]); return; }
            var r = MessageBox.Show(this, "¿Esta imagen es el CHAT?\n\nSí = leer el chat · No = usarla de fondo", "Editor de roles", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) ImportarChat(arch[0]);
            else if (r == DialogResult.No) AbrirFondo(arch[0]);
        }

        void Aviso(string m) { MessageBox.Show(this, m, "Editor de roles", MessageBoxButtons.OK, MessageBoxIcon.Information); }

        // =====================================================================
        // render
        // =====================================================================
        static Bitmap Renderizar(Documento d)
        {
            var bmp = new Bitmap(d.W, d.H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                if (d.Fondo != null && d.Fondo.Visible)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    var r = d.Fondo.Caja();
                    using (var at = new ImageAttributes())
                    {
                        at.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(d.Fondo.Img, new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height)),
                            0, 0, d.Fondo.Img.Width, d.Fondo.Img.Height, GraphicsUnit.Pixel, at);
                    }
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                }
                foreach (var gr in d.Grupos)
                {
                    if (!gr.Visible) continue;
                    foreach (var p in gr.Piezas) if (p.Visible) p.Dibujar(g);
                }
            }
            return bmp;
        }

        void CalcularAjuste()
        {
            var a = vista.ClientRectangle;
            zoom = Math.Min((a.Width - 60f) / doc.W, (a.Height - 60f) / doc.H);
            if (zoom <= 0.01f) zoom = 0.01f;
            off = new PointF((float)Math.Floor((a.Width - doc.W * zoom) / 2), (float)Math.Floor((a.Height - doc.H * zoom) / 2));
        }

        PointF CentroVista() { return new PointF(vista.ClientSize.Width / 2f, vista.ClientSize.Height / 2f); }

        void Zoom(float factor, PointF pantalla)
        {
            float nuevo = Math.Max(0.05f, Math.Min(8f, zoom * factor));
            var c = ALienzo(pantalla);
            zoom = nuevo;
            off = new PointF(pantalla.X - c.X * zoom, pantalla.Y - c.Y * zoom);
            ajustarAuto = false;
            vista.Invalidate(); ActualizarBarra();
        }

        PointF ALienzo(PointF p) { return new PointF((p.X - off.X) / zoom, (p.Y - off.Y) / zoom); }
        PointF APant(PointF c) { return new PointF(off.X + c.X * zoom, off.Y + c.Y * zoom); }
        RectangleF APant(RectangleF r) { var a = APant(r.Location); return new RectangleF(a.X, a.Y, r.Width * zoom, r.Height * zoom); }

        void Pintar(object s, PaintEventArgs e)
        {
            var g = e.Graphics;
            if (ajustarAuto) { CalcularAjuste(); ActualizarBarra(); }
            if (sucio || render == null || render.Width != doc.W || render.Height != doc.H)
            {
                if (render != null) render.Dispose();
                render = Renderizar(doc); sucio = false;
            }
            var dest = new RectangleF(off.X, off.Y, doc.W * zoom, doc.H * zoom);
            using (var br = new SolidBrush(Color.FromArgb(14, 14, 15))) g.FillRectangle(br, dest.X + 4, dest.Y + 4, dest.Width, dest.Height);
            g.InterpolationMode = zoom < 1f ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(render, dest);
            g.PixelOffsetMode = PixelOffsetMode.Default;

            if (doc.Fondo == null && doc.Grupos.Count == 0)
            {
                using (var br = new SolidBrush(Color.FromArgb(120, 120, 126)))
                using (var f = new Font("Segoe UI", 13f))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("1 · «Abrir captura» (arriba) o arrastrá la imagen acá\n2 · «Importar chat»\n3 · Acomodá con el mouse y «Exportar»", f, br, dest, sf);
            }

            if (mostrarMargen && margen > 0)
            {
                var m = APant(new RectangleF(margen, margen, doc.W - 2 * margen, doc.H - 2 * margen));
                using (var p = new Pen(Color.FromArgb(170, 0, 220, 255), 1f)) { p.DashStyle = DashStyle.Dash; g.DrawRectangle(p, m.X, m.Y, m.Width, m.Height); }
            }

            // guías inteligentes
            using (var p = new Pen(Color.FromArgb(255, 255, 40, 200), 1f))
            {
                if (guiaX.HasValue) { float x = APant(new PointF(guiaX.Value, 0)).X; g.DrawLine(p, x, dest.Top, x, dest.Bottom); }
                if (guiaY.HasValue) { float y = APant(new PointF(0, guiaY.Value)).Y; g.DrawLine(p, dest.Left, y, dest.Right, y); }
            }

            // selección + asas
            if (sel != null)
            {
                var gr = sel as GrupoChat;
                if (gr != null)
                    using (var p = new Pen(Color.FromArgb(110, 46, 140, 235))) { p.DashStyle = DashStyle.Dot; foreach (var pz in gr.Piezas) if (pz.Visible) { var rr = APant(pz.Caja()); g.DrawRectangle(p, rr.X, rr.Y, rr.Width, rr.Height); } }
                var r = CajaDe(sel);
                if (!r.IsEmpty)
                {
                    var rp = APant(r);
                    using (var p = new Pen(Tema.Acento, 1f)) g.DrawRectangle(p, rp.X, rp.Y, rp.Width, rp.Height);
                    foreach (var q in Esquinas(rp))
                    {
                        g.FillRectangle(Brushes.White, q.X - 4, q.Y - 4, 8, 8);
                        using (var p = new Pen(Tema.Acento)) g.DrawRectangle(p, q.X - 4, q.Y - 4, 8, 8);
                    }
                }
            }
        }

        static PointF[] Esquinas(RectangleF r)
        {
            return new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) };
        }

        // =====================================================================
        // mouse
        // =====================================================================
        int AsaEn(Point p)
        {
            if (sel == null) return -1;
            var r = CajaDe(sel); if (r.IsEmpty) return -1;
            var es = Esquinas(APant(r));
            for (int i = 0; i < 4; i++) if (Math.Abs(p.X - es[i].X) <= 7 && Math.Abs(p.Y - es[i].Y) <= 7) return i;
            return -1;
        }

        object Tocar(PointF c)
        {
            for (int gi = doc.Grupos.Count - 1; gi >= 0; gi--)
            {
                var g = doc.Grupos[gi]; if (!g.Visible) continue;
                for (int k = g.Piezas.Count - 1; k >= 0; k--)
                {
                    var p = g.Piezas[k]; if (!p.Visible) continue;
                    var r = p.Caja(); r.Inflate(3, 3);
                    if (r.Contains(c)) return p;
                }
            }
            if (doc.Fondo != null && doc.Fondo.Visible && doc.Fondo.Caja().Contains(c)) return doc.Fondo;
            return null;
        }

        void GuardarInicio()
        {
            pos0.Clear(); tam0.Clear();
            var f = sel as CapaFondo;
            if (f != null) { pos0.Add(new PointF(f.X, f.Y)); tam0.Add(f.Esc); }
            foreach (var p in PiezasDe(sel)) { pos0.Add(new PointF(p.X, p.Y)); tam0.Add(p.Est.Tam); }
            caja0 = CajaDe(sel);
        }

        void MDown(object s, MouseEventArgs e)
        {
            vista.Focus();
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && espacio))
            {
                modo = Modo.Paneo; iniPant = e.Location; offIni = off; vista.Cursor = Cursors.SizeAll; return;
            }
            if (e.Button != MouseButtons.Left) return;
            var c = ALienzo(e.Location);
            int asa = AsaEn(e.Location);
            if (asa >= 0)
            {
                GuardarInicio();
                var es = Esquinas(caja0);
                esquina0 = es[asa]; ancla = es[(asa + 2) % 4];
                modo = Modo.Escalar; registrado = false; iniPant = e.Location;
                return;
            }
            object t;
            if ((sel is GrupoChat || sel is Pieza) && Inflado(CajaDe(sel), 3).Contains(c) && !(sel is Pieza && Tocar(c) is Pieza && Tocar(c) != sel)) t = sel;
            else t = Tocar(c);
            Seleccionar(t);
            if (t == null) return;
            GuardarInicio();
            ini = c; iniPant = e.Location; registrado = false; modo = Modo.Mover;
        }

        static RectangleF Inflado(RectangleF r, float d) { r.Inflate(d, d); return r; }

        void MMove(object s, MouseEventArgs e)
        {
            var c = ALienzo(e.Location);
            estadoDer.Text = "x " + (int)c.X + "  y " + (int)c.Y + "   ·   Lienzo " + doc.W + "×" + doc.H + "   ·   Zoom " + Math.Round(zoom * 100) + "%";
            switch (modo)
            {
                case Modo.Paneo:
                    off = new PointF(offIni.X + e.X - iniPant.X, offIni.Y + e.Y - iniPant.Y); ajustarAuto = false; vista.Invalidate(); return;
                case Modo.Mover:
                    if (!registrado)
                    {
                        if (Math.Abs(e.X - iniPant.X) + Math.Abs(e.Y - iniPant.Y) < 3) return;
                        Registrar(); registrado = true;
                    }
                    MoverA(c.X - ini.X, c.Y - ini.Y, (ModifierKeys & Keys.Alt) == 0);
                    return;
                case Modo.Escalar:
                    if (!registrado) { Registrar(); registrado = true; }
                    EscalarA(c);
                    return;
            }
            // cursor según lo que hay debajo
            int asa = AsaEn(e.Location);
            if (espacio) vista.Cursor = Cursors.Hand;
            else if (asa == 0 || asa == 2) vista.Cursor = Cursors.SizeNWSE;
            else if (asa == 1 || asa == 3) vista.Cursor = Cursors.SizeNESW;
            else if (Tocar(c) != null || (sel != null && CajaDe(sel).Contains(c))) vista.Cursor = Cursors.SizeAll;
            else vista.Cursor = Cursors.Default;
        }

        void MUp(object s, MouseEventArgs e)
        {
            if (modo == Modo.Paneo) vista.Cursor = espacio ? Cursors.Hand : Cursors.Default;
            bool cambio = registrado;
            modo = Modo.Nada; guiaX = null; guiaY = null;
            if (cambio) Refrescar(); else vista.Invalidate();
        }

        void MoverA(float dx, float dy, bool iman)
        {
            float ax = 0, ay = 0; guiaX = null; guiaY = null;
            if (iman)
            {
                var r = caja0; r.Offset(dx, dy);
                Iman(r, out ax, out ay);
            }
            int k = 0;
            var f = sel as CapaFondo;
            if (f != null) { f.X = pos0[0].X + dx + ax; f.Y = pos0[0].Y + dy + ay; }
            foreach (var p in PiezasDe(sel)) { p.X = pos0[k].X + dx + ax; p.Y = pos0[k].Y + dy + ay; k++; }
            sucio = true; vista.Invalidate();
        }

        // guías inteligentes: bordes y centros del lienzo, de la zona segura y de las demás capas
        void Iman(RectangleF r, out float ax, out float ay)
        {
            var xs = new List<float> { 0, doc.W / 2f, doc.W };
            var ys = new List<float> { 0, doc.H / 2f, doc.H };
            if (mostrarMargen) { xs.Add(margen); xs.Add(doc.W - margen); ys.Add(margen); ys.Add(doc.H - margen); }
            var excluir = new HashSet<object>(); excluir.Add(sel);
            foreach (var p in PiezasDe(sel)) excluir.Add(p);
            foreach (var g in doc.Grupos)
                foreach (var p in g.Piezas)
                {
                    if (excluir.Contains(p) || !p.Visible || !g.Visible) continue;
                    var b = p.Caja();
                    xs.Add(b.Left); xs.Add(b.Right); ys.Add(b.Top); ys.Add(b.Bottom); ys.Add(b.Top + b.Height / 2);
                }
            float tol = 7f / zoom;
            float mejorX = tol + 1, mejorY = tol + 1; ax = 0; ay = 0;
            foreach (float borde in new[] { r.Left, r.Left + r.Width / 2, r.Right })
                foreach (float x in xs) { float d = x - borde; if (Math.Abs(d) < Math.Abs(mejorX)) { mejorX = d; guiaX = x; } }
            foreach (float borde in new[] { r.Top, r.Top + r.Height / 2, r.Bottom })
                foreach (float y in ys) { float d = y - borde; if (Math.Abs(d) < Math.Abs(mejorY)) { mejorY = d; guiaY = y; } }
            if (Math.Abs(mejorX) <= tol) ax = mejorX; else guiaX = null;
            if (Math.Abs(mejorY) <= tol) ay = mejorY; else guiaY = null;
        }

        void EscalarA(PointF c)
        {
            // proporcional, anclado en la esquina opuesta (proyección sobre la diagonal)
            float vx = esquina0.X - ancla.X, vy = esquina0.Y - ancla.Y;
            float len2 = vx * vx + vy * vy; if (len2 < 1) return;
            float f = ((c.X - ancla.X) * vx + (c.Y - ancla.Y) * vy) / len2;
            f = Math.Max(0.05f, Math.Min(20f, f));
            int k = 0;
            var fo = sel as CapaFondo;
            if (fo != null)
            {
                fo.Esc = Math.Max(0.02f, tam0[0] * f);
                float real = fo.Esc / tam0[0];
                fo.X = ancla.X + (pos0[0].X - ancla.X) * real; fo.Y = ancla.Y + (pos0[0].Y - ancla.Y) * real;
                sincronizando = true; dEscFondo.Valor = fo.Esc * 100f; sincronizando = false;
            }
            foreach (var p in PiezasDe(sel))
            {
                float t = Math.Max(4f, Math.Min(200f, tam0[k] * f));
                float real = t / tam0[k];
                p.Est.Tam = t;
                p.X = ancla.X + (pos0[k].X - ancla.X) * real; p.Y = ancla.Y + (pos0[k].Y - ancla.Y) * real;
                k++;
            }
            var est = EstiloSel(); if (est != null) { sincronizando = true; dTam.Valor = est.Tam; sincronizando = false; }
            sucio = true; vista.Invalidate();
        }

        void Rueda(object s, MouseEventArgs e)
        {
            Zoom(e.Delta > 0 ? 1.15f : 1 / 1.15f, e.Location);
        }

        // =====================================================================
        // teclado
        // =====================================================================
        void Teclas(object s, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) { Deshacer(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.Y) { Rehacer(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.E) { Exportar(false); e.Handled = true; return; }
            if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)) { ajustarAuto = true; vista.Invalidate(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Space && !espacio) { espacio = true; vista.Cursor = Cursors.Hand; e.Handled = true; e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Delete) { Borrar(); e.Handled = true; return; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            bool flecha = k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down;
            if (flecha && sel != null && !(ActiveControl is TextBox) && !(ActiveControl is NumericUpDown))
            {
                float paso = (keyData & Keys.Shift) != 0 ? 10f : 1f;
                float dx = k == Keys.Left ? -paso : k == Keys.Right ? paso : 0;
                float dy = k == Keys.Up ? -paso : k == Keys.Down ? paso : 0;
                if ((DateTime.Now - ultimoEmpuje).TotalMilliseconds > 700) Registrar();
                ultimoEmpuje = DateTime.Now;
                Desplazar(sel, dx, dy);
                vista.Invalidate();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // =====================================================================
        // modo de verificación (sin diálogos)
        // =====================================================================
        public void Prueba(string rutaFondo, string rutaChat, string salida, int w, int h)
        {
            doc.W = w; doc.H = h;
            var f = new CapaFondo(); f.Img = CargarImagen(rutaFondo); f.Nombre = "Captura del juego"; doc.Fondo = f; AjustarFondo(true);
            foreach (var ruta in rutaChat.Split('|'))
            {
                var chat = CargarImagen(ruta);
                var lec = Clasificador.Procesar(MotorOcr.LeerChat(chat, idiomaOcr), new Pixeles(chat));
                var g = new GrupoChat(); doc.Contador++; g.Nombre = "Chat " + doc.Contador; g.Todas = lec.Lineas; g.Chat = chat;
                var est = new Estilo(); est.Tam = ProponerTam();
                PointF origen = new PointF(margen, margen);
                if (doc.Grupos.Count > 0) origen = new PointF(margen, doc.Grupos[doc.Grupos.Count - 1].Caja().Bottom + est.Tam);
                g.Rearmar(origen, est);
                doc.Grupos.Add(g);
            }
            Guardar(salida, false);
            Guardar(Path.ChangeExtension(salida, null) + ".jpg", true);
            sel = doc.Grupos.Count > 0 ? (object)doc.Grupos[0] : doc.Fondo;
            Size = new Size(1360, 860);
            Show(); Refrescar(); Application.DoEvents();

            // --- secuencia de mouse simulada (mover, escalar por la esquina, imán, deshacer) ---
            var log = new System.Text.StringBuilder();
            Func<string> caja = delegate { var c = CajaDe(sel); return "caja=(" + c.X.ToString("0.0") + "," + c.Y.ToString("0.0") + "," + c.Width.ToString("0.0") + "," + c.Height.ToString("0.0") + ") tam=" + (EstiloSel() != null ? EstiloSel().Tam.ToString("0.0") : "-"); };
            Action<Point, Point> arrastrar = delegate (Point a, Point b2)
            {
                MDown(null, new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
                for (int i = 1; i <= 10; i++)
                    MMove(null, new MouseEventArgs(MouseButtons.Left, 0, a.X + (b2.X - a.X) * i / 10, a.Y + (b2.Y - a.Y) * i / 10, 0));
                MUp(null, new MouseEventArgs(MouseButtons.Left, 1, b2.X, b2.Y, 0));
            };
            log.AppendLine("zoom=" + zoom.ToString("0.000") + " inicio " + caja());
            var rp = APant(CajaDe(sel));
            var p0 = Point.Round(new PointF(rp.X + 30, rp.Y + 8));
            arrastrar(p0, new Point(p0.X + 120, p0.Y + 60));
            log.AppendLine("tras mover +120,+60 px pantalla: " + caja());
            rp = APant(CajaDe(sel));
            var esq = Point.Round(new PointF(rp.Right, rp.Bottom));
            arrastrar(esq, new Point(esq.X + 80, esq.Y + 40));
            log.AppendLine("tras escalar por la esquina: " + caja());
            // imán: llevar el borde izquierdo a 4 px (de lienzo) del margen seguro
            var cj = CajaDe(sel); rp = APant(cj);
            p0 = Point.Round(new PointF(rp.X + 30, rp.Y + 8));
            int dxp = (int)Math.Round((margen + 4 - cj.Left) * zoom);
            arrastrar(p0, new Point(p0.X + dxp, p0.Y));
            log.AppendLine("tras acercar al margen (" + margen + "): " + caja());
            log.AppendLine("pasos para deshacer: " + pasado.Count);
            Deshacer(); Deshacer(); Deshacer();
            log.AppendLine("tras 3 deshacer: " + caja());
            Rehacer();
            log.AppendLine("tras rehacer: " + caja());
            File.WriteAllText(Path.ChangeExtension(salida, null) + "_log.txt", log.ToString());
            Refrescar(); Application.DoEvents();
            using (var b = new Bitmap(Width, Height)) { DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(Path.ChangeExtension(salida, null) + "_ui.png", ImageFormat.Png); }
            Close();
        }
    }

    static class Programa
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Verificación sin ventanas: EditorRoles.exe --prueba fondo.png "chat1.png|chat2.png" salida.png [ancho alto]
            if (args.Length >= 4 && args[0] == "--prueba")
            {
                int w = args.Length >= 6 ? int.Parse(args[4]) : 800, h = args.Length >= 6 ? int.Parse(args[5]) : 600;
                var pp = new Principal(); pp.modoPrueba = true; pp.Prueba(args[1], args[2], args[3], w, h);
                return 0;
            }
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("Error inesperado:\n\n" + e.Exception.Message, "Editor de roles", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new Principal());
            return 0;
        }
    }
}
