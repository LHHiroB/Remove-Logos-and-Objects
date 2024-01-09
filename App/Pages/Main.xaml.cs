﻿using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Win32;
using OpenCvSharp;
using IOCore.Libs;
using IOApp.Features;
using IOApp.Configs;
using Windows.UI;

namespace IOApp.Pages
{
    internal partial class Main : Page, INotifyPropertyChanged
    {
        public static Main Inst { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private static readonly ResourceLoader _resourceLoader = ResourceLoader.GetForViewIndependentUse();

        public enum ShapeType
        {
            Line,
            Rectangle,
            Ellipse
        }

        public Dictionary<ShapeType, Control> SHAPES;

        public enum StatusType
        {
            Ready,
            Loading,
            Loaded,
            LoadFailed,
            Processing,
            Processed,
            ProcessFailed,
        }

        public static readonly Dictionary<StatusType, string> STATUSES = new()
		{
            { StatusType.Ready, "StatusReady" },
            { StatusType.Loading, "StatusLoading" },
            { StatusType.Loaded, "StatusLoaded" },
            { StatusType.LoadFailed, "StatusLoadFailed" },
            { StatusType.Processing, "StatusProcessing" },
            { StatusType.Processed, "StatusProcessed" },
            { StatusType.ProcessFailed, "StatusProcessFailed" },
        };

        private StatusType _status;
        public StatusType Status
        {
            get => _status;
            set
            {
                var prevStatus = _status;
                _status = value;

                PropertyChanged?.Invoke(this, new(nameof(Status)));
                PropertyChanged?.Invoke(this, new(nameof(StatusText)));

                MainWindow.Inst.StatusBarLoading(Utils.Any(_status, StatusType.Loading, StatusType.Processing));
                MainWindow.Inst.StatusBarSetText(_resourceLoader.GetString(STATUSES[_status]));

                if (_status == StatusType.Ready)
                {
                    LoadingProgressRing.Visibility = Visibility.Collapsed;
                }
                else if (_status == StatusType.Loading)
                {
                    LoadingProgressRing.Visibility = Visibility.Visible;
                    LoadingProgressRing.IsActive = true;

                    MainWindow.Inst.EnableNavigationViewItems(false);
                }
                else if (_status == StatusType.Loaded)
                {
                    LoadingProgressRing.Visibility = Visibility.Collapsed;

                    MainWindow.Inst.EnableNavigationViewItems(true);
                }
                else if (_status == StatusType.LoadFailed)
                {
                    LoadingProgressRing.Visibility = Visibility.Collapsed;

                    MainWindow.Inst.EnableNavigationViewItems(true);
                }
                else if (_status == StatusType.LoadFailed)
                {
                    LoadingProgressRing.Visibility = Visibility.Collapsed;

                    MainWindow.Inst.EnableNavigationViewItems(true);
                }
                else if (_status == StatusType.Processing)
                {
                    MainWindow.Inst.EnableNavigationViewItems(false);
                }
                else if (_status == StatusType.Processed)
                {
                    MainWindow.Inst.EnableNavigationViewItems(true);
                }
                else if (_status == StatusType.ProcessFailed)
                {
                    MainWindow.Inst.EnableNavigationViewItems(true);
                }

                EnableAllControlButtons();
            }
        }

        public string StatusText => _resourceLoader.GetString(STATUSES[_status]);

        private FileOpenPicker _inputFilesPicker;

        public RangeObservableCollection<ThumbnailItem> FileItems { get; private set; } = new();

        private ThumbnailItem _currentItem;
        public ThumbnailItem CurrentItem { get => _currentItem; set { _currentItem = value; PropertyChanged?.Invoke(this, new(nameof(CurrentItem))); } }
        private BitmapImage _currentBitmapImage;

        private string _inputTypes = string.Empty;
        public string InputTypes { get => _inputTypes; set { _inputTypes = value; PropertyChanged?.Invoke(this, new(nameof(InputTypes))); } }

        private string _fileCountText = string.Empty;
        public string FileCountText { get => _fileCountText; set { _fileCountText = value; PropertyChanged?.Invoke(this, new(nameof(FileCountText))); } }

        private bool _isCanvasDrawing = false;
        private Color _color = Colors.Blue;

        private Shape _shape;
        private Shape _cursorShape;


        private Windows.Foundation.Point _startPoint;
        private List<Windows.Foundation.Point> _polylinePoints = new();

        private int _currentRevision = 0;
        private static readonly List<System.Drawing.Bitmap> _imageHistory = new();
        private static readonly List<Mat> _maskHistory = new();

        private Mat _currentMask;
        private string _originalImage;
        private Mat _mask;
        private Mat _inpaintedImage;
        private Mat _sourceImage;
        private Mat _canvasImage = new();

        public Main()
        {
            InitializeComponent();
            Inst = this;
            DataContext = this;

            InitAllControls();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            MainWindow.Inst.StatusBarLoading(Utils.Any(_status, StatusType.Loading, StatusType.Processing));
            MainWindow.Inst.StatusBarSetText(_resourceLoader.GetString(STATUSES[_status]));
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Action action)
                action.Invoke();
        }

