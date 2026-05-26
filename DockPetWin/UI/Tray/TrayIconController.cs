using System.IO;
using Forms = System.Windows.Forms;

namespace DockPetWin.UI.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private System.Drawing.Icon? currentIcon;

    public TrayIconController(string? iconImagePath = null)
    {
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = CreateIcon(iconImagePath) ?? System.Drawing.SystemIcons.Application,
            Text = "爱弥斯",
            Visible = true,
            ContextMenuStrip = CreateMenu()
        };
    }

    public event Action? PetRequested;
    public event Action? ToggleStateRequested;
    public event Action? AgentChatRequested;
    public event Action? HomeRequested;
    public event Action? ClearCodexNotificationsRequested;
    public event Action? SettingsRequested;
    public event Action? ToggleVisibilityRequested;
    public event Action? RestartRequested;
    public event Action? ExitRequested;

    public void Update(
        bool isVisible,
        bool isWalking,
        string? statusText = null,
        string? remainingText = null)
    {
        var menu = notifyIcon.ContextMenuStrip!;
        menu.Items.Clear();
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            AddMenuItem(menu, $"当前状态：{statusText}", null, enabled: false);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Forms.ToolStripSeparator());
        }

        AddMenuItem(menu, "摸摸她", () => PetRequested?.Invoke());
        AddMenuItem(menu, isWalking ? "让她休息一下" : "让她散步", () => ToggleStateRequested?.Invoke());
        AddMenuItem(menu, "回到小屋", () => HomeRequested?.Invoke());

        menu.Items.Add(new Forms.ToolStripSeparator());
        AddMenuItem(menu, "和爱弥斯聊天", () => AgentChatRequested?.Invoke());
        AddMenuItem(menu, "清除 Codex 提醒", () => ClearCodexNotificationsRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddMenuItem(menu, "偏好设置", () => SettingsRequested?.Invoke());
        AddMenuItem(menu, isVisible ? "暂时隐藏爱弥斯" : "显示爱弥斯", () => ToggleVisibilityRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddMenuItem(menu, "重启应用", () => RestartRequested?.Invoke());
        AddMenuItem(menu, "退出应用", () => ExitRequested?.Invoke());
    }

    public void ShowNotification(string title, string message)
    {
        notifyIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "Codex" : title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        currentIcon?.Dispose();
    }

    private static Forms.ContextMenuStrip CreateMenu()
    {
        return new Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(255, 248, 252),
            ForeColor = System.Drawing.Color.FromArgb(48, 42, 54),
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F),
            Padding = new Forms.Padding(6),
            ShowImageMargin = false,
            Renderer = new SoftMenuRenderer()
        };
    }

    private static Forms.ToolStripMenuItem AddMenuItem(
        Forms.ContextMenuStrip menu,
        string text,
        Action? action,
        bool enabled = true)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Enabled = enabled,
            Padding = new Forms.Padding(10, 4, 14, 4)
        };
        if (action is not null)
        {
            item.Click += (_, _) => action();
        }

        menu.Items.Add(item);
        return item;
    }

    private System.Drawing.Icon? CreateIcon(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            using var bitmap = new System.Drawing.Bitmap(imagePath!);
            using var square = new System.Drawing.Bitmap(32, 32);
            using (var graphics = System.Drawing.Graphics.FromImage(square))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var scale = Math.Min(32.0 / bitmap.Width, 32.0 / bitmap.Height);
                var width = (int)(bitmap.Width * scale);
                var height = (int)(bitmap.Height * scale);
                graphics.DrawImage(bitmap, (32 - width) / 2, 32 - height, width, height);
            }

            currentIcon = System.Drawing.Icon.FromHandle(square.GetHicon());
            return currentIcon;
        }
        catch
        {
            return null;
        }
    }

    private sealed class SoftMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public SoftMenuRenderer()
            : base(new SoftMenuColorTable())
        {
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 230, 242));
                var rect = new System.Drawing.Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                e.Graphics.FillRectangle(brush, rect);
                return;
            }

            base.OnRenderMenuItemBackground(e);
        }
    }

    private sealed class SoftMenuColorTable : Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(232, 185, 207);
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(255, 248, 252);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(255, 248, 252);
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(255, 248, 252);
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(255, 248, 252);
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(255, 230, 242);
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(232, 185, 207);
    }
}
