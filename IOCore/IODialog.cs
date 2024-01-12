using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using IOCore.Libs;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace IOCore
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public abstract class IODialog : Page, INotifyPropertyChanged
    {
        #region NotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        public bool SetAndNotify<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new(propertyName));
            return true;
        }

        public void Notify([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new(propertyName));
        #endregion

        internal static IODialog Inst { get; private set; }

        private static readonly ContentDialog _dialog = new();

        private static bool _isPreviewKeyDownRegistered = false;

        internal static void Init(XamlRoot xamlRoot)
        {
            _dialog.XamlRoot = xamlRoot;
        }

        public ContentDialog Dialog(bool preventEscape = false)
        {
            _dialog.Hide();
            
            _dialog.Content = this;

            if (_isPreviewKeyDownRegistered)
            {
                _dialog.PreviewKeyDown -= _dialog_PreviewKeyDown;
                _isPreviewKeyDownRegistered = false;
            }

            if (preventEscape)
            {
                _dialog.PreviewKeyDown += _dialog_PreviewKeyDown;
                _isPreviewKeyDownRegistered = true;
            }

            return _dialog;
        }

        private void _dialog_PreviewKeyDown(object sender, KeyRoutedEventArgs e) => e.Handled = e.Key == VirtualKey.Escape;

        public Action OnHide;

        public IODialog()
        {
            Inst = this;
        }

        public void Hide()
        {
            OnHide?.Invoke();
            
            _dialog.Hide();
            _dialog.Content = null;
            OnHide = null;
        }
    }
}
