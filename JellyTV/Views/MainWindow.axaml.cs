using Avalonia.Controls;

namespace JellyTV.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Wire up scroll buttons
        var scrollLeftButton = this.FindControl<Button>("ScrollLeftButton");
        var scrollRightButton = this.FindControl<Button>("ScrollRightButton");
        var scrollViewer = this.FindControl<ScrollViewer>("LibraryScrollViewer");

        if (scrollLeftButton != null && scrollViewer != null)
        {
            scrollLeftButton.Click += (s, e) =>
            {
                scrollViewer.Offset = scrollViewer.Offset.WithX(scrollViewer.Offset.X - 300);
            };
        }

        if (scrollRightButton != null && scrollViewer != null)
        {
            scrollRightButton.Click += (s, e) =>
            {
                scrollViewer.Offset = scrollViewer.Offset.WithX(scrollViewer.Offset.X + 300);
            };
        }
    }
}