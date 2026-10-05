using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using WinUINotes.Models;

namespace WinUINotes.Services;

public sealed class TrashItem
{
    public string Filename { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
    public DateTime DateCreated { get; init; }
    public DateTime DeletedAt { get; init; }
    public string RetentionText { get; init; } = string.Empty;
}

public static class TrashService
{
    private const string TrashFolderName = "Trash";
    private const string IndexFilename = ".trash-index.json";
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task MoveToTrashAsync(Note note)
    {
        await Gate.WaitAsync();
        try
        {
            StorageFolder root = ApplicationData.Current.LocalFolder;
            StorageFolder trash = await root.CreateFolderAsync(TrashFolderName, CreationCollisionOption.OpenIfExists);
            StorageFile file = await root.GetFileAsync(note.Filename);
            await file.MoveAsync(trash, note.Filename, NameCollisionOption.ReplaceExisting);

            Dictionary<string, DateTime> index = await LoadIndexAsync(trash);
            index[note.Filename] = DateTime.UtcNow;
            await SaveIndexAsync(trash, index);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<IReadOnlyList<TrashItem>> LoadItemsAsync()
    {
        await Gate.WaitAsync();
        try
        {
            StorageFolder root = ApplicationData.Current.LocalFolder;
            StorageFolder trash = await root.CreateFolderAsync(TrashFolderName, CreationCollisionOption.OpenIfExists);
            Dictionary<string, DateTime> index = await LoadIndexAsync(trash);
            Dictionary<string, NoteMetadata> metadata = await NoteMetadataCache.LoadAsync(root);
            List<TrashItem> items = new();
            DateTime now = DateTime.UtcNow;

            foreach (IStorageItem entry in await trash.GetItemsAsync())
            {
                if (entry is not StorageFile file || file.Name == IndexFilename)
                {
                    continue;
                }

                if (!index.TryGetValue(file.Name, out DateTime deletedAt))
                {
                    deletedAt = file.DateCreated.UtcDateTime;
                    index[file.Name] = deletedAt;
                }

                if (now - deletedAt >= RetentionPeriod)
                {
                    await file.DeleteAsync();
                    index.Remove(file.Name);
                    await NoteMetadataCache.RemoveAsync(file.Name);
                    continue;
                }

                metadata.TryGetValue(file.Name, out NoteMetadata? cached);
                items.Add(new TrashItem
                {
                    Filename = file.Name,
                    Title = string.IsNullOrWhiteSpace(cached?.Title)
                        ? file.DateCreated.LocalDateTime.ToString()
                        : cached.Title,
                    Preview = cached?.Preview ?? string.Empty,
                    DateCreated = cached is not null ? DateTime.FromBinary(cached.DateBinary) : file.DateCreated.LocalDateTime,
                    DeletedAt = deletedAt,
                    RetentionText = $"Deleted {deletedAt.ToLocalTime():g} · {Math.Max(1, (int)Math.Ceiling((RetentionPeriod - (now - deletedAt)).TotalDays))} days left"
                });
            }

            await SaveIndexAsync(trash, index);
            return items.OrderByDescending(item => item.DeletedAt).ToList();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task RestoreAsync(string filename)
    {
        await Gate.WaitAsync();
        try
        {
            StorageFolder root = ApplicationData.Current.LocalFolder;
            StorageFolder trash = await root.GetFolderAsync(TrashFolderName);
            StorageFile file = await trash.GetFileAsync(filename);
            await file.MoveAsync(root, filename, NameCollisionOption.ReplaceExisting);
            Dictionary<string, DateTime> index = await LoadIndexAsync(trash);
            index.Remove(filename);
            await SaveIndexAsync(trash, index);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task PermanentlyDeleteAsync(string filename)
    {
        await Gate.WaitAsync();
        try
        {
            StorageFolder root = ApplicationData.Current.LocalFolder;
            StorageFolder trash = await root.GetFolderAsync(TrashFolderName);
            StorageFile file = await trash.GetFileAsync(filename);
            await file.DeleteAsync();
            Dictionary<string, DateTime> index = await LoadIndexAsync(trash);
            index.Remove(filename);
            await SaveIndexAsync(trash, index);
            await NoteMetadataCache.RemoveAsync(filename);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<Dictionary<string, DateTime>> LoadIndexAsync(StorageFolder trash)
    {
        try
        {
            StorageFile file = await trash.GetFileAsync(IndexFilename);
            string json = await FileIO.ReadTextAsync(file);
            return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json)
                ?? new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }
        catch (FileNotFoundException)
        {
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task SaveIndexAsync(StorageFolder trash, Dictionary<string, DateTime> index)
    {
        StorageFile file = await trash.CreateFileAsync(IndexFilename, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(index));
    }
}
