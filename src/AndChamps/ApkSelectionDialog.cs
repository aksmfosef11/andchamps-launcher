using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace AndChamps;

internal sealed class ApkSelectionDialog : Form
{
    private static readonly string[] SupportedExtensions = [".apk", ".apks", ".apkm", ".xapk"];
    private readonly Label _selection = new();
    private readonly Label _validation = new();
    private readonly Button _install = new();
    private readonly Panel _dropZone = new();
    private string[] _selectedFiles = [];

    public SelectedGamePackage? SelectedPackage { get; private set; }

    public ApkSelectionDialog()
    {
        Text = "게임 패키지 설치";
        ClientSize = new Size(640, 460);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(252, 249, 241);
        ForeColor = Color.FromArgb(31, 42, 68);
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        AllowDrop = true;

        var header = new Panel
        {
            Location = Point.Empty,
            Size = new Size(640, 58),
            BackColor = Color.White
        };
        header.MouseDown += BeginWindowDrag;
        var mark = new Label
        {
            Text = "APK",
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(22, 15),
            Size = new Size(42, 29),
            BackColor = Color.FromArgb(224, 53, 57),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold)
        };
        var headerTitle = TransparentLabel("게임 설치", new Point(77, 18),
            new Font("Segoe UI Semibold", 11F, FontStyle.Bold), Color.FromArgb(31, 42, 68));
        var close = new Button
        {
            Text = "×",
            Location = new Point(590, 8),
            Size = new Size(40, 40),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(86, 91, 104),
            Font = new Font("Segoe UI", 15F),
            TabStop = false,
            Cursor = Cursors.Hand
        };
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 232, 225);
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        header.Controls.AddRange([mark, headerTitle, close]);

        var eyebrow = TransparentLabel("ANDROID READY", new Point(54, 85),
            new Font("Segoe UI Semibold", 8F, FontStyle.Bold), Color.FromArgb(62, 101, 190));
        var title = TransparentLabel("게임 파일을 선택해 주세요", new Point(50, 106),
            new Font("Segoe UI Semibold", 23F, FontStyle.Bold), Color.FromArgb(31, 42, 68));
        var subtitle = TransparentLabel("Android 설정이 끝났습니다. 파일을 선택하면 설치 후 바로 실행합니다.",
            new Point(54, 154), new Font("Segoe UI", 9.5F), Color.FromArgb(100, 108, 126));

        _dropZone.Location = new Point(54, 195);
        _dropZone.Size = new Size(532, 145);
        _dropZone.BackColor = Color.FromArgb(248, 250, 255);
        _dropZone.AllowDrop = true;
        _dropZone.Paint += PaintDropZone;
        _dropZone.DragEnter += HandleDragEnter;
        _dropZone.DragDrop += HandleDragDrop;
        _dropZone.Cursor = Cursors.Hand;
        _dropZone.Click += (_, _) => BrowseFiles();

        var uploadIcon = TransparentLabel("↑", new Point(245, 16),
            new Font("Segoe UI Semibold", 24F, FontStyle.Bold), Color.FromArgb(224, 53, 57));
        uploadIcon.AutoSize = false;
        uploadIcon.Size = new Size(42, 40);
        uploadIcon.TextAlign = ContentAlignment.MiddleCenter;
        uploadIcon.Click += (_, _) => BrowseFiles();
        var dropTitle = TransparentLabel("여기에 파일을 놓거나 클릭해서 선택", new Point(118, 58),
            new Font("Segoe UI Semibold", 10F, FontStyle.Bold), Color.FromArgb(39, 52, 79));
        dropTitle.Click += (_, _) => BrowseFiles();
        var formats = TransparentLabel("APK · APKS · APKM · XAPK · 여러 split APK", new Point(133, 91),
            new Font("Segoe UI", 8.5F), Color.FromArgb(106, 116, 139));
        formats.Click += (_, _) => BrowseFiles();
        _dropZone.Controls.AddRange([uploadIcon, dropTitle, formats]);

        _selection.Text = "선택된 파일 없음";
        _selection.Location = new Point(56, 359);
        _selection.Size = new Size(390, 22);
        _selection.AutoEllipsis = true;
        _selection.BackColor = Color.Transparent;
        _selection.ForeColor = Color.FromArgb(59, 67, 83);

        _validation.Text = "파일은 이 PC에서 바로 설치되며 별도로 복사하지 않습니다.";
        _validation.Location = new Point(56, 385);
        _validation.Size = new Size(420, 22);
        _validation.AutoEllipsis = true;
        _validation.BackColor = Color.Transparent;
        _validation.ForeColor = Color.FromArgb(117, 120, 127);
        _validation.Font = new Font("Segoe UI", 8.3F);

        _install.Text = "설치 시작";
        _install.Location = new Point(474, 365);
        _install.Size = new Size(112, 42);
        _install.Enabled = false;
        _install.FlatStyle = FlatStyle.Flat;
        _install.FlatAppearance.BorderSize = 0;
        _install.BackColor = Color.FromArgb(224, 53, 57);
        _install.ForeColor = Color.White;
        _install.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _install.Cursor = Cursors.Hand;
        _install.Click += async (_, _) => await ConfirmSelectionAsync();

        Controls.AddRange([header, eyebrow, title, subtitle, _dropZone, _selection, _validation, _install]);
        DragEnter += HandleDragEnter;
        DragDrop += HandleDragDrop;
        Resize += (_, _) => ApplyRoundedRegion();
        ApplyRoundedRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(45, 246, 195, 54));
        e.Graphics.FillEllipse(glow, 455, -90, 270, 250);
        using var border = new Pen(Color.FromArgb(220, 214, 202));
        using var path = LauncherForm.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 14);
        e.Graphics.DrawPath(border, path);
    }

    private void BrowseFiles()
    {
        using var picker = new OpenFileDialog
        {
            Title = "게임 APK 선택",
            Filter = "Android 게임 패키지|*.apk;*.apks;*.apkm;*.xapk|모든 파일|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) == DialogResult.OK)
            SetSelection(picker.FileNames);
    }

    private void SetSelection(IEnumerable<string> files)
    {
        var selected = files.Where(File.Exists).Select(Path.GetFullPath).Distinct().ToArray();
        if (selected.Length == 0)
            return;
        if (selected.Length > 1 && selected.Any(path => !Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "여러 파일을 고를 때는 split APK 파일만 선택해 주세요.", "파일 형식 확인",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (selected.Any(path => !SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "APK, APKS, APKM 또는 XAPK 파일을 선택해 주세요.", "지원하지 않는 파일",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _selectedFiles = selected;
        var bytes = selected.Sum(path => new FileInfo(path).Length);
        _selection.Text = selected.Length == 1
            ? $"{Path.GetFileName(selected[0])}  ·  {FormatBytes(bytes)}"
            : $"split APK {selected.Length}개  ·  {FormatBytes(bytes)}";
        _validation.Text = selected.Length == 1
            ? ApkInspector.Inspect(selected[0]).Detail
            : "선택한 split APK를 하나의 설치 묶음으로 준비합니다.";
        _validation.ForeColor = Color.FromArgb(62, 101, 190);
        _install.Enabled = true;
        _dropZone.Invalidate();
    }

    private async Task ConfirmSelectionAsync()
    {
        if (_selectedFiles.Length == 0)
            return;
        _install.Enabled = false;
        _install.Text = "준비 중…";
        try
        {
            if (_selectedFiles.Length == 1)
            {
                SelectedPackage = new SelectedGamePackage(_selectedFiles[0]);
            }
            else
            {
                _validation.Text = "split APK 설치 묶음을 만들고 있습니다…";
                var bundle = await Task.Run(() => CreateSplitBundle(_selectedFiles));
                SelectedPackage = new SelectedGamePackage(bundle, DeleteAfterInstall: true);
            }
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _validation.Text = ex.Message;
            _validation.ForeColor = Color.FromArgb(255, 116, 128);
            _install.Enabled = true;
            _install.Text = "설치 시작";
        }
    }

    private static string CreateSplitBundle(IEnumerable<string> files)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AndChamps", "SelectedPackages");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"selected-{Guid.NewGuid():N}.apks");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
            archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.NoCompression);
        return path;
    }

    private void HandleDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            SetSelection(files);
    }

    private void PaintDropZone(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = LauncherForm.RoundedRectangle(new Rectangle(1, 1, _dropZone.Width - 3, _dropZone.Height - 3), 14);
        using var pen = new Pen(_selectedFiles.Length == 0
            ? Color.FromArgb(120, 87, 117, 177)
            : Color.FromArgb(210, 224, 53, 57), 1.5F)
        {
            DashStyle = DashStyle.Dash
        };
        e.Graphics.DrawPath(pen, path);
    }

    private static Label TransparentLabel(string text, Point location, Font font, Color color) => new()
    {
        Text = text,
        Location = location,
        AutoSize = true,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent
    };

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024d / 1024d:N2} GB"
        : $"{bytes / 1024d / 1024d:N0} MB";

    private void ApplyRoundedRegion()
    {
        using var path = LauncherForm.RoundedRectangle(ClientRectangle, 14);
        Region?.Dispose();
        Region = new Region(path);
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (nint)2, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
