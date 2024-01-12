using Microsoft.UI.Xaml;
using IOCore;
using IOApp.Features;

namespace IOApp.Dialogs
{
    internal partial class PropertiesDialog : IODialog
    {
        public static PropertiesDialog Inst { get; private set; }

        public ThumbnailItem CurrentItem { get; private set; }

        public PropertiesDialog(ThumbnailItem item)
        {
            InitializeComponent();
            DataContext = Inst = this;

            CurrentItem = item;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}