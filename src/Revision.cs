// Pantalla de revisión: muestra lo que detectó el OCR. Se marca, se cambia el tipo/color y se corrige
// el texto, pero NO se pueden agregar líneas nuevas: todo nace de la captura.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EditorRoles
{
    public class Revision : Form
    {
        readonly Image chat;
        readonly List<Linea> lineas;
        readonly DataGridView grid;
        readonly PanelDoble vista;
        bool cargando;

        public List<Linea> Resultado { get { return lineas; } }

        public Revision(Image chat, List<Linea> origen, int descartadas)
        {
            this.chat = chat;
            lineas = new List<Linea>();
            foreach (var l in origen) lineas.Add(l.Copia());

            Text = "Líneas del chat — marcá las que van a la imagen" +
                (descartadas > 0 ? "   (se descartaron " + descartadas + " cosas que no son chat: hora, nombres, HUD…)" : "");
            BackColor = Tema.Panel; ForeColor = Tema.Texto; if (Tema.IconoApp != null) Icon = Tema.IconoApp;
            Size = new Size(1150, 780);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(800, 500);
            Font = new Font("Segoe UI", 9f);

            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterDistance = 240;
            Controls.Add(split);

            vista = new PanelDoble();
            vista.Dock = DockStyle.Fill;
            vista.BackColor = Color.FromArgb(24, 24, 24);
            vista.Paint += PintarVista;
            vista.MouseDown += ClickVista;
            split.Panel1.Controls.Add(vista);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;      // regla: ninguna línea se escribe a mano
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = Color.FromArgb(32, 32, 32);
            grid.BorderStyle = BorderStyle.None;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = 26;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Tema.Cabecera;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Tema.Texto;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Tema.Cabecera;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.GridColor = Tema.Borde;
            grid.DefaultCellStyle.BackColor = Tema.Panel; grid.DefaultCellStyle.ForeColor = Tema.Texto;
            grid.DefaultCellStyle.SelectionBackColor = Tema.Seleccion; grid.DefaultCellStyle.SelectionForeColor = Color.White;

            var cUsar = new DataGridViewCheckBoxColumn(); cUsar.HeaderText = "Usar"; cUsar.Width = 45; cUsar.Name = "usar";
            var cTipo = new DataGridViewComboBoxColumn(); cTipo.HeaderText = "Tipo"; cTipo.Width = 80; cTipo.Name = "tipo";
            cTipo.Items.AddRange("me", "do", "habla", "otra"); cTipo.FlatStyle = FlatStyle.Flat;
            var cColor = new DataGridViewTextBoxColumn(); cColor.HeaderText = "Color"; cColor.Width = 75; cColor.Name = "color"; cColor.ReadOnly = true;
            var cTexto = new DataGridViewTextBoxColumn(); cTexto.HeaderText = "Texto (editable: comas, palabras mal leídas…)"; cTexto.Name = "texto";
            cTexto.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            cTexto.DefaultCellStyle.Font = new Font("Arial", 10f, FontStyle.Bold);
            cTexto.DefaultCellStyle.BackColor = Color.Black;
            cTexto.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 60, 90);
            grid.Columns.AddRange(cUsar, cTipo, cColor, cTexto);
            split.Panel2.Controls.Add(grid);

            var abajo = new FlowLayoutPanel();
            abajo.Dock = DockStyle.Bottom; abajo.Height = 44; abajo.Padding = new Padding(6);
            abajo.FlowDirection = FlowDirection.RightToLeft; abajo.BackColor = Tema.Cabecera;
            var bOk = Boton("Agregar al lienzo", 150); bOk.Click += Aceptar; bOk.Font = new Font(Font, FontStyle.Bold);
            var bCancel = Boton("Cancelar", 90); bCancel.DialogResult = DialogResult.Cancel;
            var bRest = Boton("Restaurar detección", 140); bRest.Click += delegate { Restaurar(); };
            var bNing = Boton("Desmarcar todas", 120); bNing.Click += delegate { Marcar(false); };
            var bTodas = Boton("Marcar todas", 110); bTodas.Click += delegate { Marcar(true); };
            var ayuda = new Label();
            ayuda.AutoSize = true; ayuda.Padding = new Padding(0, 7, 12, 0); ayuda.ForeColor = Tema.Tenue;
            ayuda.Text = "Doble clic en Color para cambiarlo · Se pre-marcan /me, /do y habla";
            abajo.Controls.AddRange(new Control[] { bOk, bCancel, bRest, bNing, bTodas, ayuda });
            Controls.Add(abajo);
            AcceptButton = null; CancelButton = bCancel;

            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell != null && grid.CurrentCell.ColumnIndex != 3)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += CambioCelda;
            grid.CellDoubleClick += DobleClic;
            grid.SelectionChanged += delegate { vista.Invalidate(); };
            grid.EditingControlShowing += delegate (object s, DataGridViewEditingControlShowingEventArgs e)
            {
                if (grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == 3)
                {
                    var l = lineas[grid.CurrentCell.RowIndex];
                    e.Control.BackColor = Color.Black; e.Control.ForeColor = l.Color;
                }
            };
            grid.DataError += delegate (object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            Cargar();
        }

        Button Boton(string t, int w)
        {
            var b = Tema.Boton_(t); b.Width = w; b.Height = 30; return b;
        }

        void Cargar()
        {
            cargando = true;
            grid.Rows.Clear();
            foreach (var l in lineas)
            {
                int i = grid.Rows.Add(l.Usar, Linea.Nombre(l.Tipo), Hex(l.Color), l.Texto);
                Estilo(i);
            }
            cargando = false;
            vista.Invalidate();
        }

        void Estilo(int i)
        {
            var l = lineas[i];
            var cc = grid.Rows[i].Cells[2];
            cc.Value = Hex(l.Color);
            cc.Style.BackColor = l.Color; cc.Style.SelectionBackColor = l.Color;
            int lum = (l.Color.R * 299 + l.Color.G * 587 + l.Color.B * 114) / 1000;
            cc.Style.ForeColor = lum > 140 ? Color.Black : Color.White; cc.Style.SelectionForeColor = cc.Style.ForeColor;
            var ct = grid.Rows[i].Cells[3];
            ct.Style.ForeColor = l.Color; ct.Style.SelectionForeColor = l.Color;
            grid.Rows[i].DefaultCellStyle.BackColor = l.Usar ? Color.FromArgb(38, 48, 38) : Color.FromArgb(40, 40, 40);
            grid.Rows[i].DefaultCellStyle.ForeColor = Color.Gainsboro;
        }

        static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        void CambioCelda(object s, DataGridViewCellEventArgs e)
        {
            if (cargando || e.RowIndex < 0) return;
            var l = lineas[e.RowIndex];
            var v = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            if (e.ColumnIndex == 0) l.Usar = v is bool && (bool)v;
            else if (e.ColumnIndex == 1)
            {
                l.Tipo = Linea.DeNombre(v as string);
                if ((l.Tipo == Tipo.Me || l.Tipo == Tipo.Do) && !Clasificador.EsMorado(l.Color)) l.Color = Clasificador.MORADO;
                if (l.Tipo == Tipo.Habla && Clasificador.EsMorado(l.Color)) l.Color = Color.White;
            }
            else if (e.ColumnIndex == 3)
            {
                string t = (v as string) ?? "";
                t = t.Replace("\r", " ").Replace("\n", " ").Trim();
                if (t.Length == 0) { t = l.Texto; } // vaciar no borra la línea: para eso se desmarca
                l.Texto = t;
                cargando = true; grid.Rows[e.RowIndex].Cells[3].Value = t; cargando = false;
            }
            Estilo(e.RowIndex);
            vista.Invalidate();
        }

        void DobleClic(object s, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
            var l = lineas[e.RowIndex];
            using (var cd = new ColorDialog())
            {
                cd.Color = l.Color; cd.FullOpen = true;
                cd.CustomColors = new int[] { Ole(Clasificador.MORADO), Ole(Color.White), Ole(l.ColorDet) };
                if (cd.ShowDialog(this) == DialogResult.OK) { l.Color = cd.Color; Estilo(e.RowIndex); }
            }
        }
        static int Ole(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        void Marcar(bool v)
        {
            foreach (var l in lineas) l.Usar = v;
            Cargar();
        }

        void Restaurar()
        {
            foreach (var l in lineas) { l.Usar = l.UsarDet; l.Tipo = l.TipoDet; l.Texto = l.TextoDet; l.Color = l.ColorDet; }
            Cargar();
        }

        void Aceptar(object s, EventArgs e)
        {
            grid.EndEdit();
            int n = 0; foreach (var l in lineas) if (l.Usar) n++;
            if (n == 0) { MessageBox.Show(this, "No hay ninguna línea marcada.", "Editor de roles", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            DialogResult = DialogResult.OK;
            Close();
        }

        // --- vista previa de la captura con las cajas detectadas ---
        RectangleF Area(out float z)
        {
            var r = vista.ClientRectangle;
            z = Math.Min((r.Width - 16f) / chat.Width, (r.Height - 16f) / chat.Height);
            if (z <= 0) z = 0.01f;
            float w = chat.Width * z, h = chat.Height * z;
            return new RectangleF((r.Width - w) / 2, (r.Height - h) / 2, w, h);
        }

        void PintarVista(object s, PaintEventArgs e)
        {
            float z; var a = Area(out z);
            e.Graphics.InterpolationMode = z < 1 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(chat, a);
            int sel = grid.CurrentRow != null ? grid.CurrentRow.Index : -1;
            for (int i = 0; i < lineas.Count; i++)
            {
                var l = lineas[i];
                var r = new RectangleF(a.X + l.Caja.X * z - 2, a.Y + l.Caja.Y * z - 2, l.Caja.Width * z + 4, l.Caja.Height * z + 4);
                Color c = i == sel ? Color.Yellow : (l.Usar ? Color.FromArgb(200, 80, 220, 120) : Color.FromArgb(120, 150, 150, 150));
                using (var p = new Pen(c, i == sel ? 2f : 1f)) e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
        }

        void ClickVista(object s, MouseEventArgs e)
        {
            float z; var a = Area(out z);
            for (int i = 0; i < lineas.Count; i++)
            {
                var l = lineas[i];
                var r = new RectangleF(a.X + l.Caja.X * z - 3, a.Y + l.Caja.Y * z - 3, l.Caja.Width * z + 6, l.Caja.Height * z + 6);
                if (r.Contains(e.Location))
                {
                    grid.CurrentCell = grid.Rows[i].Cells[3];
                    if (e.Clicks >= 2 || e.Button == MouseButtons.Right) { l.Usar = !l.Usar; grid.Rows[i].Cells[0].Value = l.Usar; }
                    return;
                }
            }
        }
    }


}
