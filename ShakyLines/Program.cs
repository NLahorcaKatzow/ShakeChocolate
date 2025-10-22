// .NET 8 / Windows-only (WinForms). Single-file app.
// Abre una imagen en blanco y negro y aplica un "line shaking" controlable
// desplazando suavemente solo los píxeles negros usando un campo de ruido 2D.
//
// Cómo compilar/ejecutar (PowerShell / CMD en Windows, con .NET SDK 8+):
//   dotnet new winforms -n ShakyLines -f net8.0-windows
//   cd ShakyLines
//   (Opcional) borra Program.cs y Form1.* si existen
//   copia este archivo como Program.cs y compila:
//   dotnet build
//   dotnet run
//
// No requiere paquetes NuGet extra. Usa System.Drawing en Windows.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO.Compression;
using System.IO;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ShakyLinesForm());
    }
}

public class ShakyLinesForm : Form
{
    private PictureBox picIn = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private PictureBox picOut = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private Button btnLoad = new Button { Text = "Abrir" };
    private Button btnSave = new Button { Text = "Guardar" };
    private Button btnApply = new Button { Text = "Aplicar" };

    private TrackBar tbStrength = new TrackBar { Minimum = 0, Maximum = 20, Value = 3, TickFrequency = 1 }; // px
    private TrackBar tbFreq = new TrackBar { Minimum = 1, Maximum = 40, Value = 8, TickFrequency = 1 };      // celdas por eje
    private TrackBar tbThreshold = new TrackBar { Minimum = 0, Maximum = 255, Value = 80, TickFrequency = 5 };// umbral negro