        public void AddFiles(IReadOnlyList<string> paths, Action startAction = null, Action<StatusType> endAction = null, Action<List<ThumbnailItem>> itemAction = null)
        {
            if (Utils.Any(_status, StatusType.Loading, StatusType.Processing)) return;

            Status = StatusType.Loading;
            startAction?.Invoke();

            var hasCorrupted = false;
            var hasEpsAndCannotRead = false;

            List<ThumbnailItem> items = new();
            var coreCount = Environment.ProcessorCount;
            object locked = new();

            var start = FileItems.Count == 0;

            IProgress<StatusType> progress = new Progress<StatusType>(status =>
            {
                if (status == StatusType.Loaded)
                {
                    if (hasEpsAndCannotRead)
                        MainWindow.Inst.ShowMissingLibDialog();
                    else if (hasCorrupted)
                        MainWindow.Inst.ShowMessageTeachingTip(null, string.Empty, _resourceLoader.GetString("LoadCorruptedSomeFiles"));
                }

                Status = status;
                endAction?.Invoke(status);

                if (start && FileItems.Count > 0)
                    FileListView.SelectedIndex = 0;
            });

            IProgress<List<ThumbnailItem>> itemProgress = new Progress<List<ThumbnailItem>>(items =>
            {
                lock (locked)
                {
                    FileItems.AddRange(items);
	                itemAction?.Invoke(items);
                }
            });

            _ = Task.Run(() =>
            {
                try
                {
                    var packages = paths.Chunk(256);

                    foreach (var package in packages)
                    {
                        var pathChunksPerPackage = package.Chunk(coreCount);

                        lock (locked)
                        {
                            items.Clear();
                        }

                        foreach (var pathChunks in pathChunksPerPackage)
                        {
                            Parallel.ForEach(pathChunks, path =>
                            {
                                try
                                {
                                    var isFilePath = Utils.IsFilePath(path);
                                    if (isFilePath.GetValueOrDefault(false) && !FileItems.Any(i => i.InputFilePath == path))
                                    {
                                        var imageMeta = ImageMagickUtils.GetMagickImageMeta(path);

                                        if (Profile.IsAcceptedInputFormat(imageMeta?.Format))
                                        {
                                            lock (locked)
                                            {
                                                items.Add(new() { InputInfo = new(path) });
                                            }
                                        }
                                        else throw new();
                                    }
                                }
                                catch (MissingLibException mle)
                                {
                                    if (mle.Message == "eps")
                                        hasEpsAndCannotRead = true;
                                    else
                                        hasCorrupted = true;
                                }
                                catch (Exception)
                                {
                                    hasCorrupted = true;
                                }
                            });
                        }

                        itemProgress.Report(items);
                    }

                    progress.Report(StatusType.Loaded);
                }
                catch (Exception)
                {
                    progress.Report(StatusType.LoadFailed);
                }
            });
        }

