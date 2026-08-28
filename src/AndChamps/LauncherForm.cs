using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AndChamps;

internal sealed class LauncherForm : Form
{
    private readonly Label _status = new();
    private readonly Label _detail = new();
    private readonly Label _percent = new();
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _hint = new();
    private readonly StepStrip _steps = new();
    private readonly ActivityOrb _activity = new();
    private readonly ModernProgressBar _progress = new();
    private readonly Button _primary = new();
    private readonly Button _clearData = new();
    private readonly Button _removeAll = new();
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 16 };
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _operation;
    private Task? _activeTask;
    private int _dotFrame;

    public LauncherForm()
    {
        Text = "포챔스에뮬레이터";
        ClientSize = new Size(800, 550);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(250, 247, 239);
        ForeColor = Color.FromArgb(31, 42, 68);
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;

        var dragBar = new Panel { Location = Point.Empty, Size = new Size(800, 60), BackColor = Color.Transparent };
        dragBar.MouseDown += BeginWindowDrag;

        var mark = new Label
        {
            Text = "PC",
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(24, 15),
            Size = new Size(40, 32),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            BackColor = Color.FromArgb(224, 53, 57),
            ForeColor = Color.White
        };
        var appName = new Label
        {
            Text = "포챔스에뮬레이터",
            AutoSize = true,
            Location = new Point(76, 20),
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(31, 42, 68)
        };
        var preview = new Label
        {
            Text = $"v{typeof(LauncherForm).Assembly.GetName().Version?.ToString(3)} · GAME RUNTIME",
            AutoSize = true,
            Location = new Point(218, 23),
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(62, 101, 190)
        };

        var minimize = MakeWindowButton("—", 710);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var close = MakeWindowButton("×", 756);
        close.Font = new Font("Segoe UI", 15F);
        close.Click += (_, _) => Close();
        dragBar.Controls.AddRange([mark, appName, preview, minimize, close]);

        var eyebrow = new Label
        {
            Text = "UNOFFICIAL COMMUNITY LAUNCHER",
            AutoSize = true,
            Location = new Point(64, 94),
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(62, 101, 190)
        };
        _title.Text = "Android 게임을 PC에서";
        _title.AutoSize = true;
        _title.Location = new Point(60, 119);
        _title.Font = new Font("Segoe UI Semibold", 26F, FontStyle.Bold);
        _title.BackColor = Color.Transparent;
        _title.ForeColor = Color.FromArgb(31, 42, 68);
        _subtitle.Text = "설치부터 실행, 데이터 관리까지 간단하게 준비했어요.";
        _subtitle.AutoSize = true;
        _subtitle.Location = new Point(64, 175);
        _subtitle.Font = new Font("Segoe UI", 10F);
        _subtitle.BackColor = Color.Transparent;
        _subtitle.ForeColor = Color.FromArgb(98, 108, 128);

        _steps.Location = new Point(64, 211);
        _steps.Size = new Size(672, 48);

        _activity.Location = new Point(68, 310);
        _activity.Size = new Size(58, 58);

        _status.Text = "전용 Android 런타임을 확인하고 있습니다";
        _status.AutoEllipsis = true;
        _status.Location = new Point(149, 307);
        _status.Size = new Size(510, 27);
        _status.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
        _status.BackColor = Color.Transparent;
        _status.ForeColor = Color.FromArgb(31, 42, 68);

        _detail.Text = "작업이 계속 진행 중입니다";
        _detail.AutoEllipsis = true;
        _detail.Location = new Point(149, 340);
        _detail.Size = new Size(520, 23);
        _detail.BackColor = Color.Transparent;
        _detail.ForeColor = Color.FromArgb(105, 113, 132);

        _percent.Text = "0%";
        _percent.TextAlign = ContentAlignment.MiddleRight;
        _percent.Location = new Point(681, 310);
        _percent.Size = new Size(58, 24);
        _percent.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _percent.BackColor = Color.Transparent;
        _percent.ForeColor = Color.FromArgb(214, 49, 54);

        _progress.Location = new Point(64, 383);
        _progress.Size = new Size(672, 8);

        _hint.Text = "작업은 버튼을 눌렀을 때만 시작됩니다.";
        _hint.AutoSize = true;
        _hint.Location = new Point(64, 500);
        _hint.Font = new Font("Segoe UI", 8.5F);
        _hint.BackColor = Color.Transparent;
        _hint.ForeColor = Color.FromArgb(119, 121, 126);

        ConfigureActionButton(_primary, "설치", new Point(596, 432), primary: true);
        ConfigureActionButton(_clearData, "데이터 제거", new Point(444, 432));
        ConfigureActionButton(_removeAll, "전체 제거", new Point(292, 432), danger: true);
        _primary.Click += (_, _) => BeginPrimaryAction();
        _clearData.Click += async (_, _) => await ClearDataAsync();
        _removeAll.Click += async (_, _) => await RemoveAllAsync();

        Controls.AddRange([dragBar,
            eyebrow,
            _title,
            _subtitle,
            _steps,
            _activity,
            _status,
            _detail,
            _percent,
            _progress,
            _hint,
            _removeAll,
            _clearData,
            _primary]);

        Shown += (_, _) => ShowHome();
        FormClosing += (_, _) => _shutdown.Cancel();
        Resize += (_, _) => ApplyRoundedRegion();
        _animation.Tick += (_, _) => AnimateUi();
        _animation.Start();
        ApplyRoundedRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(255, 252, 245), Color.FromArgb(241, 246, 255), 22F);
        e.Graphics.FillRectangle(background, ClientRectangle);

        using var yellowGlow = new SolidBrush(Color.FromArgb(48, 249, 205, 67));
        e.Graphics.FillEllipse(yellowGlow, 610, 58, 210, 175);
        using var redGlow = new SolidBrush(Color.FromArgb(22, 224, 53, 57));
        e.Graphics.FillEllipse(redGlow, -95, 380, 250, 210);

        using var shadow = new SolidBrush(Color.FromArgb(18, 38, 50, 78));
        using var shadowPath = RoundedRectangle(new Rectangle(43, 285, 718, 132), 18);
        e.Graphics.FillPath(shadow, shadowPath);
        using var card = new SolidBrush(Color.FromArgb(252, 255, 255, 255));
        using var cardPath = RoundedRectangle(new Rectangle(38, 280, 724, 132), 18);
        e.Graphics.FillPath(card, cardPath);
        using var border = new Pen(Color.FromArgb(222, 218, 208));
        e.Graphics.DrawPath(border, cardPath);
        using var headerLine = new Pen(Color.FromArgb(224, 219, 207));
        e.Graphics.DrawLine(headerLine, 22, 59, 778, 59);
        using var redAccent = new Pen(Color.FromArgb(224, 53, 57), 3F);
        e.Graphics.DrawLine(redAccent, 24, 58, 142, 58);
    }

    private void ShowHome(string? status = null, string? detail = null)
    {
        var paths = new AppPaths();
        var ready = new RuntimeProvisioner(paths).IsReady;
        var hasRuntimeFiles = Directory.Exists(paths.Sdk)
            || Directory.Exists(paths.AvdHome)
            || Directory.Exists(paths.Downloads);

        _steps.Visible = false;
        _progress.IsIndeterminate = false;
        _progress.Value = ready ? 1 : 0;
        _percent.Text = ready ? "준비" : "NEW";
        _activity.State = ActivityState.Working;
        _status.Text = status ?? (ready ? "게임을 실행할 준비가 됐습니다" : "Android 런타임 설치가 필요합니다");
        _detail.Text = detail ?? (ready
            ? "원하는 작업을 선택해 주세요 · 자동으로 실행하지 않습니다"
            : "설치 버튼을 누르면 필요한 공식 Android 구성 요소를 다운로드합니다");
        _hint.Text = ready
            ? "데이터 제거는 게임을 유지하고 계정·설정·저장 데이터만 초기화합니다."
            : "설치를 누르기 전에는 다운로드하거나 변경하지 않습니다.";

        _primary.Text = ready ? "실행" : "설치";
        _primary.Visible = true;
        _primary.Enabled = true;
        _clearData.Visible = ready;
        _clearData.Enabled = true;
        _removeAll.Visible = ready || hasRuntimeFiles;
        _removeAll.Enabled = true;
    }

    private async void BeginPrimaryAction()
    {
        if (_activeTask is { IsCompleted: false })
            return;
        var ready = new RuntimeProvisioner(new AppPaths()).IsReady;
        _activeTask = ready ? RunGameAsync() : InstallRuntimeAsync();
        await _activeTask;
    }

    private async Task InstallRuntimeAsync()
    {
        using var operation = BeginOperation("Android 런타임을 설치하고 있습니다",
            "다운로드와 압축 해제 진행률을 표시합니다");
        var progress = CreateProgress();
        try
        {
            var paths = new AppPaths();
            var runtime = new RuntimeProvisioner(paths);
            if (!runtime.IsReady)
                LicenseConsent.EnsureAccepted(paths);
            await runtime.EnsureAsync(progress, operation.Token);
            ShowHome("설치를 완료했습니다", "실행 버튼을 누르면 Android를 시작하고 게임을 엽니다");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ShowError("설치를 완료하지 못했습니다", ex.Message);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task RunGameAsync()
    {
        using var operation = BeginOperation("게임 실행을 준비하고 있습니다",
            "Android 부팅 후 게임 화면으로 전환합니다");
        var progress = CreateProgress();
        try
        {
            var coordinator = new LaunchCoordinator(new AppPaths());
            var options = new LaunchOptions();
            using var session = await coordinator.RunAsync(progress, SelectGamePackageAsync, options, operation.Token);
            _steps.ActiveIndex = 4;
            _progress.IsIndeterminate = false;
            _progress.Value = 1;
            _percent.Text = "100%";
            Hide();
            await session.WaitForExitAsync(operation.Token, () =>
            {
                if (_shutdown.IsCancellationRequested)
                    return;

                Show();
                Activate();
                _status.Text = "게임을 종료하고 있습니다";
                _detail.Text = "Android를 안전하게 종료하는 중입니다 · 잠시만 기다려 주세요";
                _activity.State = ActivityState.Working;
                _progress.IsIndeterminate = true;
                _percent.Text = "·";
            });
            if (!_shutdown.IsCancellationRequested)
            {
                Show();
                Activate();
                ShowHome("게임을 종료했습니다", "다시 실행하거나 데이터를 관리할 수 있습니다");
            }
        }
        catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
        {
            ShowHome("실행을 취소했습니다", "APK를 선택하지 않았거나 실행 작업을 취소했습니다");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (VirtualizationUnavailableException ex)
        {
            ShowError("가상화 설정이 필요합니다", ex.Message);
        }
        catch (Exception ex)
        {
            ShowError("게임을 실행하지 못했습니다", ex.Message);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task ClearDataAsync()
    {
        var answer = MessageBox.Show(this,
            "게임 앱은 유지하지만 로그인 정보, 설정과 저장 데이터를 모두 초기화합니다.\n계속할까요?",
            "게임 데이터 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        using var operation = BeginOperation("게임 데이터를 제거하고 있습니다",
            "게임 앱과 Android 런타임은 유지합니다");
        try
        {
            var cleared = await new LaunchCoordinator(new AppPaths())
                .ClearGameDataAsync(CreateProgress(), operation.Token);
            ShowHome(cleared ? "게임 데이터를 제거했습니다" : "초기화할 게임 데이터가 없습니다",
                cleared ? "게임 앱은 그대로 유지했습니다" : "게임 패키지가 아직 설치되지 않았습니다");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (VirtualizationUnavailableException ex)
        {
            ShowError("가상화 설정이 필요합니다", ex.Message);
        }
        catch (Exception ex)
        {
            ShowError("게임 데이터를 제거하지 못했습니다", ex.Message);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task RemoveAllAsync()
    {
        var answer = MessageBox.Show(this,
            "게임, 로그인·저장 데이터, Android 에뮬레이터와 다운로드 파일을 모두 제거합니다.\n다음 사용 시 전체 설치가 다시 필요합니다.",
            "포챔스에뮬레이터 전체 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        using var operation = BeginOperation("Android 런타임을 전체 제거하고 있습니다",
            "게임과 에뮬레이터 데이터를 정리합니다");
        try
        {
            var paths = new AppPaths();
            await new RuntimeProvisioner(paths).RemoveAllAsync(CreateProgress(), operation.Token);
            ShowHome("전체 제거를 완료했습니다", "다시 사용하려면 설치 버튼을 눌러 주세요");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ShowError("전체 제거를 완료하지 못했습니다", ex.Message);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private CancellationTokenSource BeginOperation(string status, string detail)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _operation = operation;
        _primary.Enabled = false;
        _clearData.Enabled = false;
        _removeAll.Enabled = false;
        _steps.Visible = true;
        _steps.ActiveIndex = 0;
        _status.Text = status;
        _detail.Text = detail;
        _activity.State = ActivityState.Working;
        _progress.IsIndeterminate = true;
        _progress.Value = 0;
        _percent.Text = "·";
        return operation;
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(_operation, operation))
            _operation = null;
    }

    private Progress<ProgressUpdate> CreateProgress() => new(update =>
    {
        _status.Text = update.Message.TrimEnd('…');
        _steps.ActiveIndex = DetermineStage(update.Message);
        if (update.Fraction is { } fraction)
        {
            var value = Math.Clamp(fraction, 0, 1);
            _progress.IsIndeterminate = false;
            _progress.Value = value;
            _percent.Text = $"{value:P0}";
            _detail.Text = update.Message.Contains("압축", StringComparison.Ordinal)
                ? "파일을 안전하게 구성하고 있습니다 · 창을 닫지 마세요"
                : "실제 작업 진행률을 표시하고 있습니다";
        }
        else
        {
            _progress.IsIndeterminate = true;
            _detail.Text = "작업이 계속 진행 중입니다";
        }
    });

    private void ShowError(string status, string detail)
    {
        var ready = new RuntimeProvisioner(new AppPaths()).IsReady;
        _status.Text = status;
        _detail.Text = detail.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? detail;
        _activity.State = ActivityState.Error;
        _progress.IsIndeterminate = false;
        _primary.Text = ready ? "실행" : "설치";
        _primary.Enabled = true;
        _primary.Visible = true;
        _clearData.Visible = ready;
        _clearData.Enabled = true;
        _removeAll.Visible = true;
        _removeAll.Enabled = true;

        if (detail.Contains('\n'))
            MessageBox.Show(this, detail, status, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private Task<SelectedGamePackage?> SelectGamePackageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new ApkSelectionDialog();
        var result = dialog.ShowDialog(this);
        return Task.FromResult(result == DialogResult.OK ? dialog.SelectedPackage : null);
    }

    private void AnimateUi()
    {
        _dotFrame = (_dotFrame + 1) % 90;
        _activity.Advance();
        _progress.Advance();
        if (_progress.IsIndeterminate)
        {
            var dots = new string('·', 1 + _dotFrame / 30);
            _percent.Text = dots;
        }
    }

    private static int DetermineStage(string message)
    {
        if (message.Contains("실행", StringComparison.Ordinal)
            || message.Contains("게임 패키지", StringComparison.Ordinal)
            || message.Contains("설치", StringComparison.Ordinal)
            || message.Contains("APK", StringComparison.Ordinal))
            return 4;
        if (message.Contains("부팅", StringComparison.Ordinal)
            || message.Contains("Android를 시작", StringComparison.Ordinal))
            return 3;
        if (message.Contains("압축", StringComparison.Ordinal))
            return 2;
        if (message.Contains("다운로드", StringComparison.Ordinal))
            return 1;
        return 0;
    }

    private static void ConfigureActionButton(Button button, string text, Point location,
        bool primary = false, bool danger = false)
    {
        button.Text = text;
        button.Size = new Size(140, 38);
        button.Location = location;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = primary
            ? Color.FromArgb(196, 38, 43)
            : danger ? Color.FromArgb(196, 94, 98) : Color.FromArgb(67, 103, 178);
        button.FlatAppearance.MouseOverBackColor = danger
            ? Color.FromArgb(255, 235, 235)
            : primary ? Color.FromArgb(196, 38, 43) : Color.FromArgb(235, 241, 253);
        button.BackColor = primary ? Color.FromArgb(224, 53, 57) : Color.White;
        button.ForeColor = danger
            ? Color.FromArgb(176, 52, 57)
            : primary ? Color.White : Color.FromArgb(47, 78, 145);
        button.Cursor = Cursors.Hand;
    }

    private static Button MakeWindowButton(string text, int x) => new()
    {
        Text = text,
        Location = new Point(x, 10),
        Size = new Size(42, 36),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0, MouseOverBackColor = Color.FromArgb(238, 232, 220) },
        BackColor = Color.Transparent,
        ForeColor = Color.FromArgb(72, 78, 91),
        Font = new Font("Segoe UI", 11F),
        TabStop = false,
        UseVisualStyleBackColor = false,
        Cursor = Cursors.Hand
    };

    private void ApplyRoundedRegion()
    {
        using var path = RoundedRectangle(ClientRectangle, 14);
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

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}

internal enum ActivityState
{
    Working,
    Error
}

internal sealed class ActivityOrb : Control
{
    private float _angle;
    public ActivityState State { get; set; }

    public ActivityOrb()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    public void Advance()
    {
        _angle = (_angle + 3.2F) % 360;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(7, 7, Width - 14, Height - 14);
        using var track = new Pen(Color.FromArgb(225, 219, 207), 5F);
        e.Graphics.DrawEllipse(track, bounds);
        var color = State == ActivityState.Error
            ? Color.FromArgb(190, 47, 52)
            : Color.FromArgb(224, 53, 57);
        using var arc = new Pen(color, 5F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(arc, bounds, _angle, 112);
        using var center = new SolidBrush(State == ActivityState.Error
            ? Color.FromArgb(210, color)
            : Color.FromArgb(246, 195, 54));
        e.Graphics.FillEllipse(center, Width / 2F - 4, Height / 2F - 4, 8, 8);
    }
}

internal sealed class ModernProgressBar : Control
{
    private double _value;
    private float _phase;
    public bool IsIndeterminate { get; set; }

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    public ModernProgressBar()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    public void Advance()
    {
        _phase = (_phase + 0.012F) % 1F;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var trackPath = LauncherForm.RoundedRectangle(bounds, Height / 2);
        using var track = new SolidBrush(Color.FromArgb(229, 225, 216));
        e.Graphics.FillPath(track, trackPath);

        Rectangle fill;
        if (IsIndeterminate)
        {
            var width = Math.Max(72, Width / 5);
            var x = (int)((Width + width) * _phase) - width;
            fill = new Rectangle(x, 0, width, Height - 1);
        }
        else
        {
            fill = new Rectangle(0, 0, Math.Max(1, (int)(Width * Value)), Height - 1);
        }
        if (fill.Width <= 0)
            return;
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SetClip(trackPath);
            using var gradient = new LinearGradientBrush(fill,
                Color.FromArgb(224, 53, 57), Color.FromArgb(246, 195, 54), 0F);
            e.Graphics.FillRectangle(gradient, fill);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }
}

internal sealed class StepStrip : Control
{
    private static readonly string[] Labels = ["확인", "다운로드", "구성", "부팅", "실행"];
    private int _activeIndex;

    public int ActiveIndex
    {
        get => _activeIndex;
        set
        {
            _activeIndex = Math.Clamp(value, 0, Labels.Length - 1);
            Invalidate();
        }
    }

    public StepStrip()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var labelFont = new Font("Segoe UI", 8.3F, FontStyle.Bold);
        var gap = (Width - 26) / (Labels.Length - 1F);
        var lineY = 13F;
        using var pendingLine = new Pen(Color.FromArgb(218, 214, 204), 2F);
        e.Graphics.DrawLine(pendingLine, 13, lineY, Width - 13, lineY);
        if (ActiveIndex > 0)
        {
            using var activeLine = new Pen(Color.FromArgb(224, 53, 57), 2F);
            e.Graphics.DrawLine(activeLine, 13, lineY, 13 + gap * ActiveIndex, lineY);
        }

        for (var index = 0; index < Labels.Length; index++)
        {
            var x = 13 + gap * index;
            var completed = index < ActiveIndex;
            var active = index == ActiveIndex;
            var color = completed || active
                ? Color.FromArgb(224, 53, 57)
                : Color.FromArgb(212, 208, 199);
            using var dot = new SolidBrush(color);
            e.Graphics.FillEllipse(dot, x - 8, lineY - 8, 16, 16);
            if (completed)
            {
                using var check = new Pen(Color.White, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                e.Graphics.DrawLines(check, [new PointF(x - 4, lineY), new PointF(x - 1, lineY + 3), new PointF(x + 5, lineY - 4)]);
            }
            else if (active)
            {
                using var center = new SolidBrush(Color.White);
                e.Graphics.FillEllipse(center, x - 2.5F, lineY - 2.5F, 5, 5);
            }

            var size = e.Graphics.MeasureString(Labels[index], labelFont);
            using var text = new SolidBrush(active
                ? Color.FromArgb(36, 47, 70)
                : Color.FromArgb(124, 127, 134));
            e.Graphics.DrawString(Labels[index], labelFont, text, x - size.Width / 2, 29);
        }
    }
}
