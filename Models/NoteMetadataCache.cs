using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace WinUINotes.Models
{
    public sealed class NoteMetadata
    {
        public string Preview { get; set; } = string.Empty;
        public long DateBinary { get; set; }
        public ulong FileSize { get; set; }
        public long ModifiedUtcTicks { get; set; }
    }

    internal static class NoteMetadataCache
    {
        public const string CacheFilename = ".notes-index.json";
        private static readonly SemaphoreSlim CacheLock = new(1, 1);
        private static Dictionary<string, NoteMetadata>? entries;

        public static async Task<Dictionary<string, NoteMetadata>> LoadAsync(StorageFolder folder)
        {
            await CacheLock.WaitAsync();
            try
            {
                await EnsureLoadedAsync(folder);
                return new Dictionary<string, NoteMetadata>(entries!, StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task SaveSnapshotAsync(StorageFolder folder, Dictionary<string, NoteMetadata> snapshot)
        {
            await CacheLock.WaitAsync();
            try
            {
                entries = new Dictionary<string, NoteMetadata>(snapshot, StringComparer.OrdinalIgnoreCase);
                await WriteAsync(folder);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task UpdateAsync(Note note)
        {
            StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(note.Filename);
            BasicProperties properties = await file.GetBasicPropertiesAsync();

            await CacheLock.WaitAsync();
            try
            {
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                await EnsureLoadedAsync(folder);
                entries![note.Filename] = CreateEntry(note, properties);
                await WriteAsync(folder);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task RemoveAsync(string filename)
        {
            await CacheLock.WaitAsync();
            try
            {
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                await EnsureLoadedAsync(folder);
                if (entries!.Remove(filename))
                {
                    await WriteAsync(folder);
                }
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static NoteMetadata CreateEntry(Note note, BasicProperties properties)
        {
            return new NoteMetadata
            {
                Preview = note.Preview,
                DateBinary = note.Date.ToBinary(),
                FileSize = properties.Size,
                ModifiedUtcTicks = properties.DateModified.UtcDateTime.Ticks
            };
        }

        private static async Task EnsureLoadedAsync(StorageFolder folder)
        {
            if (entries is not null)
            {
                return;
            }

            entries = new Dictionary<string, NoteMetadata>(StringComparer.OrdinalIgnoreCase);
            try
            {
                StorageFile cacheFile = await folder.GetFileAsync(CacheFilename);
                string json = await FileIO.ReadTextAsync(cacheFile);
                Dictionary<string, NoteMetadata>? loaded = JsonSerializer.Deserialize<Dictionary<string, NoteMetadata>>(json);
                if (loaded is not null)
                {
                    entries = new Dictionary<string, NoteMetadata>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (JsonException)
            {
                entries.Clear();
            }
        }

        private static async Task WriteAsync(StorageFolder folder)
        {
            StorageFile cacheFile = await folder.CreateFileAsync(CacheFilename, CreationCollisionOption.ReplaceExisting);
            string json = JsonSerializer.Serialize(entries);
            await FileIO.WriteTextAsync(cacheFile, json);
        }
    }
}