        private void RefreshPreviewBox()
        {
            PreviewBox.Visibility = Visibility.Visible;

            System.Drawing.SizeF maxPreviewSize = new(0, 0);

            maxPreviewSize = Utils.GetMaxContainSize((float)_sourceImage.Width, (float)_sourceImage.Height, (float)PreviewBox.ActualWidth, (float)PreviewBox.ActualHeight);

            PreviewImage.Width = maxPreviewSize.Width;
            PreviewImage.Height = maxPreviewSize.Height;

            CanvasDrawing.Width = maxPreviewSize.Width;
            CanvasDrawing.Height = maxPreviewSize.Height;

            var size = new Size(CanvasDrawing.Width, CanvasDrawing.Height);
            Cv2.Resize(_sourceImage, _canvasImage, size, (double)InterpolationFlags.LinearExact);

            System.Drawing.Bitmap bitmap = MatToBitmap(_canvasImage);
            var bitmapImage = Utils.ConvertBitmapToBitmapImage(bitmap);
            PreviewImage.Source = _currentBitmapImage = bitmapImage;

            _imageHistory.Add(bitmap);
            _maskHistory.Add(new Mat(_canvasImage.Size(), MatType.CV_8U, -1));
            _currentRevision = 0;
        }

        #region DRAWING_CANVAS

        private void Draw(ShapeType shape, Windows.Foundation.Point point)
        {
            if (shape == ShapeType.Line)
            {
                if (point == _startPoint)
                {
                    _polylinePoints = new();
                    _shape = new Polyline()
                    {
                        Stroke = new SolidColorBrush(_color),
                        StrokeThickness = SliderThickness.Value,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        Opacity = 0.4,
                    };
                    CanvasDrawing.Children.Add(_shape);
                }

                (_shape as Polyline).Points.Add(point);
                _polylinePoints.Add(new Windows.Foundation.Point(point.X, point.Y));
            }
            else if (shape == ShapeType.Rectangle)
            {
                double x, y;

                if (point == _startPoint)
                {
                    _shape = new Rectangle()
                    {
                        Stroke = new SolidColorBrush(_color),
                        Fill = new SolidColorBrush(_color),
                        Opacity = 0.4,
                    };

                    x = _startPoint.X;
                    y = _startPoint.Y;

                    CanvasDrawing.Children.Add(_shape);
                }
                else
                {
                    x = Math.Min(point.X, _startPoint.X);
                    y = Math.Min(point.Y, _startPoint.Y);

                    _shape.Width = Math.Max(point.X, _startPoint.X) - x;
                    _shape.Height = Math.Max(point.Y, _startPoint.Y) - y;
                }

                Canvas.SetLeft(_shape, x);
                Canvas.SetTop(_shape, y);
            }    
            else
            {
                double x, y;

                if (point == _startPoint)
                {
                    _shape = new Ellipse()
                    {
                        Stroke = new SolidColorBrush(_color),
                        Fill = new SolidColorBrush(_color),
                        Opacity = 0.4,
                    };

                    x = _startPoint.X;
                    y = _startPoint.Y;

                    CanvasDrawing.Children.Add(_shape);
                }
                else
                {
                    x = Math.Min(point.X, _startPoint.X);
                    y = Math.Min(point.Y, _startPoint.Y);

                    var w = Math.Max(point.X, _startPoint.X) - x;
                    var h = Math.Max(point.Y, _startPoint.Y) - y;

                    _shape.Width = w;
                    _shape.Height = h;
                }

                Canvas.SetLeft(_shape, x);
                Canvas.SetTop(_shape, y);
            }    
        }    

