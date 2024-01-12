using Microsoft.UI.Xaml;
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
using ImageMagick;
using System.Collections.ObjectModel;
using Microsoft.VisualBasic;
using Windows.Storage;

namespace IOApp.Pages
{
    internal partial class Main : Page, INotifyPropertyChanged
    {
        public static Main Inst { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private static readonly ResourceLoader _resourceLoader = ResourceLoader.GetForViewIndependentUse();

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

        public List<ThumbnailItem> SOURCE_FILE_ITEMS = new();
        public RangeObservableCollection<ThumbnailItem> FileItems { get; private set; } = new();

        private string _inputTypes = string.Empty;
        public string InputTypes { get => _inputTypes; set { _inputTypes = value; PropertyChanged?.Invoke(this, new(nameof(InputTypes))); } }

        private string _fileCountText = string.Empty;
        public string FileCountText { get => _fileCountText; set { _fileCountText = value; PropertyChanged?.Invoke(this, new(nameof(FileCountText))); } }



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
                    FileGridView.SelectedIndex = 0;
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

                                            //lock (DBManager.Inst.Locker)
                                            //{
                                            //    AppDbContext.Inst.Files.Add(new(path));

                                            //    AppDbContext.Inst.SaveChanges();
                                            //}
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

        public void SelectIndex(int index)
        {
            FileGridView.SelectedIndex = index;
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

        private void FileGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue)
            {
                if (args.Item is not ThumbnailItem item) return;
                try
                {
                    item.LoadBasicInfoIfNotExist();
                }
                catch { }
            }
        }

        private void MediaControl_OnPlay(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ThumbnailItem item) return;

            MainWindow.Inst.SetCurrentNavigationViewItem(typeof(RemoveObject).ToString(), item);
        }

        private void MediaControl_OnRemove(object sender, RoutedEventArgs e)
        {
            if ((e.OriginalSource as Control).Tag is not string tag) return;
            if ((sender as FrameworkElement)?.DataContext is not ThumbnailItem item) return;

            //lock (DBManager.Inst.Locker)
            //{
            //    var file = AppDbContext.Inst.Files.FirstOrDefault(i => i.Path == item.InputFilePath);
            //    if (file != null)
            //    {
            //        try
            //        {
            //            AppDbContext.Inst.Files.Remove(file);
            //            AppDbContext.Inst.SaveChanges();

            //            FileItems.Remove(item);
            //        }
            //        catch { }
            //    }
            //}
        }

        public static void RemoveMany<T>(
            Collection<T> itemList,
            int parentSelectedIndex,
            IEnumerable<T> items,
            bool doDelete,

            Action pageAction = null,
            Action<int> selectIndexAction = null)
        {
            var selectedIndex = parentSelectedIndex;

            foreach (var item in items)
            {
                var removedIndex = itemList.IndexOf(item);

                var oldSelectedIndex = selectedIndex;
                var newSelectedIndex = selectedIndex;

                if (itemList.Count > 1)
                {
                    if (oldSelectedIndex == removedIndex)
                    {
                        if (oldSelectedIndex == itemList.Count - 1)
                            newSelectedIndex = oldSelectedIndex - 1;
                    }
                    else if (oldSelectedIndex > removedIndex)
                        newSelectedIndex--;

                    selectedIndex = newSelectedIndex;
                }

                pageAction?.Invoke();

                itemList.Remove(item);

                if (doDelete)
                {
                    if (item is ThumbnailItem thumbnail)
                        Utils.DeleteFileOrDirectory(thumbnail.CacheImagePath);
                }
            }

            if (itemList.Count > 0)
                selectIndexAction?.Invoke(selectedIndex);

            GC.Collect();
        }

        private void Item_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ThumbnailItem item) return;

            MainWindow.Inst.SetCurrentNavigationViewItem(typeof(RemoveObject).ToString(), Tuple.Create(FileItems.ToList(), FileItems.IndexOf(item)));
        }

        private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as ListView).SelectedItem is not ThumbnailItem item) return;
            item.LoadBasicInfoIfNotExist();
        }

        private void KeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (Utils.Any(_status, StatusType.Loading, StatusType.Processing)) return;
            var e = args.KeyboardAccelerator;

            if (e.Modifiers == VirtualKeyModifiers.Control)
            {
                switch (e.Key)
                {
                    case VirtualKey.O:
                        OpenInputPicker();
                        break;
                }
            }
        }

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

                FileCountText = FileItems.Count > 0 && FileGridView.SelectedIndex >= 0 ? $"{FileGridView.SelectedIndex + 1}/{FileItems.Count}" : $"{FileItems.Count}";
            };

            FileItems.CollectionChanged += (object sender, NotifyCollectionChangedEventArgs e) => fileItemsCollectionChangedAction();
            fileItemsCollectionChangedAction();

            //lock (DBManager.Inst.Locker)
            //{
            //    AddFiles(AppDbContext.Inst.Files.Select(i => i.Path).ToList());
            //}
        }

        #endregion

        private void EnableControlButton(Control control)
        {
            var processing = Utils.Any(_status, StatusType.Loading, StatusType.Processing);
        }

        private void EnableAllControlButtons()
        {
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Control control) return;

            string title = null;
            string subTitle = null;
            Action action = null;

            //if (control == RemoveAllButton)
            //{
            //    title = _resourceLoader.GetString("ClearItemList");
            //    action = () =>
            //    {
            //        RemoveAll();
            //    };
            //}

            MainWindow.Inst.ShowConfirmTeachingTip(sender, title, subTitle, action);
        }
    }
}