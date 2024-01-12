using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using IOCore.Libs;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace IOCore
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public abstract class IOPage : Page, INotifyPropertyChanged
    {
        internal static IOPage Inst { get; private set; }

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

        public IOPage()
        {
            Inst = this;
        }
    }
}
