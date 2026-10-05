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
using Windows.Storage;
using WinUINotes.Models;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUINotes.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class NotePage : Page
    {
        private Note? noteModel;

        public NotePage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Note note)
            {
                noteModel = note;
            }
            else
            {
                noteModel = new Note();
            }

            if (noteModel is { IsContentLoaded: false })
            {
                NoteEditor.IsEnabled = false;
                try
                {
                    await noteModel.LoadContentAsync();
                }
                catch (Exception)
                {
                    ContentDialog errorDialog = new()
                    {
                        Title = "Couldn't open note",
                        Content = "WinUINotes couldn't read this note. You can go back and try again.",
                        CloseButtonText = "OK",
                        XamlRoot = XamlRoot
                    };

                    await errorDialog.ShowAsync();
                }
                finally
                {
                    NoteEditor.IsEnabled = true;
                }
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (noteModel is not null)
            {
                await noteModel.SaveAsync();
            }
        }

        private async void SaveAndCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (noteModel is not null)
            {
                await noteModel.SaveAsync();
            }

            if (Frame.CanGoBack == true)
            {
                Frame.GoBack();
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (noteModel is null)
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

            await noteModel.DeleteAsync();

            if (Frame.CanGoBack == true)
            {
                Frame.GoBack();
            }
        }
    }
}
