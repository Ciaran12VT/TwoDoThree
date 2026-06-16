using System.Windows;
using Microsoft.Identity.Client;
using TwoDoThree.Models;
using TwoDoThree.Services;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly IAppSettingsStore settingsStore;
    private readonly IGraphAuthService graphAuthService;
    private readonly IEmailCacheStore emailCacheStore;

    public SettingsWindow(
        AppSettings settings,
        IAppSettingsStore settingsStore,
        IGraphAuthService graphAuthService,
        IEmailCacheStore emailCacheStore)
    {
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.graphAuthService = graphAuthService;
        this.emailCacheStore = emailCacheStore;
        InitializeComponent();
        DataContext = new SettingsViewModel(settings);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        try
        {
            if (viewModel.Email.Source == EmailSource.ClassicOutlook)
            {
                viewModel.ConnectionStatus = ClassicOutlookEmailProvider.IsClassicOutlookAvailable()
                    ? "Classic Outlook is available. No Microsoft Graph sign-in is required."
                    : "Classic Outlook is not installed or is not registered for COM automation.";
                return;
            }

            if (viewModel.Email.Source == EmailSource.ManualImport)
            {
                viewModel.ConnectionStatus = "Manual import does not require sign-in.";
                return;
            }

            viewModel.ConnectionStatus = "Opening Microsoft sign-in...";
            var result = await graphAuthService.AcquireTokenInteractiveAsync(
                viewModel.CreateEmailSettingsSnapshot(),
                this,
                CancellationToken.None);
            viewModel.Email.AccountAddress = result.Account?.Username ?? viewModel.Email.AccountAddress;
            viewModel.ConnectionStatus = $"Connected as {viewModel.Email.AccountAddress}.";
        }
        catch (InvalidOperationException ex)
        {
            viewModel.ConnectionStatus = ex.Message;
        }
        catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
        {
            viewModel.ConnectionStatus = "Sign-in cancelled.";
        }
        catch (MsalException ex)
        {
            viewModel.ConnectionStatus = $"Sign-in failed: {ex.Message}";
        }
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        try
        {
            if (viewModel.Email.Source == EmailSource.ClassicOutlook)
            {
                viewModel.ConnectionStatus = "Testing Classic Outlook...";
                var messages = ClassicOutlookEmailProvider.ReadInbox(1, CancellationToken.None);
                viewModel.ConnectionStatus = $"Classic Outlook connection OK. Found {messages.Count} recent email{(messages.Count == 1 ? string.Empty : "s")}.";
                return;
            }

            if (viewModel.Email.Source == EmailSource.ManualImport)
            {
                viewModel.ConnectionStatus = "Manual import is ready. Import .eml or .msg files from the main Email panel.";
                return;
            }

            viewModel.ConnectionStatus = "Testing Outlook connection...";
            var result = await graphAuthService.TryAcquireTokenSilentAsync(
                viewModel.CreateEmailSettingsSnapshot(),
                CancellationToken.None);
            viewModel.ConnectionStatus = result is null
                ? "Sign in first, then test the connection again."
                : $"Token acquired for {result.Account?.Username ?? "Outlook"}.";
        }
        catch (InvalidOperationException ex)
        {
            viewModel.ConnectionStatus = ex.Message;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            viewModel.ConnectionStatus = $"Classic Outlook test failed: {ex.Message}";
        }
        catch (MsalException ex)
        {
            viewModel.ConnectionStatus = $"Connection test failed: {ex.Message}";
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        if (viewModel.Email.Source == EmailSource.MicrosoftGraph)
        {
            await graphAuthService.SignOutAsync(viewModel.CreateEmailSettingsSnapshot(), CancellationToken.None);
        }

        emailCacheStore.Clear();
        viewModel.ConnectionStatus = "Cleared cached emails.";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.Apply();
            settingsStore.Save(settings);
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
