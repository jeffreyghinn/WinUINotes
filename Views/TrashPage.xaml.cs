using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using WinUINotes.Services;

namespace WinUINotes.Views;

public sealed partial class TrashPage : Page
{
    private readonly ObservableCollection<TrashItem> items = new();

    public TrashPage()
    {
        InitializeComponent();
        TrashList.ItemsSource = items;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        items.Clear();
        foreach (TrashItem item in await TrashService.LoadItemsAsync())
        {
            items.Add(item);
        }

        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string filename })
        {
            await TrashService.RestoreAsync(filename);
            await RefreshAsync();
        }
    }

    private async void DeletePermanentlyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string filename })
        {
            return;
        }

        ContentDialog confirmationDialog = new()
        {
            Title = "Delete this note permanently?",
            Content = "This note can't be restored after it's deleted.",
            PrimaryButtonText = "Delete permanently",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await confirmationDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await TrashService.PermanentlyDeleteAsync(filename);
            await RefreshAsync();
        }
    }
}
