using System.Windows;

namespace TraderVI.WPF;

/// <summary>Recorded-file entry point: no operational dashboard, monitor, SQL or market client.</summary>
public partial class PracticeWindow : Window
{
    public PracticeWindow(string path)
    {
        InitializeComponent();
        Loaded += async (_, _) => await Playback.OpenPlaybackAsync(path);
    }
}
