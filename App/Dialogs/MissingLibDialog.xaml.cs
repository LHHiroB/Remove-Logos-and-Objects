using Microsoft.UI.Xaml;
using System;
using Windows.System;
using Microsoft.Windows.AppLifecycle;
using IOCore;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace IOApp.Dialogs
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    internal partial class MissingLibDialog : IODialog
    {
        public static MissingLibDialog Inst { get; private set; }

        public MissingLibDialog()
        {
            InitializeComponent();
            DataContext = Inst = this;
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string tag) return;

            if (tag == "Download")
            {
                _ = Launcher.LaunchUriAsync(new(
                        IntPtr.Size == 4 ?
                        "https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/download/gs10021/gs10021w32.exe" :
                        "https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/download/gs10021/gs10021w64.exe")
                    );
            }
            else if (tag == "Restart")
                AppInstance.Restart(string.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}