        private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isCanvasDrawing = true;
            _startPoint = e.GetCurrentPoint(CanvasDrawing).Position;
            Draw((ShapeType)ShapeButton.Tag, _startPoint);
        }

       
        private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(CanvasDrawing).Position;

            if ((ShapeType)ShapeButton.Tag == ShapeType.Line)
            {
                if (_cursorShape == null)
                {
                    _cursorShape = new Ellipse();
                    _cursorShape.Opacity = 0.4;

                    CanvasDrawing.Children.Add(_cursorShape);
                }
                _cursorShape.Width = SliderThickness.Value;
                _cursorShape.Height = SliderThickness.Value;
                _cursorShape.Fill = new SolidColorBrush(_color);

                Canvas.SetLeft(_cursorShape, point.X - SliderThickness.Value / 2);
                Canvas.SetTop(_cursorShape, point.Y - SliderThickness.Value / 2);
            }

            var isPointerOutside = point.X <= 0 || point.Y <= 0 || point.X >= CanvasDrawing.ActualWidth || point.Y >= CanvasDrawing.ActualHeight;

            if ( isPointerOutside )
            {
                CanvasDrawing.Children.Remove(_cursorShape);
                _cursorShape = null;
            }

            if (_isCanvasDrawing)
            {
                if (isPointerOutside)
                {
                    _isCanvasDrawing = false;

                    if (_startPoint != point)
                        InpaintImage(point);
                }
                else
                    Draw((ShapeType)ShapeButton.Tag, point);
            }
        }

        private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isCanvasDrawing = false;

            if (_startPoint != e.GetCurrentPoint(CanvasDrawing).Position)
            InpaintImage(e.GetCurrentPoint(CanvasDrawing).Position);
        }

        #endregion

        public void SelectIndex(int index)
        {
            FileListView.SelectedIndex = index;
        }    

        public void SelectItem(ThumbnailItem item)
        {
            if (!Utils.IsExistFileOrDirectory(item.InputFilePath))
            {
                RemoveMany(new() { item }, false);
                return;
            }

            Status = StatusType.Loading;

            item.LoadImageCacheIfNotExist(true, new Progress<int>((int cacheImageLevel) =>
            {
                if (cacheImageLevel > 0)
                {
                    if (FileListView.SelectedItem is not ThumbnailItem currentItem) return;

                    if (item == currentItem)
                    {
                        _imageHistory.Clear();
                        _maskHistory.Clear();
                        _currentRevision = 0;

                        CurrentItem = item;
                        _sourceImage = new Mat(item.CacheImagePath);
                        _originalImage = item.CacheImagePath;

                        RefreshPreviewBox();

                        EnableControlButton(PrevButton);
                        EnableControlButton(NextButton);

                        FileCountText = FileItems.Count > 0 && FileListView.SelectedIndex >= 0 ? $"{FileListView.SelectedIndex + 1}/{FileItems.Count}" : "-/-";

                        Status = StatusType.Loaded;

                        GC.Collect();
                    }
                }
                else
                {
                    PreviewImage.Source = _currentBitmapImage = null;
                    CurrentItem = null;
                    Status = StatusType.LoadFailed;
                }
            }));
        }

        public void RemoveMany(List<ThumbnailItem> items, bool doDelete)
        {
            FileListView.SelectionChanged -= FileListView_SelectionChanged;

            var selectedIndex = FileListView.SelectedIndex;

            foreach (var i in items)
            {
                var removedIndex = FileItems.IndexOf(i);

                var oldSelectedIndex = selectedIndex;
                var newSelectedIndex = selectedIndex;

                if (FileItems.Count > 1)
                {
                    if (oldSelectedIndex == removedIndex)
                    {
                        if (oldSelectedIndex == FileItems.Count - 1)
                            newSelectedIndex = oldSelectedIndex - 1;
                    }
                    else if (oldSelectedIndex > removedIndex)
                        newSelectedIndex--;

                    selectedIndex = newSelectedIndex;
                }

                FileItems.Remove(i);
                Utils.DeleteFileOrDirectory(i.CacheImagePath);

                if (doDelete)
                    Utils.DeleteFileOrDirectory(i.InputFilePath);

                i.Dispose();
            }

            FileListView.SelectionChanged += FileListView_SelectionChanged;

            if (FileItems.Count > 0)
                FileListView.SelectedIndex = selectedIndex;

            GC.Collect();
        }

        public void RemoveAll()
        {
            _currentBitmapImage = null;
            PreviewImage.Source = null;
            CurrentItem = null;

            foreach(var i in FileItems)
                Utils.DeleteFileOrDirectory(i.CacheImagePath);

            FileItems.Clear();
        }

        public async void SaveFile(ThumbnailItem item)
        {
            try
            {
                item.LoadBasicInfoIfNotExist();

                var saveFileDialog = new ContentDialog() { XamlRoot = XamlRoot };
                saveFileDialog.PreviewKeyDown += (object sender, KeyRoutedEventArgs e) =>
                {
                    e.Handled = e.Key == VirtualKey.Escape;
                };

                item.OutputThumbnail = Utils.ConvertBitmapToBitmapImage(_imageHistory[_currentRevision]);
                item.Mask = _currentMask;
                item.SourceImage = _sourceImage;
                item.InpaintedImage = _inpaintedImage;
    
                saveFileDialog.Content = new SaveSettings(saveFileDialog) { Item = item };
                _ = await saveFileDialog.ShowAsync();
            }
            catch (MissingLibException mle)
            {
                if (mle.Message == "eps")
                    MainWindow.Inst.ShowMissingLibDialog();
            }
            catch { }
        }

        public async void OpenInputPicker(Action startAction = null, Action<StatusType> endAction = null, Action<List<ThumbnailItem>> itemAction = null)
        {
            if (Utils.Any(_status, StatusType.Loading, StatusType.Processing)) return;

            try
            {
                if (_inputFilesPicker == null)
                {
                    _inputFilesPicker = new() { SuggestedStartLocation = PickerLocationId.ComputerFolder };
                    WinRT.Interop.InitializeWithWindow.Initialize(_inputFilesPicker, PInvoke.GetActiveWindow());

                    foreach (var extension in Profile.INPUT_EXTENSIONS)
                        _inputFilesPicker.FileTypeFilter.Add(extension);
                }

                var storageFiles = await _inputFilesPicker.PickMultipleFilesAsync();
                if (storageFiles.Count > 0)
                    AddFiles(storageFiles.Select(i => i.Path).ToList(), startAction, endAction, itemAction);
            }
            catch { }
        }

        private void InputFilesButton_Click(object sender, RoutedEventArgs e)
        {
            OpenInputPicker();
        }

        private void FileListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue)
            {
                var item = FileItems[args.ItemIndex];
                try
                {
                    item.LoadBasicInfoIfNotExist();
                }
                catch { }
            }
        }

        private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as ListView).SelectedItem is not ThumbnailItem item) return;

            try
            {
                item.LoadBasicInfoIfNotExist();
                SelectItem(item);
            }
            catch (MissingLibException mle)
            {
                if (mle.Message == "eps")
                    MainWindow.Inst.ShowMissingLibDialog();
            }
            catch { }
        }

        private void MenuItemButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement).DataContext is not ThumbnailItem item) return;
            if ((sender as FrameworkElement).Tag is not string tag) return;

            if (tag == "RevealInFileExplorer")
                Utils.RevealInFileExplorer(item.InputFilePath);
            else if (tag == "OpenWith")
                Utils.OpenFileWithDefaultApp(item.InputFilePath);
            else if (tag == "Remove" || tag == "Delete")
                RemoveMany(new() { item }, tag == "Delete");
        }

        private void PreviewBox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_currentBitmapImage != null)
                RefreshPreviewBox();
        }

        private void ControlButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Control control) return;

            if (control == SaveButton)
            {
                if (Utils.Any(_status, StatusType.Loading, StatusType.Processing)) return;
                if (FileListView.SelectedItem is not ThumbnailItem item) return;
                SaveFile(item);
            }
            else if (control == PrevButton)
            {
                if (FileListView.SelectedIndex > 0) FileListView.SelectedIndex--;
            }
            else if (control == NextButton)
            {
                if (FileListView.SelectedIndex < FileItems.Count - 1) FileListView.SelectedIndex++;
            }
        }

        private void KeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (Utils.Any(_status, StatusType.Loading, StatusType.Processing)) return;
            var e = args.KeyboardAccelerator;

            if (e.Modifiers == VirtualKeyModifiers.Control)
            {
                switch (e.Key)
                {
                    case VirtualKey.Z:
                        Undo("Undo");
                        break;
                    case VirtualKey.U:
                        Undo("Redo");
                        break;
                    case VirtualKey.S:
                        if (FileListView.SelectedItem is not ThumbnailItem item) return;
                        SaveFile(item);
                        break;
                    case VirtualKey.O:
                        OpenInputPicker();
                        break;
                }
            }
            else
            {
                switch (e.Key)
                {
                    case VirtualKey.Up:
                    case VirtualKey.Left:
                        if (FileListView.SelectedIndex > 0) FileListView.SelectedIndex--;
                        break;
                    case VirtualKey.Down:
                    case VirtualKey.Right:
                        if (FileListView.SelectedIndex < FileItems.Count - 1) FileListView.SelectedIndex++;
                        break;
                    case VirtualKey.Add:
                        SliderThickness.Value += 5;
                        _cursorShape.Width = SliderThickness.Value;
                        _cursorShape.Height = SliderThickness.Value;
                        break;
                    case VirtualKey.Subtract:
                        SliderThickness.Value -= 5;
                        _cursorShape.Width = SliderThickness.Value;
                        _cursorShape.Height = SliderThickness.Value;
                        break;
                }
            }
        }

        //INPAINT

        private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            SetColor.Background = new SolidColorBrush(args.NewColor);
            _color = args.NewColor;
        }

        private void InpaintImage(Windows.Foundation.Point endPoint)
        {
            _mask = new Mat(_canvasImage.Size(), MatType.CV_8U, -1);
            _inpaintedImage = new Mat();

            var shape = (ShapeType)ShapeButton.Tag;

            if (shape == ShapeType.Line)
            {
                List<IEnumerable<Point>> polylines = new() { _polylinePoints.Select(i => new Point(i.X, i.Y)) };
                Cv2.Polylines(_mask, polylines, false, Scalar.White, (int)SliderThickness.Value);
                CanvasDrawing.Children.Remove(_shape);
            }
            else if (shape == ShapeType.Rectangle)
            {
                Cv2.Rectangle(_mask, new(_startPoint.X, _startPoint.Y), new(endPoint.X, endPoint.Y), Scalar.White, -1);
                CanvasDrawing.Children.Remove(_shape);
            }
            else if (shape == ShapeType.Ellipse)
            {
                var ellipse = _shape as Ellipse;

                var centerPoint = new Point((_startPoint.X + endPoint.X) / 2, (_startPoint.Y + endPoint.Y) / 2);
                var size = new Size(ellipse.Width / 2, ellipse.Height / 2);
                Cv2.Ellipse(_mask, centerPoint, size, 0, 0, 360, Scalar.White, -1);
                CanvasDrawing.Children.Remove(ellipse);
            }

            Cv2.Inpaint(_canvasImage, _mask, _inpaintedImage, 30, InpaintMethod.NS);
            if (_currentMask == null)
                _currentMask = _mask;
            else
            {
                if (_currentMask.Size() != _mask.Size())
                    _currentMask = _mask;
                _currentMask = _currentMask + _mask;
            }

            System.Drawing.Bitmap bitmap = MatToBitmap(_inpaintedImage);
            var bitmapImage = Utils.ConvertBitmapToBitmapImage(bitmap);
            PreviewImage.Source = bitmapImage;

            _canvasImage = BitmapToMat(bitmap);

            if (_currentRevision != _imageHistory.Count - 1)
            {
                for (var i = _imageHistory.Count - 1; i > _currentRevision; i--)
                {
                    _imageHistory.RemoveAt(i);
                    _maskHistory.RemoveAt(i);
                }
            }

            _currentRevision++;

            _maskHistory.Add(_currentMask);
            _imageHistory.Add(bitmap);
            _currentBitmapImage = bitmapImage;
        }

        private void Undo(string tag)
        {
            if (Utils.Any(_status, StatusType.Loading, StatusType.Processing)) return;

            if (tag == "Undo")
            {
                if (_currentRevision == 0) return;
                else if (_currentRevision >= 1) _currentRevision--;
            }
            else if (tag == "Redo")
            {
                if (_currentRevision == _imageHistory.Count - 1) return;
                else _currentRevision++;
            }

            PreviewImage.Source = _currentBitmapImage = Utils.ConvertBitmapToBitmapImage(_imageHistory[_currentRevision]);
                _currentMask = _maskHistory[_currentRevision];
            _canvasImage = BitmapToMat(_imageHistory[_currentRevision]);
        }    

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string tag) return;
            Undo(tag);
        }

        public static System.Drawing.Bitmap MatToBitmap(Mat image) => OpenCvSharp.Extensions.BitmapConverter.ToBitmap(image);

        public static Mat BitmapToMat(System.Drawing.Bitmap bitmap) => OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap);

        #region DRAP_DROP_FILES

        private void Input_DragEnter(object sender, DragEventArgs e)
        {
            e.DragUIOverride.IsGlyphVisible = false;
            (sender as FrameworkElement).Opacity = 0.6;
        }

        private void Input_DragLeave(object sender, DragEventArgs e)
        {
            (sender as FrameworkElement).Opacity = 1.0;
        }

        private void Input_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = _resourceLoader.GetString("PlusCopy");
        }

        private async void Input_Drop(object sender, DragEventArgs e)
        {
            if (e.Data != null)
            {
                (sender as FrameworkElement).Opacity = 1.0;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                if (storageItems.Count > 0)
                    AddFiles(storageItems.Select(i => i.Path).ToList());
            }

            (sender as FrameworkElement).Opacity = 1.0;
        }

        #endregion

        #region INIT_MAKE_DEFAULT_VALUE

        private void InitAllControls()
        {
            InputTypes = Profile.GetInputExtensionsTextByGroupFamily();

            void fileItemsCollectionChangedAction()
            {
                if (FileItems.Count == 0)
                {
                    WorkBox.Visibility = Visibility.Collapsed;
                    WelcomeBox.Visibility = Visibility.Visible;
                }
                else
                {
                    WorkBox.Visibility = Visibility.Visible;
                    WelcomeBox.Visibility = Visibility.Collapsed;

                    foreach ((var item, int i) in FileItems.Select((v, i) => (v, i)))
                        item.Position = i;
                }

                EnableAllControlButtons();

                FileCountText = FileItems.Count > 0 && FileListView.SelectedIndex >= 0 ? $"{FileListView.SelectedIndex + 1}/{FileItems.Count}" : $"{FileItems.Count}";
            };

            FileItems.CollectionChanged += (object sender, NotifyCollectionChangedEventArgs e) => fileItemsCollectionChangedAction();
            fileItemsCollectionChangedAction();

            //

            SHAPES = new()
            {
                { ShapeType.Line,      LineShapeMenuFlyoutItem },
                { ShapeType.Rectangle, RectangleShapeMenuFlyoutItem },
                { ShapeType.Ellipse,   EllipseShapeMenuFlyoutItem }
            };

            LineShapeMenuFlyoutItem.Text = "Pen";
            LineShapeMenuFlyoutItem.Icon = new FontIcon() { Glyph = "\uED63" };
            LineShapeMenuFlyoutItem.Tag = ShapeType.Line;

            RectangleShapeMenuFlyoutItem.Text = "Rectangle";
            RectangleShapeMenuFlyoutItem.Icon = new FontIcon() { Glyph = "\uE7FB" };
            RectangleShapeMenuFlyoutItem.Tag = ShapeType.Rectangle;

            EllipseShapeMenuFlyoutItem.Text = "Ellipse";
            EllipseShapeMenuFlyoutItem.Icon = new FontIcon() { Glyph = "\uEA3A" };
            EllipseShapeMenuFlyoutItem.Tag = ShapeType.Ellipse;

            LineShapeMenuFlyoutItem.IsChecked = true;

            ShapeButton.Icon = ((SHAPES[ShapeType.Line] as RadioMenuFlyoutItem).Icon as FontIcon).Glyph;
            ShapeButton.Tag = ShapeType.Line;
        }

        #endregion

        private void EnableControlButton(Control control)
        {
            var processing = Utils.Any(_status, StatusType.Loading, StatusType.Processing);

            if (control == SaveButton)
                control.IsEnabled = !processing && FileItems.Count > 0 && FileListView.SelectedIndex >= 0;
            else if (control == PrevButton)
                control.IsEnabled = !processing && FileItems.Count > 0 && FileListView.SelectedIndex > 0;
            else if (control == NextButton)
                control.IsEnabled = !processing && FileItems.Count > 0 && FileListView.SelectedIndex < FileItems.Count - 1;
        }

        private void EnableAllControlButtons()
        {
            EnableControlButton(SaveButton);

            EnableControlButton(PrevButton);
            EnableControlButton(NextButton);
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Control control) return;

            string title = null;
            string subTitle = null;
            Action action = null;

            if (control == RemoveAllButton)
            {
                title = _resourceLoader.GetString("ClearItemList");
                action = () =>
                {
                    RemoveAll();
                };
            }

            MainWindow.Inst.ShowConfirmTeachingTip(sender, title, subTitle, action);
        }

        private void ShapeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Control control) return;

            ShapeButton.Icon = ((control as RadioMenuFlyoutItem).Icon as FontIcon).Glyph;
            ShapeButton.Tag = (control as RadioMenuFlyoutItem).Tag;

            if ((ShapeType)ShapeButton.Tag == ShapeType.Line)
                Slider.Visibility = Visibility.Visible;
            else
                Slider.Visibility = Visibility.Collapsed;
        }

        private void AnimeMakerButton_Click(object sender, RoutedEventArgs e)
        {
            //ImageSource
            Mat originalImage = new Mat();
            originalImage = Cv2.ImRead(_originalImage);
            //Cv2.CvtColor(originalImage, originalImage, ColorConversionCodes.BGR2RGB);

            //GreyScale
            Mat grayScaleImage = new Mat();
            Cv2.CvtColor(originalImage, grayScaleImage, ColorConversionCodes.BGR2GRAY);

            //Smoothening
            Mat smoothGrayScale = new Mat();
            Cv2.MedianBlur(grayScaleImage, smoothGrayScale, 21);

            //Retrieve Edges
            Mat getEdge = new Mat();
            Cv2.AdaptiveThreshold(smoothGrayScale, getEdge, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, 15, 5);

            //Mask Image
            Mat colorImage = new Mat();
            Cv2.BilateralFilter(originalImage, colorImage, 9, 300, 300);

            //Cartoon Effect
            Mat cartoonImage = new Mat();
            Cv2.BitwiseAnd(colorImage, colorImage, cartoonImage, getEdge);

            System.Drawing.Bitmap bitmap = MatToBitmap(cartoonImage);
            var bitmapImage = Utils.ConvertBitmapToBitmapImage(bitmap);
            PreviewImage.Source = bitmapImage;
        }
    }
}