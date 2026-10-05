using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUINotes.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class AllNotesPage : Page
{
    private AllNotes notesModel = new AllNotes();
    private bool hasLoadedNotes;

    public AllNotesPage()
    {
        NavigationCacheMode = NavigationCacheMode.Enabled;
        DataContext = notesModel;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        foreach (Note draft in notesModel.Notes.Where(note => !note.IsSaved).ToList())
        {
            notesModel.Notes.Remove(draft);
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
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        Note note = new();
        notesModel.Notes.Insert(0, note);
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

        await note.DeleteAsync();
        notesModel.Notes.Remove(note);
    }
}
