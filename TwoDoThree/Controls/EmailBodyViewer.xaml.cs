using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using TwoDoThree.Services;

namespace TwoDoThree.Controls;

public partial class EmailBodyViewer : UserControl
{
    public static readonly DependencyProperty HtmlBodyProperty =
        DependencyProperty.Register(
            nameof(HtmlBody),
            typeof(string),
            typeof(EmailBodyViewer),
            new PropertyMetadata(string.Empty, OnBodyChanged));

    public static readonly DependencyProperty PlainTextProperty =
        DependencyProperty.Register(
            nameof(PlainText),
            typeof(string),
            typeof(EmailBodyViewer),
            new PropertyMetadata(string.Empty, OnBodyChanged));

    private bool isWebViewReady;
    private bool isWebViewUnavailable;

    public EmailBodyViewer()
    {
        InitializeComponent();
        Loaded += EmailBodyViewer_Loaded;
        BodyWebView.NavigationStarting += BodyWebView_NavigationStarting;
        BodyWebView.CoreWebView2InitializationCompleted += BodyWebView_CoreWebView2InitializationCompleted;
    }

    public string HtmlBody
    {
        get => (string)GetValue(HtmlBodyProperty);
        set => SetValue(HtmlBodyProperty, value);
    }

    public string PlainText
    {
        get => (string)GetValue(PlainTextProperty);
        set => SetValue(PlainTextProperty, value);
    }

    private async void EmailBodyViewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (isWebViewReady || isWebViewUnavailable)
        {
            RenderBody();
            return;
        }

        try
        {
            await BodyWebView.EnsureCoreWebView2Async();
        }
        catch (Exception)
        {
            isWebViewUnavailable = true;
            ShowFallback();
        }
    }

    private void BodyWebView_CoreWebView2InitializationCompleted(
        object? sender,
        CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            isWebViewUnavailable = true;
            ShowFallback();
            return;
        }

        BodyWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        BodyWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        BodyWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        isWebViewReady = true;
        RenderBody();
    }

    private void BodyWebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!ShouldOpenExternally(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        OpenExternalUri(e.Uri);
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (ShouldOpenExternally(e.Uri))
        {
            OpenExternalUri(e.Uri);
        }
    }

    private static void OnBodyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((EmailBodyViewer)dependencyObject).RenderBody();
    }

    private void RenderBody()
    {
        if (isWebViewUnavailable)
        {
            ShowFallback();
            return;
        }

        if (!isWebViewReady)
        {
            FallbackTextBox.Text = PlainText ?? string.Empty;
            return;
        }

        BodyWebView.Visibility = Visibility.Visible;
        FallbackTextBox.Visibility = Visibility.Collapsed;
        BodyWebView.NavigateToString(EmailBodyHtml.CreateDisplayDocument(HtmlBody, PlainText));
    }

    private void ShowFallback()
    {
        BodyWebView.Visibility = Visibility.Collapsed;
        FallbackTextBox.Visibility = Visibility.Visible;
        FallbackTextBox.Text = string.IsNullOrWhiteSpace(PlainText)
            ? EmailBodyHtml.ToPlainText(HtmlBody)
            : PlainText;
    }

    private static bool ShouldOpenExternally(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText)
            || uriText.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https" or "mailto" or "tel";
    }

    private static void OpenExternalUri(string uriText)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uriText)
            {
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
        }
    }
}
