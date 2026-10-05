using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using WinUINotes.Services;

namespace WinUINotes.Models
{
    public class Note : INotifyPropertyChanged
    {
        private readonly StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        private string filename = string.Empty;
        private string text = string.Empty;
        private string preview = string.Empty;
        private string title = string.Empty;
        private bool isProEnabled;
        private DateTime date = DateTime.Now;
        private DateTime lastEdited = DateTime.Now;
        private readonly SemaphoreSlim fileOperationLock = new(1, 1);

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Filename
        {
            get => filename;
            set => SetProperty(ref filename, value);
        }

        public string Text
        {
            get => text;
            set
            {
                if (SetProperty(ref text, value))
                {
                    Preview = GetPreview(value);
                }
            }
        }

        public string Preview
        {
            get => preview;
            private set => SetProperty(ref preview, value);
        }

        // Empty means use the note's original creation date as its title.
        public string Title
        {
            get => title;
            set
            {
                if (SetProperty(ref title, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListTitle)));
                }
            }
        }

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Date.ToString() : Title;
        public string ListTitle => isProEnabled && !string.IsNullOrWhiteSpace(Title) ? Title : Date.ToString();

        internal void SetProEnabled(bool value)
        {
            if (isProEnabled != value)
            {
                isProEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListTitle)));
            }
        }

        public DateTime Date
        {
            get => date;
            set
            {
                if (SetProperty(ref date, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListTitle)));
                }
            }
        }

        public DateTime LastEdited
        {
            get => lastEdited;
            set => SetProperty(ref lastEdited, value);
        }

        public bool IsSaved { get; internal set; }
        public bool IsContentLoaded { get; private set; } = true;
        public bool HasChanges { get; internal set; }
        public bool IsDeleted { get; private set; }

        public Note()
        {
            Filename = "notes" + DateTime.Now.ToBinary().ToString() + ".txt";
        }

        public async Task SaveAsync()
        {
            await fileOperationLock.WaitAsync();
            try
            {
                StorageFile noteFile = (StorageFile)await storageFolder.TryGetItemAsync(Filename);
                if (noteFile is null)
                {
                    noteFile = await storageFolder.CreateFileAsync(Filename, CreationCollisionOption.ReplaceExisting);
                }

                string textToSave = Text;
                await FileIO.WriteTextAsync(noteFile, textToSave);
                BasicProperties properties = await noteFile.GetBasicPropertiesAsync();
                LastEdited = properties.DateModified.DateTime;
                IsSaved = true;
                IsContentLoaded = true;
                await NoteMetadataCache.UpdateAsync(this);
            }
            finally
            {
                fileOperationLock.Release();
            }
        }

        public async Task LoadPreviewAsync(StorageFile noteFile)
        {
            using StreamReader reader = new(noteFile.Path);
            char[] previewCharacters = new char[300];
            int charactersRead = await reader.ReadBlockAsync(previewCharacters, 0, previewCharacters.Length);
            Preview = new string(previewCharacters, 0, charactersRead);
            IsContentLoaded = false;
        }

        internal void SetCachedPreview(string cachedPreview)
        {
            Preview = cachedPreview;
            IsContentLoaded = false;
        }

        public async Task LoadContentAsync()
        {
            if (IsContentLoaded)
            {
                return;
            }

            StorageFile noteFile = await storageFolder.GetFileAsync(Filename);
            Text = await FileIO.ReadTextAsync(noteFile);
            IsContentLoaded = true;
        }

        public async Task DeleteAsync(bool moveToTrash = false)
        {
            await fileOperationLock.WaitAsync();
            try
            {
                IStorageItem? noteFile = await storageFolder.TryGetItemAsync(Filename);
                if (noteFile is StorageFile && moveToTrash)
                {
                    await TrashService.MoveToTrashAsync(this);
                }
                else
                {
                    if (noteFile is StorageFile existingFile)
                    {
                        await existingFile.DeleteAsync();
                    }

                    await NoteMetadataCache.RemoveAsync(Filename);
                }
                IsDeleted = true;
            }
            finally
            {
                fileOperationLock.Release();
            }
        }

        private string GetPreview(string value)
        {
            return value.Length <= 300 ? value : value[..300];
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
