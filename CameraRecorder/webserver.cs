// See https://aka.ms/new-console-template for more information
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CameraRecorder;
public class webserver
{
    TcpListener? listener;

    CancellationToken _cancellationToken;

    public static void Main()
    {
        Console.WriteLine("Hello, World!");

        //Rec();
        string hostName = Dns.GetHostName(); // Get the name of the host
        IPAddress[] addresses = Dns.GetHostAddresses(hostName); // Get all IP addresses for this host

        // Filter to find the first IPv4 address that isn't a loopback (127.0.0.1)
        var localIPv4 = addresses.Where(ip =>
            ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));

        Console.WriteLine("Local IP Address: " + string.Join(", ", localIPv4));

        //var instance = new CameraRecorder();
        //instance.StartTCP();

        //Console.WriteLine("Press Enter to Exit...");
        //Console.ReadLine();
        //instance.StopRecording();

        //var recoder = new RtspRecorder();
        //var tokenSource = new CancellationTokenSource();
        //var token = tokenSource.Token;
        //var outputPath = Path.Combine("c:\\temp", $"record-{DateTime.Now:yyyyMMdd HH-mm-ss}.mkv");

        //recoder.Record("rtsp://192.168.1.8:554/stream1", outputPath, token);

        Console.WriteLine("Started");

        Console.ReadLine();
        //tokenSource.Cancel();



    }


    void StartTCP(int port = 6001)
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine("Server started, waiting for connections...");

        Thread acceptThread = new Thread(AcceptClients);
        acceptThread.Start();
    }

    async void AcceptClients()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(_cancellationToken);

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
                Console.WriteLine(jsonString);
                var payload = JsonSerializer.Deserialize<CameraEvent>(jsonString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                //if (payload?.Type == "Motion Detect")
                {
                    if (payload.Status == 1)
                    {
                        Console.WriteLine("Start recording");
                        //StartRecording("rtsp://192.168.1.8:554/stream1", $@"c:\temp\camera\{DateTime.Now:yyyy-MM-dd HH.mm.ss}_%03d.mp4");
                    }
                    else if (payload.Status == 0)
                    {
                        Console.WriteLine("Stop recording");
                        //StopRecording();
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