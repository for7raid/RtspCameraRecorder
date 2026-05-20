// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CameraRecorder;
public class AlarmWebServer
{
    private readonly  TcpListener? _listener;
    CancellationToken _cancellationToken;
    private readonly ILogger<AlarmWebServer> _logger;

    public AlarmWebServer(ILogger<AlarmWebServer> logger)
    {
    
        _logger = logger;
        
        _listener = new TcpListener(IPAddress.Any, 8081);
        _listener.Start();

        Thread acceptThread = new Thread(AcceptClients);
        acceptThread.Start();

        _logger.LogInformation("Started alarm web server.");
    }

    async void AcceptClients()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_cancellationToken);

                Thread clientThread = new Thread(() => HandleClient(client));
                clientThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error accepting client: " + ex.Message);
            }
        }
    }

    void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];
        int bytesRead;

        try
        {
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string jsonString = Encoding.UTF8.GetString(buffer, 0, bytesRead).Replace("\0", "");
                _logger.LogInformation(jsonString);

                var payload = JsonSerializer.Deserialize<CameraEvent>(jsonString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload?.Type == "Motion Detect")
                {
                    if (payload.Status == 1)
                    {
                        _logger.LogInformation("Detect motion on camera");
                    }
                    else if (payload.Status == 0)
                    {
                        _logger.LogInformation("Ent motion on camera");
                    }
                }

            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error communicating with client: " + ex.Message);
        }
        finally
        {
            stream.Close();
            client.Close();
            //Console.WriteLine("Client disconnected.");
        }
    }
}
sealed record CameraEvent(string Type, int Status);

/*
 "{\"Type\":\"Manual\",\"Status\":1,\"Time\":\"2026-05-06 18:25:35\",\"IP\":\"192.168.1.8\",\"DeviceName\":\"Tikhoretskiy 4k2 4 podjezd 11 etag\",\"AttachLen1\":0,\"AttachLen2\":0,\"AttachLen3\":0}\0"

{"Type":"Motion Detect","Status":0,"Time":"2026-05-06 18:31:00","IP":"192.168.1.8","DeviceName":"Tikhoretskiy 4k2 4 podjezd 11 etag","AttachLen1":0,"AttachLen2":0,"AttachLen3":0}
 */