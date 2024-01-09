﻿using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Windows.Storage.Pickers;
using System.IO;
using ImageMagick;
using System.Linq;
using Windows.Win32;
using IOApp.Features;
using IOApp.Configs;
using IOCore.Libs;
using OpenCvSharp;
using OpenCvSharp.Extensions;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace IOApp.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    sealed partial class SaveSettings : Page, INotifyPropertyChanged
    {
        public static SaveSettings Inst { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private static readonly ResourceLoader _resourceLoader = ResourceLoader.GetForViewIndependentUse();

        public ContentDialog _dialog;

        private Main.StatusType _status;
        public Main.StatusType Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }

        private ThumbnailItem _item;
        public ThumbnailItem Item
        {
            get => _item; 
            set 
            { 
                _item = value;

                WidthTextBox.TextChanging -= SizeTextBox_TextChanging;
                HeightTextBox.TextChanging -= SizeTextBox_TextChanging;

                WidthTextBox.Text = Item.InputInfo.Width.ToString();
                HeightTextBox.Text = Item.InputInfo.Height.ToString();

                WidthTextBox.TextChanging += SizeTextBox_TextChanging;
                HeightTextBox.TextChanging += SizeTextBox_TextChanging;

                ThumbnailImage.Source = Item.OutputThumbnail;

                PropertyChanged?.Invoke(this, new(nameof(Item))); 
            }
        }

        private FileSavePicker _outputFilePicker;

        public SaveSettings(ContentDialog dialog)
        {
            InitializeComponent();
            Inst = this;
            DataContext = this;

            _dialog = dialog;
        }

        #region WIDTH_HEIGHT_CONFIGS

        private void SizeTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            args.Cancel = args.NewText.Any(i => !char.IsDigit(i));
        }

        private void SizeTextBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            if (sender.Text == string.Empty) return;

            var width = 0;
            var height = 0;

            WidthTextBox.TextChanging -= SizeTextBox_TextChanging;
            HeightTextBox.TextChanging -= SizeTextBox_TextChanging;

            if (sender == WidthTextBox)
            {
                try
                {
                    width = Math.Max(int.Parse(sender.Text), 0);
                }
                catch (FormatException)
                {
                    sender.Text = width.ToString();
                }

                if (sender.Text.StartsWith("0"))
                    WidthTextBox.Text = width.ToString();

                HeightTextBox.Text = Math.Max(Utils.Round((double)Item.InputInfo.Height.GetValueOrDefault(1) / Item.InputInfo.Width.GetValueOrDefault(1) * width), 0).ToString();
            }
            else if (sender == HeightTextBox)
            {
                try
                {
                    height = Math.Max(int.Parse(sender.Text), 0);
                }
                catch (FormatException)
                {
                    HeightTextBox.Text = height.ToString();
                }

                if (sender.Text.StartsWith("0"))
                    HeightTextBox.Text = height.ToString();

                WidthTextBox.Text = Math.Max(Utils.Round((double)Item.InputInfo.Width.GetValueOrDefault(1) / Item.InputInfo.Height.GetValueOrDefault(1) * height), 0).ToString();
            }

            WidthTextBox.TextChanging += SizeTextBox_TextChanging;
            HeightTextBox.TextChanging += SizeTextBox_TextChanging;
        }

        private void SizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;

            if (textBox.Text == string.Empty || textBox.Text == "0")
            {
                WidthTextBox.Text = "1";
                HeightTextBox.Text = "1";
            }
        }

        #endregion

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_outputFilePicker == null)
            {
                _outputFilePicker = new() { SuggestedStartLocation = PickerLocationId.Desktop, SuggestedFileName = "Image" };
                WinRT.Interop.InitializeWithWindow.Initialize(_outputFilePicker, PInvoke.GetActiveWindow());

                _outputFilePicker.FileTypeChoices.Clear();
                foreach (var i in Profile.OUTPUT_IMAGE_FORMATS)
                    if (i.Value.IsSupported)
                        _outputFilePicker.FileTypeChoices.Add(i.Value.Name, new List<string>() { i.Value.Extension });
            }

            _outputFilePicker.SuggestedFileName = Path.GetFileNameWithoutExtension(Item.InputFilePath);

            var storageFile = await _outputFilePicker.PickSaveFileAsync();
            if (storageFile == null) return;

            Status = Main.StatusType.Processing;

            var width = Utils.GetValueOrDefault(WidthTextBox.Text, 1);
            var height = Utils.GetValueOrDefault(HeightTextBox.Text, 1);

            IProgress<Main.StatusType> progress = new Progress<Main.StatusType>((Main.StatusType status) =>
            {
                Utils.RevealInFileExplorer(storageFile.Path);
                Status = status;
            });

            if (ImageMagickUtils.IsVectorFamily(Item.InputInfo.Format))
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        // INPAINT 
                        Cv2.Resize(_item.Mask, _item.Mask, _item.SourceImage.Size());
                        _item.InpaintedImage = new Mat();
                        Cv2.Inpaint(_item.SourceImage, _item.Mask, _item.InpaintedImage, 30, InpaintMethod.NS);
                        var bitmap = BitmapConverter.ToBitmap(_item.InpaintedImage);

                        // BITMAP TO MAGICK IMAGE
                        MemoryStream ms = new MemoryStream();
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;
                        var image = new MagickImage(ms);

                        // SAVE WITH CONFIG
                        ImageMagickUtils.AutoOrient(image);
                        image.Quality = 100;
                        image.Resize(width, height);
                        image.Write(storageFile.Path);
                        image.Dispose();
                        bitmap.Dispose();

                        progress.Report(Main.StatusType.Processed);
                    }
                    catch
                    {
                        progress.Report(Main.StatusType.ProcessFailed);
                    }
                });
            }
            else
            {
                Item.LoadImageCacheIfNotExist(false, new Progress<int>((int cacheImageLevel) =>
                {
                    if (cacheImageLevel == 2)
                    {
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                // INPAINT 
                                Cv2.Resize(_item.Mask, _item.Mask, _item.SourceImage.Size());
                                _item.InpaintedImage = new Mat();
                                Cv2.Inpaint(_item.SourceImage, _item.Mask, _item.InpaintedImage, 30, InpaintMethod.NS);
                                var bitmap = BitmapConverter.ToBitmap(_item.InpaintedImage);

                                // BITMAP TO MAGICK IMAGE
                                MemoryStream ms = new MemoryStream();
                                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                ms.Position = 0;
                                var image = new MagickImage(ms);

                                // SAVE WITH CONFIG
                                ImageMagickUtils.AutoOrient(image);
                                image.Quality = 100;
                                image.Resize(width, height);
                                image.Write(storageFile.Path);
                                image.Dispose();
                                bitmap.Dispose();

                                progress.Report(Main.StatusType.Processed);
                            }
                            catch
                            {
                                progress.Report(Main.StatusType.ProcessFailed);
                            }
                        });
                    }
                }));
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _dialog.Hide();
        }
    }
}