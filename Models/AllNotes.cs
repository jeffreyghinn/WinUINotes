using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace WinUINotes.Models
{
    public class AllNotes
    {
        private const int MaxConcurrentFileOperations = 8;

        public ObservableCollection<Note> Notes { get; set; } =
                                    new ObservableCollection<Note>();

        public async Task LoadNotesAsync()
        {
            Notes.Clear();
            StorageFolder storageFolder =
                          ApplicationData.Current.LocalFolder;
            Dictionary<string, NoteMetadata> cachedEntries = await NoteMetadataCache.LoadAsync(storageFolder);
            List<StorageFile> noteFiles = new();
            await GetFilesInFolderAsync(storageFolder, noteFiles);

            using SemaphoreSlim fileOperationLimit = new(MaxConcurrentFileOperations);
            ConcurrentDictionary<string, NoteMetadata> currentEntries = new(StringComparer.OrdinalIgnoreCase);
            Task<Note>[] loadTasks = noteFiles
                .Select(file => LoadNoteAsync(file, cachedEntries, currentEntries, fileOperationLimit))
                .ToArray();
            Note[] loadedNotes = await Task.WhenAll(loadTasks);

            foreach (Note note in loadedNotes)
            {
                Notes.Add(note);
            }

            await NoteMetadataCache.SaveSnapshotAsync(
                storageFolder,
                new Dictionary<string, NoteMetadata>(currentEntries, StringComparer.OrdinalIgnoreCase));
        }

        private async Task GetFilesInFolderAsync(StorageFolder folder, List<StorageFile> noteFiles)
        {
            IReadOnlyList<IStorageItem> storageItems =
                                        await folder.GetItemsAsync();
            foreach (IStorageItem item in storageItems)
            {
                if (item.IsOfType(StorageItemTypes.Folder))
                {
                    if (!string.Equals(item.Name, "Trash", StringComparison.OrdinalIgnoreCase))
                    {
                        await GetFilesInFolderAsync((StorageFolder)item, noteFiles);
                    }
                }
                else if (item.IsOfType(StorageItemTypes.File))
                {
                    StorageFile file = (StorageFile)item;
                    if (string.Equals(file.Name, NoteMetadataCache.CacheFilename, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    noteFiles.Add(file);
                }
            }
        }

        private static async Task<Note> LoadNoteAsync(
            StorageFile file,
            Dictionary<string, NoteMetadata> cachedEntries,
            ConcurrentDictionary<string, NoteMetadata> currentEntries,
            SemaphoreSlim fileOperationLimit)
        {
            await fileOperationLimit.WaitAsync();
            try
            {
                BasicProperties properties = await file.GetBasicPropertiesAsync();
                Note note = new()
                {
                    Filename = file.Name,
                    Date = file.DateCreated.DateTime,
                    LastEdited = properties.DateModified.DateTime,
                    IsSaved = true
                };

                if (cachedEntries.TryGetValue(file.Name, out NoteMetadata? cachedEntry) &&
                    cachedEntry.FileSize == properties.Size &&
                    cachedEntry.ModifiedUtcTicks == properties.DateModified.UtcDateTime.Ticks)
                {
                    note.Date = DateTime.FromBinary(cachedEntry.DateBinary);
                    note.Title = cachedEntry.Title ?? string.Empty;
                    note.SetCachedPreview(cachedEntry.Preview);
                }
                else
                {
                    await note.LoadPreviewAsync(file);
                }

                currentEntries[file.Name] = NoteMetadataCache.CreateEntry(note, properties);
                return note;
            }
            finally
            {
                fileOperationLimit.Release();
            }
        }
    }
}
