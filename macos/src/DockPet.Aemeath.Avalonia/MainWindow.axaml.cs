using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DockPet.Core;
using System;

namespace DockPet.Aemeath.Avalonia;

public partial class MainWindow : Window
{
    private readonly PetShellViewModel viewModel;

    public MainWindow()
    {
        viewModel = PetShellViewModel.Create();
        DataContext = viewModel;
        InitializeComponent();
        Opened += (_, _) => MoveNearScreenCorner();
    }

    private void MoveNearScreenCorner()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height));
        Position = new PixelPoint(
            area.X + area.Width - width - 32,
            area.Y + area.Height - height - 32);
    }

    private void OnShellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnChatClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.SetStatus("聊天窗口下一阶段接入");
    }

    private void OnHomeClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.SetStatus("小屋窗口下一阶段接入");
    }

    private void OnSettingsClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.SetStatus("API 设置页下一阶段接入");
    }

    private void OnRestartClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.SetStatus("重启流程下一阶段接入");
    }

    private void OnExitClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}

public sealed class PetShellViewModel : global::Avalonia.AvaloniaObject
{
    public string BubbleTitle { get; private init; } = "";
    public string BubbleText { get; private init; } = "";

    public static readonly StyledProperty<string> StatusLineProperty =
        AvaloniaProperty.Register<PetShellViewModel, string>(nameof(StatusLine), "正在等待 API 设置");

    public string StatusLine
    {
        get => GetValue(StatusLineProperty);
        set => SetValue(StatusLineProperty, value);
    }

    public static PetShellViewModel Create()
    {
        var settings = AemeathDefaults.CreateDefaultSettings();
        settings.Normalize();

        return new PetShellViewModel
        {
            BubbleTitle = $"{settings.PetName}想和你说",
            BubbleText = FirstRunGuide.ApiMissingBubble(settings.UserSalutation),
            StatusLine = $"{settings.PetName} / {settings.UserSalutation}"
        };
    }

    public void SetStatus(string status)
    {
        StatusLine = status;
    }
}
