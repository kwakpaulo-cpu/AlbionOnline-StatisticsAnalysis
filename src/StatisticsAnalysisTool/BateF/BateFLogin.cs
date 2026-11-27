using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StatisticsAnalysisTool.BateF;

// Bate F: login com o Discord pelo navegador. O app recebe o codigo pessoal sozinho e salva em %APPDATA%\BateF.
public static class BateFLogin
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static void ShowIfNeeded()
    {
        try
        {
            if (!string.IsNullOrEmpty(BateFHub.ReadToken()))
            {
                return;
            }

            CreateWindow().Show();
        }
        catch
        {
            // o app continua funcionando sem o login
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignorado
        }
    }

    private static Window CreateWindow()
    {
        var bg = new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1b));
        var fg = new SolidColorBrush(Color.FromRgb(0xe8, 0xe6, 0xe3));
        var muted = new SolidColorBrush(Color.FromRgb(0x91, 0x9b, 0xad));
        var accent = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xf2));

        var window = new Window
        {
            Title = "Bate F - Entrar",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = bg,
            Topmost = true
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "BATE F - CHEGOU? CORRE!",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Entre com o seu Discord para o app mandar a sua fama, prata e loot para o seu Tracker no Hub.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            Margin = new Thickness(0, 12, 0, 16),
            TextAlignment = TextAlignment.Center
        });

        var status = new TextBlock
        {
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var login = new Button
        {
            Content = "Entrar com Discord",
            Padding = new Thickness(16, 10, 16, 10),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Background = accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var skip = new Button
        {
            Content = "Agora não",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 10, 0, 0),
            Background = Brushes.Transparent,
            Foreground = muted,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        skip.Click += (_, _) => window.Close();

        var waiting = false;
        login.Click += async (_, _) =>
        {
            if (waiting)
            {
                return;
            }

            waiting = true;
            login.IsEnabled = false;
            var code = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            OpenUrl(BateFHub.HubUrl + "/app-login?c=" + code);
            status.Text = "Termine o login no navegador que abriu...";

            var token = await WaitTokenAsync(code, window);
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    BateFHub.SaveToken(token);
                    status.Foreground = new SolidColorBrush(Color.FromRgb(0x7e, 0xe7, 0x87));
                    status.Text = "Conectado! Pode jogar: os dados vão para o seu Tracker.";
                    login.Content = "Abrir meu Tracker";
                    login.IsEnabled = true;
                    login.Click += (_, _) => OpenUrl(BateFHub.HubUrl + "/codex?aba=tracker");
                    skip.Content = "Fechar";
                    return;
                }
                catch
                {
                    status.Text = "Não consegui salvar o login. Tente de novo.";
                }
            }
            else if (window.IsLoaded)
            {
                status.Text = "O login não terminou. Clique de novo para tentar.";
            }

            waiting = false;
            login.IsEnabled = true;
        };

        panel.Children.Add(login);
        panel.Children.Add(status);
        panel.Children.Add(skip);
        window.Content = panel;
        return window;
    }

    private static async Task<string> WaitTokenAsync(string code, Window window)
    {
        var limit = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < limit && window.IsLoaded)
        {
            await Task.Delay(2500);
            try
            {
                var json = await Client.GetStringAsync(BateFHub.HubUrl + "/api/app-token?c=" + code);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } token)
                {
                    return token;
                }
            }
            catch
            {
                // tenta de novo
            }
        }

        return null;
    }
}
