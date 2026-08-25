using System.Diagnostics;

namespace AndChamps;

internal static class LicenseConsent
{
    private const string LicenseUrl = "https://developer.android.com/studio/terms";

    public static void EnsureAccepted(AppPaths paths)
    {
        var marker = Path.Combine(paths.Root, "android-sdk-license.accepted");
        if (File.Exists(marker))
            return;

        using var dialog = new Form
        {
            Text = "포챔스에뮬레이터 첫 설치",
            ClientSize = new Size(520, 245),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(252, 249, 241),
            ForeColor = Color.FromArgb(31, 42, 68),
            Font = new Font("Segoe UI", 10F)
        };
        var text = new Label
        {
            Text = "포챔스에뮬레이터는 설치 버튼을 누르면 필요한 Android Emulator와 시스템 이미지를 Google 공식 저장소에서 다운로드합니다. 계속하려면 Android SDK 라이선스를 확인하고 동의해야 합니다.",
            Location = new Point(28, 28),
            Size = new Size(462, 76)
        };
        var link = new LinkLabel
        {
            Text = "Android SDK 라이선스 보기",
            Location = new Point(28, 113),
            AutoSize = true,
            LinkColor = Color.FromArgb(62, 101, 190),
            ActiveLinkColor = Color.FromArgb(224, 53, 57)
        };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(LicenseUrl) { UseShellExecute = true });
        var accept = new Button
        {
            Text = "동의하고 계속",
            DialogResult = DialogResult.OK,
            Location = new Point(337, 174),
            Size = new Size(153, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(224, 53, 57),
            ForeColor = Color.White
        };
        var cancel = new Button
        {
            Text = "취소",
            DialogResult = DialogResult.Cancel,
            Location = new Point(237, 174),
            Size = new Size(88, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(65, 72, 88)
        };
        dialog.Controls.AddRange([text, link, accept, cancel]);
        dialog.AcceptButton = accept;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog() != DialogResult.OK)
            throw new OperationCanceledException("Android SDK 라이선스에 동의하지 않아 설치를 취소했습니다.");
        paths.EnsureDirectories();
        File.WriteAllText(marker, $"accepted={DateTimeOffset.UtcNow:O}\nurl={LicenseUrl}\n");
    }
}
