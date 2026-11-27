using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StatisticsAnalysisTool.BateF;

// Bate F: janela principal com o Tracker e os Mapas do Hub dentro do app.
// O login do Discord e feito aqui dentro e ja conecta o envio ao vivo.
public sealed class BateFHubWindow : Window
{
    private const string TrackerPath = "/codex?aba=tracker";
    private const string MapasPath = "/codex?aba=avalon";
    private const string HubPath = "/perfil";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static BateFHubWindow _instance;

    private readonly WebView2 _web = new();
    private readonly TextBlock _status = new();
    private readonly Button _btnTracker;
    private readonly Button _btnMapas;
    private readonly Button _btnHub;
    private string _loginCode;
    private bool _closingAll;

    public static void Open()
    {
        try
        {
            if (_instance != null)
            {
                _instance.Show();
                _instance.Activate();
                return;
            }

            _instance = new BateFHubWindow();
            _instance.Show();
        }
        catch
        {
            // sem a janela do Hub o app continua coletando
            BateFLogin.ShowIfNeeded();
        }
    }

    private BateFHubWindow()
    {
        var bg = new SolidColorBrush(Color.FromRgb(0x0e, 0x11, 0x16));
        var bar = new SolidColorBrush(Color.FromRgb(0x15, 0x19, 0x21));

        Title = "Bate F - CHEGOU? CORRE!";
        Width = 1360;
        Height = 860;
        MinWidth = 800;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = bg;
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/sat-icon.png"));
        }
        catch
        {
            // sem icone
        }

        _btnTracker = MakeButton("⏱  Tracker", (_, _) => Go(TrackerPath));
        _btnMapas = MakeButton("🗺  Mapas", (_, _) => Go(MapasPath));
        _btnHub = MakeButton("🏰  Hub", (_, _) => Go(HubPath));
        var btnStats = MakeButton("📊  Estatísticas", (_, _) => ShowStatistics());
        var btnBack = MakeButton("←", (_, _) =>
        {
            if (_web.CoreWebView2?.CanGoBack == true)
            {
                _web.CoreWebView2.GoBack();
            }
        });

        _status.Foreground = new SolidColorBrush(Color.FromRgb(0x91, 0x9b, 0xad));
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(12, 0, 12, 0);

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(btnBack);
        left.Children.Add(_btnTracker);
        left.Children.Add(_btnMapas);
        left.Children.Add(_btnHub);
        left.Children.Add(btnStats);

        var top = new DockPanel { Background = bar, LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        top.Children.Add(left);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        top.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(_web);
        Content = root;

        Loaded += async (_, _) => await InitAsync();
        Closing += (_, e) =>
        {
            if (_closingAll)
            {
                return;
            }

            var r = MessageBox.Show(this, "Fechar o app Bate F? A coleta de fama, prata e loot para o Tracker também para.",
                "Bate F", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _closingAll = true;
            _instance = null;
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Application.Current.MainWindow?.Close();
                }
                catch
                {
                    Application.Current.Shutdown();
                }
            });
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => UpdateStatus();
        timer.Start();
        UpdateStatus();
    }

    private static Button MakeButton(string text, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(4, 6, 0, 6),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xe8, 0xe6, 0xe3)),
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x26, 0x34)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        b.Click += onClick;
        return b;
    }

    private void UpdateStatus()
    {
        var hasToken = !string.IsNullOrEmpty(BateFHub.ReadToken());
        var s = BateFHub.Status;
        _status.Text = !hasToken ? "Entre com o Discord para conectar" : string.IsNullOrEmpty(s) ? "Conectado - esperando o jogo" : s;
    }

    private async Task InitAsync()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BateF", "WebView2");
            Directory.CreateDirectory(folder);
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                // links externos abrem no navegador normal
                e.Handled = true;
                BateFLogin.OpenUrl(e.Uri);
            };

            if (string.IsNullOrEmpty(BateFHub.ReadToken()))
            {
                _loginCode = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                _web.CoreWebView2.Navigate(BateFHub.HubUrl + "/app-login?c=" + _loginCode);
                _ = WaitLoginAsync();
            }
            else
            {
                Go(TrackerPath);
            }
        }
        catch
        {
            // WebView2 indisponivel: usa o login pelo navegador
            MessageBox.Show(this, "Não consegui abrir o Hub dentro do app. Vou abrir o login no navegador.", "Bate F");
            BateFLogin.ShowIfNeeded();
        }
    }

    private async Task WaitLoginAsync()
    {
        var code = _loginCode;
        var limit = DateTime.UtcNow.AddMinutes(30);
        while (DateTime.UtcNow < limit && !_closingAll)
        {
            await Task.Delay(2500);
            try
            {
                var json = await Client.GetStringAsync(BateFHub.HubUrl + "/api/app-token?c=" + code);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } token)
                {
                    BateFHub.SaveToken(token);
                    UpdateStatus();
                    Go(TrackerPath);
                    return;
                }
            }
            catch
            {
                // tenta de novo
            }
        }
    }

    private void Go(string path)
    {
        try
        {
            _web.CoreWebView2?.Navigate(BateFHub.HubUrl + path);
        }
        catch
        {
            // ignorado
        }
    }

    private static void ShowStatistics()
    {
        try
        {
            var w = Application.Current.MainWindow;
            if (w == null)
            {
                return;
            }

            w.Show();
            if (w.WindowState == WindowState.Minimized)
            {
                w.WindowState = WindowState.Normal;
            }

            w.Activate();
        }
        catch
        {
            // ignorado
        }
    }
}
