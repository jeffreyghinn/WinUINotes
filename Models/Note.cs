using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace WinUINotes.Models
{
    public class Note : INotifyPropertyChanged
    {
        private readonly StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        private string filename = string.Empty;
        private string text = string.Empty;
        private string preview = string.Empty;
        private DateTime date = DateTime.Now;
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

        public DateTime Date
        {
            get => date;
            set => SetProperty(ref date, value);
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

        public async Task DeleteAsync()
        {
            await fileOperationLock.WaitAsync();
            try
            {
                StorageFile noteFile = (StorageFile)await storageFolder.TryGetItemAsync(Filename);
                if (noteFile is not null)
                {
                    await noteFile.DeleteAsync();
                }

                await NoteMetadataCache.RemoveAsync(Filename);
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
