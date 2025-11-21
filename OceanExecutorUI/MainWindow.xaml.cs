using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Fleck;

namespace OceanExecutorUI;

public partial class MainWindow : Window
{
    private WebSocketServer? _server;
    private readonly List<ClientConnection> _clients = new();
    private bool _isExecuting = false;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => StartServer();
        Closing += (_, _) => StopServer();
    }

    private void StartServer()
    {
        try
        {
            _server = new WebSocketServer("ws://0.0.0.0:61417");
            _server.Start(socket => ConfigureConnection(socket));
            UpdateStatus(true, "Server running");
            AppendLog("[System] Server started on ws://localhost:61417");
            ShowNotification("Server Connected", "WebSocket server is listening", NotificationType.Success);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to start WebSocket server: {ex.Message}", "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateStatus(false, "Server failed");
            RunButton.IsEnabled = false;
            ShowNotification("Error", "Failed to start server", NotificationType.Error);
        }
    }

    private void StopServer()
    {
        foreach (var client in _clients.ToList())
        {
            try
            {
                client.Socket.Close();
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        _server?.Dispose();
        _clients.Clear();
    }

    private void ConfigureConnection(IWebSocketConnection socket)
    {
        var client = new ClientConnection(socket);

        socket.OnOpen = () =>
        {
            _clients.Add(client);
            AppendLog("[System] Client connected");
            RefreshClientStatus();
            UpdateRunButtonState();
        };

        socket.OnClose = () =>
        {
            _clients.Remove(client);
            AppendLog($"[System] Client {client.DisplayName} disconnected");
            RefreshClientStatus();
            UpdateRunButtonState();
        };

        socket.OnError = ex =>
        {
            AppendLog($"[Error] WebSocket error - {ex.Message}");
            ShowNotification("Connection Error", ex.Message, NotificationType.Error);
        };

        socket.OnMessage = message => HandleClientMessage(client, message);
    }

    private void HandleClientMessage(ClientConnection client, string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var op = root.GetProperty("op").GetString() ?? string.Empty;
            var data = root.TryGetProperty("data", out var dataProp) ? dataProp : (JsonElement?)null;

            switch (op)
            {
                case "client/identify":
                    var name = data?.TryGetProperty("player", out var playerProp) == true &&
                               playerProp.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString()
                        : null;
                    client.DisplayName = string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
                    AppendLog($"[System] Client identified as {client.DisplayName}");
                    RefreshClientStatus();
                    ShowNotification("Client Attached", $"{client.DisplayName} connected", NotificationType.Success);
                    break;

                case "client/console/print":
                    if (data?.TryGetProperty("message", out var printMsg) == true)
                        AppendLog($"{client.DisplayName}: {printMsg.GetString()}");
                    break;

                case "client/console/info":
                    if (data?.TryGetProperty("message", out var infoMsg) == true)
                        AppendLog($"[Info] {client.DisplayName}: {infoMsg.GetString()}");
                    break;

                case "client/console/warn":
                    if (data?.TryGetProperty("message", out var warnMsg) == true)
                        AppendLog($"[Warning] {client.DisplayName}: {warnMsg.GetString()}");
                    break;

                case "client/console/error":
                    if (data?.TryGetProperty("message", out var errorMsg) == true)
                        AppendLog($"[Error] {client.DisplayName}: {errorMsg.GetString()}");
                    break;

                default:
                    AppendLog($"[{client.DisplayName}] {op}");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[Warning] Failed to parse message - {ex.Message}");
        }
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isExecuting) return;

        if (!_clients.Any())
        {
            ShowNotification("No Clients", "No clients are connected", NotificationType.Error);
            return;
        }

        _isExecuting = true;
        RunButton.IsEnabled = false;
        RunButton.Content = "⏳ Executing...";

        var scriptText = ScriptTextBox.Text ?? string.Empty;
        var payload = new
        {
            op = "client/onDidTextDocumentExecute",
            data = new
            {
                textDocument = new { text = scriptText }
            }
        };

        var json = JsonSerializer.Serialize(payload);

        foreach (var client in _clients.ToList())
        {
            try
            {
                client.Socket.Send(json);
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] Failed to send to {client.DisplayName} - {ex.Message}");
                ShowNotification("Execution Error", $"Failed to send to {client.DisplayName}", NotificationType.Error);
            }
        }

        AppendLog("[System] Script executed on all clients");
        ShowNotification("Script Executed", "Your script was sent to all clients", NotificationType.Success);

        System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                _isExecuting = false;
                RunButton.IsEnabled = _clients.Any();
                RunButton.Content = "▶ Execute";
            });
        });
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        OutputTextBox.Clear();
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            if (tag == "scripts")
            {
                AnimateViewChange(SettingsPanel, false);
            }
            else if (tag == "settings")
            {
                AnimateViewChange(SettingsPanel, true);
            }
        }
    }

    private void AnimateViewChange(UIElement targetElement, bool show)
    {
        var animation = new DoubleAnimation
        {
            From = show ? 0 : 1,
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (show)
        {
            targetElement.Visibility = Visibility.Visible;
        }

        targetElement.BeginAnimation(OpacityProperty, animation);

        if (!show)
        {
            animation.Completed += (_, _) => targetElement.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshClientStatus()
    {
        var statusText = _clients.Count switch
        {
            0 => "Waiting for clients",
            1 => $"{_clients[0].DisplayName} connected",
            _ => $"{_clients.Count} clients connected"
        };

        UpdateStatus(_clients.Any(), statusText);
    }

    private void UpdateStatus(bool isConnected, string text)
    {
        Dispatcher.Invoke(() =>
        {
            ClientCountText.Text = text;
            SettingsStatusText.Text = isConnected ? "Running" : "Waiting...";
            StatusIndicator.Fill = new SolidColorBrush(isConnected ? Colors.Green : Colors.Orange);
        });
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            OutputTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            OutputTextBox.ScrollToEnd();
        });
    }

    private void UpdateRunButtonState()
    {
        Dispatcher.Invoke(() => RunButton.IsEnabled = _clients.Any() && !_isExecuting);
    }

    private void ShowNotification(string title, string message, NotificationType type)
    {
        Dispatcher.Invoke(() =>
        {
            var notification = CreateNotificationElement(title, message, type);
            NotificationCanvas.Children.Add(notification);

            var enterAnimation = new DoubleAnimation(300, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Canvas.SetRight(notification, 0);

            notification.BeginAnimation(Canvas.RightProperty, enterAnimation);

            System.Threading.Tasks.Task.Delay(4000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    var exitAnimation = new DoubleAnimation(0, 300, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };

                    exitAnimation.Completed += (_, _) => NotificationCanvas.Children.Remove(notification);
                    notification.BeginAnimation(Canvas.RightProperty, exitAnimation);
                });
            });
        });
    }

    private Border CreateNotificationElement(string title, string message, NotificationType type)
    {
        var backgroundColor = type switch
        {
            NotificationType.Success => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
            NotificationType.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"))
        };

        var notification = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
            BorderBrush = backgroundColor,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Width = 280,
            Margin = new Thickness(0, 0, 20, 20)
        };

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        });

        content.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        notification.Child = content;

        Canvas.SetRight(notification, 300);
        Canvas.SetBottom(notification, 20);

        return notification;
    }

    private sealed class ClientConnection
    {
        public ClientConnection(IWebSocketConnection socket)
        {
            Socket = socket;
            DisplayName = "Client";
        }

        public IWebSocketConnection Socket { get; }
        public string DisplayName { get; set; }
    }
}

public enum NotificationType
{
    Info,
    Success,
    Error
}