    private NumericUpDown nudSeed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = 1234, Increment = 1, ThousandsSeparator = true };
    private NumericUpDown nudFPS = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 15, Increment = 1 };
    private CheckBox cbMaskOnly = new CheckBox { Text = "Solo desplazar los negros (más nítido)", Checked = true };
    private CheckBox cbAntiAlias = new CheckBox { Text = "Antialias (bilineal)", Checked = true };

    private Label lblStrength = new Label { Text = "Intensidad (px):" };
    private Label lblFreq = new Label { Text = "Frecuencia ruido (celdas):" };
    private Label lblThreshold = new Label { Text = "Umbral negro (0-255):" };
    private Label lblSeed = new Label { Text = "Seed:" };
    private Label lblFPS = new Label { Text = "FPS:" };

    private Bitmap? srcBmp;
    private List<Bitmap> animationFrames = new List<Bitmap>();
    private System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
    private int currentFrameIndex = 0;

    public ShakyLinesForm()
    {
        Text = "Shaky Lines – B/N line shaking";
        MinimumSize = new Size(980, 640);
        
        // Initialize animation timer
        animationTimer.Tick += (_, __) => UpdateAnimationFrame();

        // Layout
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));

        picIn.Dock = DockStyle.Fill;
        picOut.Dock = DockStyle.Fill;

        var controls = BuildControlsPanel();
        table.Controls.Add(picIn, 0, 0);
        table.Controls.Add(picOut, 1, 0);
        table.SetColumnSpan(controls, 2);
        table.Controls.Add(controls, 0, 1);

        Controls.Add(table);

        btnLoad.Click += (_, __) => LoadImage();
        btnApply.Click += (_, __) => Apply();
        btnSave.Click += (_, __) => SaveImage();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer?.Stop();
            animationTimer?.Dispose();
            
            foreach (var frame in animationFrames)
                frame?.Dispose();
            animationFrames.Clear();
            
            srcBmp?.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildControlsPanel()
    {
        var p = new Panel { Dock = DockStyle.Fill };

        var flowTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight };
        flowTop.Controls.Add(btnLoad);
        flowTop.Controls.Add(btnApply);
        flowTop.Controls.Add(btnSave);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 4, Padding = new Padding(8) };
        for (int i = 0; i < 6; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        lblStrength.TextAlign = ContentAlignment.MiddleLeft;
        lblFreq.TextAlign = ContentAlignment.MiddleLeft;
        lblThreshold.TextAlign = ContentAlignment.MiddleLeft;
        lblSeed.TextAlign = ContentAlignment.MiddleLeft;
        lblFPS.TextAlign = ContentAlignment.MiddleLeft;

        grid.Controls.Add(lblStrength, 0, 0);
        grid.SetColumnSpan(tbStrength, 2);
        grid.Controls.Add(tbStrength, 1, 0);

        grid.Controls.Add(lblFreq, 3, 0);
        grid.SetColumnSpan(tbFreq, 2);
        grid.Controls.Add(tbFreq, 4, 0);

        grid.Controls.Add(lblThreshold, 0, 1);
        grid.SetColumnSpan(tbThreshold, 2);
        grid.Controls.Add(tbThreshold, 1, 1);

        grid.Controls.Add(lblSeed, 3, 1);
        grid.Controls.Add(nudSeed, 4, 1);

        grid.Controls.Add(lblFPS, 0, 2);
        grid.Controls.Add(nudFPS, 1, 2);

        grid.SetColumnSpan(cbMaskOnly, 2);
        grid.Controls.Add(cbMaskOnly, 2, 2);
        grid.SetColumnSpan(cbAntiAlias, 2);
        grid.Controls.Add(cbAntiAlias, 4, 2);

        p.Controls.Add(grid);
        p.Controls.Add(flowTop);
        return p;
    }

    private void LoadImage()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
            Title = "Abrir imagen B/N"
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            srcBmp?.Dispose();
            srcBmp = new Bitmap(ofd.FileName);
            picIn.Image = srcBmp;
            picOut.Image = null;
            
            // Stop animation when loading new image
            animationTimer.Stop();
            foreach (var frame in animationFrames)
                frame?.Dispose();
            animationFrames.Clear();
        }
    }

    private void SaveImage()
    {
        if (animationFrames.Count == 0)
        {
            MessageBox.Show(this, "Primero genera la animación.", "Guardar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        
        using var sfd = new SaveFileDialog
        {
            Filter = "ZIP|*.zip",
            Title = "Guardar animación como ZIP",
            FileName = "shaky_animation.zip"
        };
        
        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                
                using var archive = ZipFile.Open(sfd.FileName, ZipArchiveMode.Create);
                
                for (int i = 0; i < animationFrames.Count; i++)
                {
                    var entry = archive.CreateEntry($"frame_{i:D3}.png");
                    using var entryStream = entry.Open();
                    animationFrames[i].Save(entryStream, ImageFormat.Png);
                }
                
                MessageBox.Show(this, $"Animación guardada con {animationFrames.Count} frames.", "Guardar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error al guardar: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    private void UpdateAnimationFrame()
    {
        if (animationFrames.Count > 0)
        {
            currentFrameIndex = (currentFrameIndex + 1) % animationFrames.Count;
            picOut.Image = animationFrames[currentFrameIndex];
        }
    }

    private void Apply()
    {
        if (srcBmp == null)
        {
            MessageBox.Show(this, "Cargá primero una imagen en blanco y negro.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            
            // Stop current animation
            animationTimer.Stop();
            
            // Clear previous frames
            foreach (var frame in animationFrames)
                frame?.Dispose();
            animationFrames.Clear();
            
            var strength = tbStrength.Value;
            var cells = Math.Max(1, tbFreq.Value);
            var threshold = (byte)tbThreshold.Value;
            var baseSeed = (int)nudSeed.Value;
            var antialias = cbAntiAlias.Checked;
            var maskOnly = cbMaskOnly.Checked;
            var fps = (int)nudFPS.Value;
            
            // Generate frames for 1 second of animation
            for (int frame = 0; frame < fps; frame++)
            {
                // Use frame number to vary the seed for different noise patterns
                var frameSeed = baseSeed + frame;
                var frameBmp = MakeShaky(srcBmp, strength, cells, frameSeed, threshold, antialias, maskOnly);
                animationFrames.Add(frameBmp);
            }
            
            // Start animation
            currentFrameIndex = 0;
            picOut.Image = animationFrames[0];
            animationTimer.Interval = 1000 / fps; // Convert FPS to milliseconds
            animationTimer.Start();
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static Bitmap MakeShaky(Bitmap src, int strengthPx, int cellsPerAxis, int seed, byte threshold, bool antialias, bool maskOnly)
    {
        // 1) Convertir a escala de grises y crear máscara binaria de negros
        int w = src.Width, h = src.Height;
        float[,] mask = new float[w, h]; // 1 = negro, 0 = blanco

        using (var gray = ToGrayscale(src))
        {
            var data = gray.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                unsafe
                {
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = (byte*)data.Scan0 + y * data.Stride;
                        for (int x = 0; x < w; x++)
                        {
                            byte g = row[x * 3]; // B=G=R en gris
                            mask[x, y] = g < threshold ? 1f : 0f;
                        }
                    }
                }
            }
            finally
            {
                gray.UnlockBits(data);
            }
        }

        // 2) Generar campos de desplazamiento suaves con "value noise" (rápido)
        float[,] dx = new float[w, h];
        float[,] dy = new float[w, h];
        GenerateDisplacementFields(w, h, cellsPerAxis, seed, strengthPx, dx, dy);

        // 3) Remapeo inverso: para cada pixel destino, buscamos desde dónde viene
        //    y muestreamos la máscara (bilineal si se pide), para mantener continuidad.
        var dest = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var dData = dest.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    byte* drow = (byte*)dData.Scan0 + y * dData.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        float sx = x - dx[x, y];
                        float sy = y - dy[x, y];

                        float m = antialias ? SampleBilinear(mask, w, h, sx, sy) : SampleNearest(mask, w, h, sx, sy);

                        if (maskOnly)
                        {
                            // Solo movemos los negros; fondo blanco
                            bool isBlack = m > 0.5f;
                            byte v = isBlack ? (byte)0 : (byte)255;
                            drow[x * 3 + 0] = v;
                            drow[x * 3 + 1] = v;
                            drow[x * 3 + 2] = v;
                        }
                        else
                        {
                            // Desplazar todo y volver a umbralizar con el mismo threshold
                            // (para B/N originales).
                            byte v = m > 0.5f ? (byte)0 : (byte)255;
                            drow[x * 3 + 0] = v;
                            drow[x * 3 + 1] = v;
                            drow[x * 3 + 2] = v;
                        }
                    }
                }
            }
        }
        finally
        {
            dest.UnlockBits(dData);
        }

        return dest;
    }

    private static Bitmap ToGrayscale(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        var gray = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(gray);
        var cm = new System.Drawing.Imaging.ColorMatrix(new float[][]
        {
            new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
            new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
            new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 }
        });
        var ia = new ImageAttributes();
        ia.SetColorMatrix(cm);
        g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia);
        return gray;
    }

    private static void GenerateDisplacementFields(int w, int h, int cellsPerAxis, int seed, int strengthPx, float[,] dx, float[,] dy)
    {
        // "Value noise":
        //  - Creamos una grilla coarse (cellsPerAxis x cellsPerAxis) con valores aleatorios en [-1,1]
        //  - Interpolamos bilinealmente a resolución w x h
        //  - Escalamos por strengthPx para obtener desplazamientos en píxeles

        int gx = Math.Max(1, cellsPerAxis);
        int gy = Math.Max(1, (int)Math.Round((double)cellsPerAxis * h / Math.Max(1, w))); // mantener aspecto

        float[,] gridX = new float[gx + 1, gy + 1];
        float[,] gridY = new float[gx + 1, gy + 1];

        var rngX = new Random(seed);
        var rngY = new Random(unchecked((int)(seed * 73856093 ^ 0x9E3779B9)));

        for (int j = 0; j <= gy; j++)
        {
            for (int i = 0; i <= gx; i++)
            {
                gridX[i, j] = (float)(rngX.NextDouble() * 2.0 - 1.0); // [-1,1]
                gridY[i, j] = (float)(rngY.NextDouble() * 2.0 - 1.0);
            }
        }

        // Precalcular escala entre pixel y celda
        float cellW = (float)w / gx;
        float cellH = (float)h / gy;

        for (int y = 0; y < h; y++)
        {
            float fy = y / cellH;
            int jy = (int)Math.Floor(fy);
            float ty = fy - jy;
            int jy1 = Math.Min(jy + 1, gy);
            jy = Math.Max(0, Math.Min(jy, gy));

            for (int x = 0; x < w; x++)
            {
                float fx = x / cellW;
                int ix = (int)Math.Floor(fx);
                float tx = fx - ix;
                int ix1 = Math.Min(ix + 1, gx);
                ix = Math.Max(0, Math.Min(ix, gx));

                // Suavizado cúbico (fade) tipo Perlin para evitar costuras
                float sx = Smoothstep(tx);
                float sy = Smoothstep(ty);

                float v00x = gridX[ix, jy];
                float v10x = gridX[ix1, jy];
                float v01x = gridX[ix, jy1];
                float v11x = gridX[ix1, jy1];

                float v00y = gridY[ix, jy];
                float v10y = gridY[ix1, jy];
                float v01y = gridY[ix, jy1];
                float v11y = gridY[ix1, jy1];

                float vx0 = Lerp(v00x, v10x, sx);
                float vx1 = Lerp(v01x, v11x, sx);
                float vy0 = Lerp(v00y, v10y, sx);
                float vy1 = Lerp(v01y, v11y, sx);

                float vx = Lerp(vx0, vx1, sy);
                float vy = Lerp(vy0, vy1, sy);

                dx[x, y] = vx * strengthPx;
                dy[x, y] = vy * strengthPx;
            }
        }
    }

    private static float Smoothstep(float t)
    {
        // 3t^2 - 2t^3
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SampleNearest(float[,] img, int w, int h, float x, float y)
    {
        int xi = (int)Math.Round(x);
        int yi = (int)Math.Round(y);
        if (xi < 0 || yi < 0 || xi >= w || yi >= h) return 0f;
        return img[xi, yi];
    }

    private static float SampleBilinear(float[,] img, int w, int h, float x, float y)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        float tx = x - x0;
        float ty = y - y0;

        float c00 = (x0 >= 0 && y0 >= 0 && x0 < w && y0 < h) ? img[x0, y0] : 0f;
        float c10 = (x1 >= 0 && y0 >= 0 && x1 < w && y0 < h) ? img[x1, y0] : 0f;
        float c01 = (x0 >= 0 && y1 >= 0 && x0 < w && y1 < h) ? img[x0, y1] : 0f;
        float c11 = (x1 >= 0 && y1 >= 0 && x1 < w && y1 < h) ? img[x1, y1] : 0f;

        float a = Lerp(c00, c10, tx);
        float b = Lerp(c01, c11, tx);
        return Lerp(a, b, ty);
    }
}
