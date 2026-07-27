using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NAudio.Wave;

namespace CallApp
{
    class Program
    {
        private static WaveInEvent? waveIn;
        private static WaveOutEvent? waveOut;
        private static BufferedWaveProvider? waveProvider;
        private static UdpClient? udpSender;
        private static UdpClient? udpReceiver;
        private static bool isRunning = true;

        // 16kHz, 16-bit Mono Audio Format (Low-latency voice quality)
        private static readonly WaveFormat VoiceFormat = new WaveFormat(16000, 16, 1);

        static async Task Main(string[] args)
        {
            Console.Title = "CallApp - P2P Voice Streamer";
            Console.WriteLine("========================================");
            Console.WriteLine("        CALLAPP - P2P VOICE CHAT        ");
            Console.WriteLine("========================================\n");

            Console.Write("Enter YOUR Local Port to listen on (e.g., 5000): ");
            int localPort = int.Parse(Console.ReadLine() ?? "5000");

            Console.Write("Enter TARGET Remote IP (e.g., 127.0.0.1): ");
            string remoteIp = Console.ReadLine() ?? "127.0.0.1";

            Console.Write("Enter TARGET Remote Port (e.g., 5001): ");
            int remotePort = int.Parse(Console.ReadLine() ?? "5001");

            try
            {
                // 1. Setup Audio Output (Speakers)
                waveOut = new WaveOutEvent();
                waveProvider = new BufferedWaveProvider(VoiceFormat)
                {
                    DiscardOnBufferOverflow = true
                };
                waveOut.Init(waveProvider);
                waveOut.Play();

                // 2. Setup Sockets
                udpReceiver = new UdpClient(localPort);
                udpSender = new UdpClient();
                IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);

                // Start receiving incoming voice packets in background
                _ = Task.Run(() => ReceiveAudioAsync(udpReceiver));

                // 3. Setup Audio Input (Microphone)
                waveIn = new WaveInEvent
                {
                    WaveFormat = VoiceFormat,
                    BufferMilliseconds = 40 // Small buffer for low latency
                };

                waveIn.DataAvailable += (sender, e) =>
                {
                    if (e.BytesRecorded > 0 && isRunning)
                    {
                        try
                        {
                            udpSender.Send(e.Buffer, e.BytesRecorded, remoteEndpoint);
                        }
                        catch
                        {
                            // Drop packet if socket is busy
                        }
                    }
                };

                waveIn.StartRecording();

                Console.WriteLine("\n[+] Call connected! Audio streaming active.");
                Console.WriteLine("[*] Press [ENTER] to disconnect and quit.\n");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[!] Error: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        private static async Task ReceiveAudioAsync(UdpClient receiver)
        {
            while (isRunning)
            {
                try
                {
                    UdpReceiveResult result = await receiver.ReceiveAsync();
                    waveProvider?.AddSamples(result.Buffer, 0, result.Buffer.Length);
                }
                catch
                {
                    break;
                }
            }
        }

        private static void Cleanup()
        {
            isRunning = false;
            waveIn?.StopRecording();
            waveIn?.Dispose();
            waveOut?.Stop();
            waveOut?.Dispose();
            udpReceiver?.Close();
            udpSender?.Close();
            Console.WriteLine("[+] Disconnected.");
        }
    }
}
