using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading;
using System.Threading.Tasks;
using WinUINotes.Models;
using WinUINotes.Services;
using Windows.Services.Store;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUINotes.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class NotePage : Page
    {
        private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(800);
        private Note? noteModel;
        private CancellationTokenSource? saveCancellation;
        private Task? autoSaveTask;
        private long editVersion;
        private bool isLoading = true;
        private bool isDeleting;
        private bool noteContentReady;
        private bool isPro;

        public NotePage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            isLoading = true;

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
                    SaveStatusText.Text = "Couldn't open note. Go back and try again.";
                    return;
                }
            }

            NoteEditor.Text = noteModel.Text;
            CreatedDateText.Text = noteModel.Date.ToString();
            isPro = await ProLicense.HasProAsync();
            TitleEditor.Visibility = isPro ? Visibility.Visible : Visibility.Collapsed;
            CreatedDateText.Visibility = isPro ? Visibility.Collapsed : Visibility.Visible;
            TitleEditor.Text = noteModel.DisplayTitle;
            NoteEditor.IsEnabled = true;
            SaveStatusText.Text = "Changes save automatically";
            isLoading = false;
            noteContentReady = true;
            QueueEditorFocus();
        }

        private void NoteEditor_Loaded(object sender, RoutedEventArgs e)
        {
            QueueEditorFocus();
        }

        public void ApplyProLicense(bool hasPro)
        {
            isPro = hasPro;
            TitleEditor.Visibility = hasPro ? Visibility.Visible : Visibility.Collapsed;
            CreatedDateText.Visibility = hasPro ? Visibility.Collapsed : Visibility.Visible;
        }

        private void QueueEditorFocus()
        {
            if (!noteContentReady)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (noteContentReady && NoteEditor.IsLoaded && NoteEditor.IsEnabled)
                {
                    NoteEditor.Focus(FocusState.Programmatic);
                    NoteEditor.SelectionStart = NoteEditor.Text.Length;
                }
            });
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            saveCancellation?.Cancel();
            if (!isDeleting && noteModel is { HasChanges: true } note)
            {
                if (autoSaveTask is not null)
                {
                    await autoSaveTask;
                }

                if (note.HasChanges)
                {
                    await SaveChangesAsync(note, editVersion);
                }
            }
        }

        private void NoteEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isLoading || noteModel is null)
            {
                return;
            }

            noteModel.Text = NoteEditor.Text;
            MarkChanged();
        }

        private void TitleEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isLoading || !isPro || noteModel is null)
            {
                return;
            }

            noteModel.Title = TitleEditor.Text;
            MarkChanged();
        }

        private async void TitleEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            await SaveOnLostFocusAsync();
        }

        private void MarkChanged()
        {
            noteModel!.HasChanges = true;
            editVersion++;
            SaveStatusText.Text = "Unsaved changes";
            ScheduleAutoSave();
        }

        private async void NoteEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            await SaveOnLostFocusAsync();
        }

        private async Task SaveOnLostFocusAsync()
        {
            if (!isLoading && noteModel is { HasChanges: true } note)
            {
                saveCancellation?.Cancel();
                if (autoSaveTask is not null)
                {
                    await autoSaveTask;
                }

                if (note.HasChanges)
                {
                    autoSaveTask = SaveChangesAsync(note, editVersion);
                }
            }
        }

        private void ScheduleAutoSave()
        {
            saveCancellation?.Cancel();
            saveCancellation?.Dispose();
            saveCancellation = new CancellationTokenSource();
            autoSaveTask = SaveAfterPauseAsync(noteModel!, editVersion, saveCancellation.Token);
        }

        private async Task SaveAfterPauseAsync(Note note, long version, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(SaveDelay, cancellationToken);
                await SaveChangesAsync(note, version);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task SaveChangesAsync(Note note, long version)
        {
            SaveStatusText.Text = "Saving…";
            try
            {
                await note.SaveAsync();
                if (version == editVersion)
                {
                    note.HasChanges = false;
                    SaveStatusText.Text = "All changes saved";
                }
                else
                {
                    SaveStatusText.Text = "Unsaved changes";
                }
            }
            catch (Exception)
            {
                SaveStatusText.Text = "Couldn't save. Changes will remain here; edit again to retry.";
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

            isDeleting = true;
            saveCancellation?.Cancel();
            if (autoSaveTask is not null)
            {
                await autoSaveTask;
            }

            await noteModel.DeleteAsync(moveToTrash: isPro);

            if (Frame.CanGoBack == true)
            {
                Frame.GoBack();
            }
        }
    }
}
