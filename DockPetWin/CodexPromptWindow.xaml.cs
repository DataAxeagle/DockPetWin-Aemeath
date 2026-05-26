using System.Windows;

namespace DockPetWin;

public partial class CodexPromptWindow : Window
{
    public CodexPromptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PromptBox.Focus();
    }

    public string Message => PromptBox.Text.Trim();

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Message))
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
