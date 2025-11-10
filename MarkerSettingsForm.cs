using System.Drawing;
using System.Windows.Forms;

namespace GPXVideoTools
{
    public class MarkerSettingsForm : Form
    {
        private NumericUpDown _num;
        private Button _colorBtn;
        private Panel _preview;
        private Button _ok,_cancel;
        private Color _color;
        public double MarkerSize { get; private set; }
        public Color MarkerColor => _color;

        public MarkerSettingsForm(double initialSize, Color initialColor)
        {
            Text = "Tamaño y color del marcador"; Width = 360; Height = 220; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            var lbl = new Label(){Left=12,Top=12,Text="Tamaño (m):"}; Controls.Add(lbl);
            _num = new NumericUpDown(){Left=100,Top=10,Minimum=0.1M,Maximum=10M,DecimalPlaces=1,Increment=0.1M,Width=120}; _num.Value = (decimal)initialSize; _num.ValueChanged += (s,e)=> _preview.Invalidate(); Controls.Add(_num);
            _colorBtn = new Button(){Left=240,Top=8,Width=100,Text="🎨 Cambiar color..."}; _colorBtn.Click += (s,e)=>{ using(var cd=new ColorDialog()){ cd.Color = _color; if(cd.ShowDialog()==DialogResult.OK){ _color = cd.Color; _preview.Invalidate(); } } }; Controls.Add(_colorBtn);
            var lp = new Label(){Left=12,Top=50,Text="Vista previa:"}; Controls.Add(lp);
            _preview = new Panel(){Left=100,Top=45,Width=240,Height=80,BorderStyle=BorderStyle.FixedSingle}; _preview.Paint += Preview_Paint; Controls.Add(_preview);
            _ok = new Button(){Left=180,Top=140,Width=75,Text="Aceptar"}; _ok.Click += (s,e)=>{ MarkerSize = (double)_num.Value; DialogResult = DialogResult.OK; Close(); }; Controls.Add(_ok);
            _cancel = new Button(){Left=260,Top=140,Width=75,Text="Cancelar"}; _cancel.Click += (s,e)=>{ DialogResult = DialogResult.Cancel; Close(); }; Controls.Add(_cancel);
            _color = initialColor;
        }

        private void Preview_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(_preview.BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float w = _preview.Width; float h = _preview.Height; float size = (float)((double)_num.Value * 10.0f);
            float cx = w/2; float cy = h/2; var tip = new System.Drawing.PointF(cx+size/2f,cy); var left = new System.Drawing.PointF(cx-size/2f,cy-size/4f); var right = new System.Drawing.PointF(cx-size/2f,cy+size/4f);
            using(var b=new SolidBrush(_color)) e.Graphics.FillPolygon(b,new System.Drawing.PointF[]{ tip,left,right }); using(var pen=new Pen(_color,4)) e.Graphics.DrawLine(pen,cx-size/2f,cy,cx+size/4f,cy);
        }
    }
}
