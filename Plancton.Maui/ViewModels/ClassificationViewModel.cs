using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using Plancton.Maui.Helper;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace Plancton.Maui.ViewModels
{
    public partial class ClassificationViewModel : ObservableObject
    {
        [ObservableProperty] private ImageSource previewImage;
        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private bool isImageLoaded;

        public bool IsImageNotLoaded => !isImageLoaded;
        [ObservableProperty]
        private string tagJsonInput;
        private byte[] _originalImageBytes;
        private byte[] _bgRemovedImageBytes;
        private byte[] _latestModifiedImageBytes;
        private SKColor _customBackgroundColor = SKColors.White;
        private string _lastPickedImagePath;
        public ObservableCollection<string> Chips { get; set; } = new();

        public ICommand PickImageCommand => new AsyncRelayCommand(OnPickImageAsync);
        public ICommand ReplaceWithRedCommand => new RelayCommand(() => ReplaceBackground("Red"));
        public ICommand ReplaceWithTransparentCommand => new RelayCommand(() => ReplaceBackground("Transparent"));
        public ICommand PickCustomColorCommand => new AsyncRelayCommand(OnPickCustomColorAsync);
        public ICommand SaveImageCommand => new AsyncRelayCommand(SaveImageAsync);
        public ICommand PickNewImageCommand => new AsyncRelayCommand(OnPickImageAsync);
        public ICommand SaveTagsCommand => new Command(SaveTagsToImage);
        public ICommand AddChipCommand { get; }
        public ICommand RemoveChipCommand { get; }

        public ClassificationViewModel()
        {
            AddChipCommand = new Command(OnAddChip);
            RemoveChipCommand = new Command<string>(OnRemoveChip);
        }
        private void OnAddChip()
        {
            var text = ChipEntry?.Trim();
            if (!string.IsNullOrEmpty(text) && !Chips.Contains(text))
            {
                Chips.Add(text);
                ChipEntry = string.Empty;
            }
        }
        private void OnRemoveChip(string chip)
        {
            if (Chips.Contains(chip))
            {
                Chips.Remove(chip);
            }
        }

        private string chipEntry;
        public string ChipEntry
        {
            get => chipEntry;
            set
            {
                if (chipEntry != value)
                {
                    chipEntry = value;
                    OnPropertyChanged(nameof(ChipEntry));
                }
            }
        }

        private async Task OnPickImageAsync()
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select an Image",
                FileTypes = FilePickerFileType.Images
            });

            if (result == null)
                return;

            _lastPickedImagePath = result.FullPath;

            using var stream = await result.OpenReadAsync();
            _originalImageBytes = new byte[stream.Length];
            await stream.ReadAsync(_originalImageBytes);

            PreviewImage = ImageSource.FromStream(() => new MemoryStream(_originalImageBytes));
            await RemoveBackgroundFirstTimeAsync();

            IsImageLoaded = true;
            OnPropertyChanged(nameof(IsImageNotLoaded));
        }

        private async Task RemoveBackgroundFirstTimeAsync()
        {
            IsBusy = true;
            try
            {
                using var httpClient = new HttpClient();
                var base64String = Convert.ToBase64String(_originalImageBytes);
                var form = new MultipartFormDataContent
                {
                    { new StringContent(base64String), PlanctonConsts.ImageB64 }
                };

                var response = await httpClient.PostAsync(PlanctonConsts.PredisAIUrl, form);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    string imageUrl = doc.RootElement.GetProperty("data").GetProperty("bg_removed_image").GetString();
                    _bgRemovedImageBytes = await httpClient.GetByteArrayAsync(imageUrl);
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Error", ex.Message, "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ReplaceBackground(string type)
        {
            if (_bgRemovedImageBytes == null)
            {
                Application.Current.MainPage.DisplayAlert("Error", "Background not removed yet", "OK");
                return;
            }

            _latestModifiedImageBytes = AddOrRemoveBackground(_bgRemovedImageBytes, type);
            PreviewImage = ImageSource.FromStream(() => new MemoryStream(_latestModifiedImageBytes));
        }

        private async Task OnPickCustomColorAsync()
        {
            // Replace this with a real color picker popup
            var color = await PickColorAsync();
            if (color != null)
            {
                _customBackgroundColor = color.Value;
                ReplaceBackground("Custom");
            }
        }

        private async Task SaveImageAsync()
        {
            if (_latestModifiedImageBytes == null)
            {
                await Application.Current.MainPage.DisplayAlert("Error", "No image to save.", "OK");
                return;
            }

#if ANDROID || IOS
    var folderPath = FileSystem.Current.AppDataDirectory;
#else
            var folderPath = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "Resources", "Images"));
#endif

            Directory.CreateDirectory(folderPath);
            var filename = $"image_{DateTime.Now:yyyyMMdd_hhmmss}.png";
            var filePath = Path.Combine(folderPath, filename);

            try
            {
                await File.WriteAllBytesAsync(filePath, _latestModifiedImageBytes);
                await Application.Current.MainPage.DisplayAlert("Saved", $"Image saved to:\n{filePath}", "OK");
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private byte[] AddOrRemoveBackground(byte[] imageBytes, string type)
        {
            using var inputStream = new SKManagedStream(new MemoryStream(imageBytes));
            using var original = SKBitmap.Decode(inputStream);
            var info = new SKImageInfo(original.Width, original.Height);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;

            if (type == "Red") canvas.Clear(SKColors.Red);
            else if (type == "Transparent")
            {
                int tileSize = 10;
                var lightGray = new SKColor(200, 200, 200);
                var white = SKColors.White;
                for (int y = 0; y < info.Height; y += tileSize)
                    for (int x = 0; x < info.Width; x += tileSize)
                    {
                        bool isLight = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                        var paint = new SKPaint { Color = isLight ? lightGray : white };
                        canvas.DrawRect(new SKRect(x, y, x + tileSize, y + tileSize), paint);
                    }
            }
            else if (type == "Custom") canvas.Clear(_customBackgroundColor);

            canvas.DrawBitmap(original, 0, 0);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private async Task<SKColor?> PickColorAsync()
        {
            var popup = new ColorPickerPopup();
            var result = await Application.Current.MainPage.ShowPopupAsync(popup);

            if (result is Color selectedColor)
            {
                return new SKColor(
                    (byte)(selectedColor.Red * 255),
                    (byte)(selectedColor.Green * 255),
                    (byte)(selectedColor.Blue * 255),
                    (byte)(selectedColor.Alpha * 255)
                );
            }

            return null;
        }

        public void AddTagToImage(string imagePath, ObservableCollection<string> tags)
        {
            using (var image = new MagickImage(imagePath))
            {
                var profile = image.GetIptcProfile() ?? new IptcProfile();
                var existingTags = profile.Values.Where(v => v.Tag == IptcTag.Keyword).Select(v => v.Value.ToString()).ToList();
                var allTags = existingTags.Union(tags, StringComparer.OrdinalIgnoreCase).ToList();

                profile.RemoveValue(IptcTag.Keyword);
                foreach (var tag in allTags)
                {
                    profile.SetValue(IptcTag.Keyword, tag);
                }
                image.SetProfile(profile);
                image.Write(imagePath);
                if (existingTags.Count > 0)
                    Application.Current.MainPage.DisplayAlert("Success", "Tags Updated Successfully", "OK");
                else
                    Application.Current.MainPage.DisplayAlert("Success", "Tags Added Successfully", "OK");
                TagJsonInput = null;
            }
        }
        private void SaveTagsToImage()
        {
            if (Chips.Count > 0)
                AddTagToImage(_lastPickedImagePath, Chips);
            else
            {
                Application.Current.MainPage.DisplayAlert("Warning", "Oops! Don't forget to add at least one tag.", "OK");
                return;
            }
        }
    }
}
