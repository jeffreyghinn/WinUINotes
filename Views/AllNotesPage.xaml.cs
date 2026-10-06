using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinUINotes.Models;
using WinUINotes.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUINotes.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class AllNotesPage : Page
{
    private const string SortFieldSettingKey = "NotesSortField";
    private const string SortAscendingSettingKey = "NotesSortAscending";
    private AllNotes notesModel = new AllNotes();
    private bool hasLoadedNotes;
    private bool hasCleanedExpiredTrash;
    private bool isPro;
    private bool isInitializingSortControls;
    private int sortField = 1;
    private bool sortAscending;

    public AllNotesPage()
    {
        NavigationCacheMode = NavigationCacheMode.Enabled;
        DataContext = notesModel;
        InitializeComponent();
        NotesView.ItemsSource = notesModel.Notes;
        LoadSortPreferences();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (FeatureFlags.ProFeaturesEnabled && !hasCleanedExpiredTrash)
        {
            try
            {
                await TrashService.LoadItemsAsync();
                hasCleanedExpiredTrash = true;
            }
            catch (Exception)
            {
                // A cleanup failure should not prevent access to the notes list.
            }
        }

        foreach (Note note in notesModel.Notes
                     .Where(note => note.IsDeleted || (!note.IsSaved && !note.HasChanges))
                     .ToList())
        {
            notesModel.Notes.Remove(note);
        }

        if (!hasLoadedNotes)
        {
            try
            {
                await notesModel.LoadNotesAsync();
                hasLoadedNotes = true;
            }
            catch (Exception)
            {
                ContentDialog errorDialog = new()
                {
                    Title = "Couldn't load notes",
                    Content = "WinUINotes couldn't read your notes. Return to this page to try again.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                await errorDialog.ShowAsync();
            }
        }

        bool isPro = await ProLicense.HasProAsync();
        ApplyProLicense(isPro);
    }

    public void ApplyProLicense(bool isPro)
    {
        this.isPro = isPro;
        SortControls.Visibility = isPro ? Visibility.Visible : Visibility.Collapsed;
        foreach (Note note in notesModel.Notes)
        {
            note.SetProEnabled(isPro);
        }

        if (isPro)
        {
            SortNotes();
        }
    }

    private void LoadSortPreferences()
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        if (settings.TryGetValue(SortFieldSettingKey, out object? savedField) && savedField is int field && field is >= 0 and <= 2)
        {
            sortField = field;
        }

        if (settings.TryGetValue(SortAscendingSettingKey, out object? savedAscending) && savedAscending is bool ascending)
        {
            sortAscending = ascending;
        }

        isInitializingSortControls = true;
        SortByComboBox.SelectedIndex = sortField;
        UpdateSortDirectionLabel();
        isInitializingSortControls = false;
    }

    private void SortByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isInitializingSortControls || SortByComboBox.SelectedIndex < 0)
        {
            return;
        }

        sortField = SortByComboBox.SelectedIndex;
        SaveSortPreferences();
        SortNotes();
    }

    private void SortDirectionToggle_Click(object sender, RoutedEventArgs e)
    {
        if (isInitializingSortControls)
        {
            return;
        }

        sortAscending = !sortAscending;
        UpdateSortDirectionLabel();
        SaveSortPreferences();
        SortNotes();
    }

    private void UpdateSortDirectionLabel()
    {
        string label = sortAscending ? "Ascending" : "Descending";
        SortDirectionIcon.Text = sortAscending ? "↑" : "↓";
        string description = $"Sort {label.ToLowerInvariant()}";
        AutomationProperties.SetName(SortDirectionToggle, description);
        ToolTipService.SetToolTip(SortDirectionToggle, description);
    }

    private void SaveSortPreferences()
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        settings[SortFieldSettingKey] = sortField;
        settings[SortAscendingSettingKey] = sortAscending;
    }

    private void SortNotes()
    {
        if (isPro && notesModel.Notes.Count > 1)
        {
            IOrderedEnumerable<Note> ordered = sortField switch
            {
                0 => sortAscending
                    ? notesModel.Notes.OrderBy(note => note.ListTitle, StringComparer.CurrentCultureIgnoreCase)
                    : notesModel.Notes.OrderByDescending(note => note.ListTitle, StringComparer.CurrentCultureIgnoreCase),
                2 => sortAscending
                    ? notesModel.Notes.OrderBy(note => note.LastEdited)
                    : notesModel.Notes.OrderByDescending(note => note.LastEdited),
                _ => sortAscending
                    ? notesModel.Notes.OrderBy(note => note.Date)
                    : notesModel.Notes.OrderByDescending(note => note.Date)
            };

            List<Note> sortedNotes = ordered
                .ThenBy(note => note.Filename, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int targetIndex = 0; targetIndex < sortedNotes.Count; targetIndex++)
            {
                if (ReferenceEquals(notesModel.Notes[targetIndex], sortedNotes[targetIndex]))
                {
                    continue;
                }

                int currentIndex = notesModel.Notes.IndexOf(sortedNotes[targetIndex]);
                if (currentIndex >= 0)
                {
                    notesModel.Notes.Move(currentIndex, targetIndex);
                }
            }
        }

        // ItemsView can retain stale realized-item mappings after ObservableCollection.Move.
        // Reassigning the source refreshes its item-to-data mapping along with the visible order.
        NotesView.ItemsSource = null;
        NotesView.ItemsSource = notesModel.Notes;
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        Note note = new();
        notesModel.Notes.Insert(0, note);
        SortNotes();
        Frame.Navigate(typeof(NotePage), note);
    }

    private void ItemsView_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        Frame.Navigate(typeof(NotePage), args.InvokedItem);
    }

    private async void DeleteNoteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem menuItem || menuItem.CommandParameter is not string filename)
        {
            return;
        }

        Note? note = notesModel.Notes.FirstOrDefault(item => item.Filename == filename);
        if (note is null)
        {
            return;
        }

        ContentDialog confirmationDialog = new()
        {
            Title = "Delete this note?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await confirmationDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await note.DeleteAsync(moveToTrash: isPro);
        notesModel.Notes.Remove(note);
    }
}
