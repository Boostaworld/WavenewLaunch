using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Fleck;

namespace OceanExecutorUI;

public partial class MainWindow : Window
{
    private WebSocketServer? _server;
    private readonly List<ClientConnection> _clients = new();

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
            UpdateStatus("Listening on ws://localhost:61417");
            AppendLog("[Server started]");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to start WebSocket server: {ex.Message}", "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateStatus("Server failed to start");
            RunButton.IsEnabled = false;
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
            AppendLog("[Client connected]");
            RefreshClientStatus();
            UpdateRunButtonState();
        };

        socket.OnClose = () =>
        {
            _clients.Remove(client);
            AppendLog($"[Client {client.DisplayName} disconnected]");
            RefreshClientStatus();
            UpdateRunButtonState();
        };

        socket.OnError = ex =>
        {
            AppendLog($"ERROR: WebSocket error - {ex.Message}");
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
                    AppendLog($"[Client {client.DisplayName} identified]");
                    RefreshClientStatus();
                    break;

                case "client/console/print":
                case "client/console/info":
                case "client/console/warn":
                    if (data?.TryGetProperty("message", out var infoMessage) == true)
                    {
                        AppendLog($"{client.DisplayName}: {infoMessage.GetString()}");
                    }
                    break;

                case "client/console/error":
                    if (data?.TryGetProperty("message", out var errorMessage) == true)
                    {
                        AppendLog($"ERROR [{client.DisplayName}]: {errorMessage.GetString()}");
                    }
                    break;

                default:
                    AppendLog($"[{client.DisplayName}] {op}: {message}");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"WARN: Failed to parse message - {ex.Message}");
        }
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_clients.Any())
        {
            MessageBox.Show(this, "No clients are connected.", "Run", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
                AppendLog($"ERROR: Failed to send to {client.DisplayName} - {ex.Message}");
            }
        }

        AppendLog("[Script dispatched]");
    }

    private void RefreshClientStatus()
    {
        var statusText = _clients.Count switch
        {
            0 => "Waiting for clients...",
            1 => $"Connected: {_clients[0].DisplayName}",
            _ => $"Connected clients: {_clients.Count}"
        };

        UpdateStatus(statusText);
    }

    private void UpdateStatus(string text)
    {
        Dispatcher.Invoke(() => ConnectionStatusText.Text = text);
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            OutputTextBox.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
            OutputTextBox.ScrollToEnd();
        });
    }

    private void UpdateRunButtonState()
    {
        Dispatcher.Invoke(() => RunButton.IsEnabled = _clients.Any());
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
