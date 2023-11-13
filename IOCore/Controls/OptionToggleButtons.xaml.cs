using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using IOCore.Types;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace IOCore.Controls
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class SelectionToggleButtons : UserControl
    {
        private static readonly Type _type = typeof(SelectionToggleButtons);

        public SelectionToggleButtons()
        {
            InitializeComponent();

            IsEnabledChanged += (object sender, DependencyPropertyChangedEventArgs e) =>
            {
                foreach (var i in ItemsSource)
                    i.IsEnabled = IsEnabled;
            };
        }

        public ObservableCollection<Option> ItemsSource
        {
            get => (ObservableCollection<Option>)GetValue(_itemsSourceProperty);
            set
            {
                SetValue(_itemsSourceProperty, value);
                foreach (var i in value)
                {
                    i.AutoRaise = true;
                    i.IsEnabled = IsEnabled;
                }
            }
        }
        public static readonly DependencyProperty _itemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(ObservableCollection<Option>), _type, new(default));

        public ButtonTypes.SizeOption Size { get => (ButtonTypes.SizeOption)GetValue(_sizeProperty); set => SetValue(_sizeProperty, value); }
        public static readonly DependencyProperty _sizeProperty = DependencyProperty.Register(nameof(Size), typeof(ButtonTypes.SizeOption), _type, new(ButtonTypes.SizeOption.Designed));

        public double ConstantScale { get => (double)GetValue(_constantScaleProperty); set => SetValue(_constantScaleProperty, value); }
        public static readonly DependencyProperty _constantScaleProperty = DependencyProperty.Register(nameof(ConstantScale), typeof(double), _type, new(1.0));

        public new Visibility Visibility { get => (Visibility)GetValue(_visibilityProperty); set => SetValue(_visibilityProperty, value); }
        private static readonly DependencyProperty _visibilityProperty = DependencyProperty.Register(nameof(Visibility), typeof(Visibility), _type, new(Visibility.Visible,
            (DependencyObject d, DependencyPropertyChangedEventArgs e) => (d as UIElement).Visibility = (Visibility)e.NewValue));

        //

        public event TappedEventHandler SelectionChanged;
        public Option SelectedItem { get; private set; }

        private void OptionItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Option item) return;

            foreach (var i in ItemsSource)
                if (i != item)
                    i.IsSelected = false;
            item.IsSelected = true;

            SelectedItem = item;

            SelectionChanged?.Invoke(this, e);
        }
    }
}