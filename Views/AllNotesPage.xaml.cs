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

    public AllNotesPage()
    {
        DataContext = notesModel;
        InitializeComponent();
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(NotePage));
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
