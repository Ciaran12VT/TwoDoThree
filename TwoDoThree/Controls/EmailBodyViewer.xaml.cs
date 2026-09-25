using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TwoDoThree.Services;

namespace TwoDoThree.Controls;

public partial class EmailBodyViewer : UserControl
{
    // MainWindow opts into native text rendering; task Resources retain composition
    // so their ancestor can receive file drops over the browser surface.
    public static readonly DependencyProperty UseNativeRendererProperty = DependencyProperty.Register(
        nameof(UseNativeRenderer), typeof(bool), typeof(EmailBodyViewer),
        new PropertyMetadata(false, OnRendererChanged));

    public bool UseNativeRenderer
    {
        get => (bool)GetValue(UseNativeRendererProperty);
        set => SetValue(UseNativeRendererProperty, value);
    }

    private IWebView2 EmailBrowser => UseNativeRenderer ? NativeBodyWebView : BodyWebView;

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
        // Native and composition controllers cannot share the default environment.
        // Keep this profile separate from Resources and from native Markdown.
        NativeBodyWebView.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TwoDoThree", "WebView2", "NativeEmail")
        };
        Loaded += EmailBodyViewer_Loaded;
        BodyWebView.NavigationStarting += BodyWebView_NavigationStarting;
        BodyWebView.CoreWebView2InitializationCompleted += BodyWebView_CoreWebView2InitializationCompleted;
        NativeBodyWebView.NavigationStarting += BodyWebView_NavigationStarting;
        NativeBodyWebView.CoreWebView2InitializationCompleted += BodyWebView_CoreWebView2InitializationCompleted;
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

    private static void OnRendererChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var viewer = (EmailBodyViewer)owner;
        viewer.BodyWebView.AllowDrop = !viewer.UseNativeRenderer;
        viewer.BodyWebView.Visibility = viewer.UseNativeRenderer ? Visibility.Collapsed : Visibility.Visible;
        viewer.NativeBodyWebView.Visibility = viewer.UseNativeRenderer ? Visibility.Visible : Visibility.Collapsed;
        viewer.FallbackTextBox.AllowDrop = !viewer.UseNativeRenderer;
        viewer.isWebViewReady = viewer.EmailBrowser.CoreWebView2 is not null;
        viewer.isWebViewUnavailable = false;
        if (viewer.IsLoaded) viewer.EmailBodyViewer_Loaded(viewer, new RoutedEventArgs());
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
            await EmailBrowser.EnsureCoreWebView2Async();
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
        if (!ReferenceEquals(sender, EmailBrowser)) return;
        if (!e.IsSuccess)
        {
            isWebViewUnavailable = true;
            ShowFallback();
            return;
        }

        EmailBrowser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        EmailBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        EmailBrowser.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
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

        ((FrameworkElement)EmailBrowser).Visibility = Visibility.Visible;
        FallbackTextBox.Visibility = Visibility.Collapsed;
        EmailBrowser.NavigateToString(EmailBodyHtml.CreateDisplayDocument(HtmlBody, PlainText));
    }

    private void ShowFallback()
    {
        BodyWebView.Visibility = Visibility.Collapsed;
        NativeBodyWebView.Visibility = Visibility.Collapsed;
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
