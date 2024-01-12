using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using IOApp.Pages;
using IOCore.Libs;
using IOCore;
using IOApp.Dialogs;
using System.Linq;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace IOApp.Features
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    internal partial class MediaControl : IOUserControl
    {
        private static readonly Type _type = typeof(MediaControl);

        public MediaControl()
        {
            InitializeComponent();
        }

        public bool IsRecent { get => (bool)GetValue(_isRecentProperty); set => SetValue(_isRecentProperty, value); }
        private static readonly DependencyProperty _isRecentProperty = DependencyProperty.Register(nameof(IsRecent), typeof(bool), _type, new(true, (DependencyObject d, DependencyPropertyChangedEventArgs e) => 
        {
            var control = d as MediaControl;

            if (control?.CorruptedRemoveItem != null)
                control.CorruptedRemoveItem.Visibility = control.IsRecent ? Visibility.Visible : Visibility.Collapsed;

            if (control?.RemoveFromList != null)
                control.RemoveFromList.Visibility = control.IsRecent ? Visibility.Visible : Visibility.Collapsed;
        }));

        //

        public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);

        public event RoutedEventHandler OnPlay;
        public event RoutedEventHandler OnPrivateChanged;
        public event RoutedEventHandler OnRemove;

        private void Item_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            OnPlay?.Invoke(sender, e);
        }

        private async void MenuItemButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string tag) return;
            if ((sender as FrameworkElement)?.DataContext is not ThumbnailItem item) return;
            if (tag== "Open")
                MainWindow.Inst.SetCurrentNavigationViewItem(typeof(RemoveObject).ToString(), Main.Inst.FileItems);
            if (tag == "Play")
                OnPlay?.Invoke(sender, e);
            if (tag == "Private")
                OnPrivateChanged?.Invoke(sender, e);
            else if (tag == "Properties")
                await new PropertiesDialog(item).Dialog().ShowAsync();
            else if (tag == "RevealInFileExplorer")
                Utils.RevealInFileExplorer(item.CacheImagePath);  
            else if (tag == "RemoveFromList" || tag == "RemovePermanently")
            {
                OnRemove?.Invoke(sender, e);
            }
        }

        private void MenuItemButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string tag) return;
            if ((sender as FrameworkElement)?.DataContext is not ThumbnailItem item) return;

            if (tag == "Play")
                OnPlay?.Invoke(sender, e);
        }
    }
}