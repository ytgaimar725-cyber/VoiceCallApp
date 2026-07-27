using System;
using System.Threading.Tasks;
using LiveKit;

namespace CallApp
{
    class Program
    {
        private static Room? room;

        static async Task Main(string[] args)
        {
            Console.Title = "CallApp - Discord Style Voice";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("========================================");
            Console.WriteLine("      DISCORD-STYLE VOICE CALLAPP       ");
            Console.WriteLine("========================================\n");
            Console.ResetColor();

            Console.Write("Enter Your Display Name: ");
            string username = Console.ReadLine() ?? "User";

            Console.Write("Enter Room Name to Join/Create (e.g., general): ");
            string roomName = Console.ReadLine() ?? "general";

            Console.WriteLine("\n[+] Connecting to Discord-style voice server...");

            try
            {
                // 1. Initialize Room Connection
                room = new Room();

                // Listen for when other people join or leave your voice room
                room.ParticipantConnected += (participant) =>
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n[+] {participant.Identity} joined the call!");
                    Console.ResetColor();
                };

                room.ParticipantDisconnected += (participant) =>
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[-] {participant.Identity} left the call.");
                    Console.ResetColor();
                };

                // 2. Connect using LiveKit Cloud / Free Relay
                // (Paste your LiveKit URL and Token here or use hosted relay)
                string serverUrl = "wss://your-livekit-instance.livekit.cloud";
                string token = "YOUR_GENERATED_USER_TOKEN"; 

                await room.Connect(serverUrl, token);

                // 3. Enable Microphone
                await room.LocalParticipant.SetMicrophoneEnabled(true);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[SUCCESS] Connected to room: #{roomName}");
                Console.WriteLine("[*] Your microphone is active!");
                Console.WriteLine("[*] Tell your friend to join the same room name.");
                Console.WriteLine("[*] Press [ENTER] to hang up.\n");
                Console.ResetColor();

                Console.ReadLine();
                
                await room.Disconnect();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[!] Connection failed: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}
