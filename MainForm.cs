using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace CallApp
{
    public class MainForm : Form
    {
        private const int PORT = 15050;

        // Packet Types
        private const byte PACKET_HANDSHAKE = 0x01;
        private const byte PACKET_AUDIO = 0x02;
        private const byte PACKET_USER_LIST = 0x03;

        private WaveInEvent? waveIn;
        private WaveOutEvent? waveOut;
        private BufferedWaveProvider? waveProvider;

        // Network State
        private TcpListener? tcpServer;
        private TcpClient? tcpClient;
        private NetworkStream? netStream;
        private bool isHost = false;
        private bool isConnected = false;
        private bool isMuted = false;
        private bool isDeafened = false;

        // Server Side Connections (Only used if Hosting)
        private readonly List<ConnectedClient> serverClients = new List<ConnectedClient>();

        // UI Colors (Dark Theme with FamChat Orange Accents)
        private readonly Color bgDark = Color.FromArgb(30, 31, 34);
        private readonly Color bgCard = Color.FromArgb(43, 45, 49);
        private readonly Color bgInput = Color.FromArgb(17, 18, 20);
        private readonly Color accentOrange = Color.FromArgb(255, 102, 0);
        private readonly Color accentGreen = Color.FromArgb(35, 165, 89);
        private readonly Color accentRed = Color.FromArgb(242, 63, 67);
        private readonly Color textColor = Color.FromArgb(242, 243, 245);

        // UI Controls
        private TextBox txtUsername = null!;
        private TextBox txtIpAddress = null!;
        private Button btnConnect = null!;
        private Button btnHost = null!;
        private Button btnMute = null!;
        private Button btnDeafen = null!;
        private Label lblStatus = null!;
        private Panel statusDot = null!;
        private ListBox lstUsers = null!;

        private class ConnectedClient
        {
            public TcpClient Tcp { get; set; } = null!;
            public string Name { get; set; } = "";
            public bool HasMic { get; set; }
            public bool IsMuted { get; set; }
        }

        public MainForm()
        {
            SetupUI();
        }

        private void SetupUI()
        {
            this.Text = "FamCall - Family Voice Server";
            this.Size = new Size(620, 480);
            this.BackColor = bgDark;
            this.ForeColor = textColor;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Header Banner
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.FromArgb(23, 24, 28)
            };
            Label lblTitle = new Label
            {
                Text = "📞 FAMCALL VOICE SERVER",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = accentOrange,
                AutoSize = true,
                Location = new Point(20, 18)
            };
            header.Controls.Add(lblTitle);
            this.Controls.Add(header);

            // Left Setup Card
            Panel setupCard = new Panel
            {
                Location = new Point(20, 80),
                Size = new Size(340, 200),
                BackColor = bgCard
            };

            Label lblUser = new Label { Text = "Set Your Display Name:", Location = new Point(15, 12), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            txtUsername = new TextBox { Location = new Point(15, 32), Width = 310, BackColor = bgInput, ForeColor = textColor, BorderStyle = BorderStyle.FixedSingle, Text = Environment.UserName };

            Label lblIp = new Label { Text = "Enter Host Computer IP Address:", Location = new Point(15, 65), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            txtIpAddress = new TextBox { Location = new Point(15, 85), Width = 310, BackColor = bgInput, ForeColor = textColor, BorderStyle = BorderStyle.FixedSingle, Text = "127.0.0.1" };

            btnConnect = new Button
            {
                Text = "Connect to Family Voice Server",
                Location = new Point(15, 120),
                Size = new Size(310, 32),
                BackColor = accentOrange,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Click += BtnConnect_Click;

            btnHost = new Button
            {
                Text = "Host Voice Server Hub",
                Location = new Point(15, 158),
                Size = new Size(310, 28),
                BackColor = bgDark,
                ForeColor = accentOrange,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8, FontStyle.Bold)
            };
            btnHost.FlatAppearance.BorderSize = 1;
            btnHost.FlatAppearance.BorderColor = accentOrange;
            btnHost.Click += BtnHost_Click;

            setupCard.Controls.AddRange(new Control[] { lblUser, txtUsername, lblIp, txtIpAddress, btnConnect, btnHost });
            this.Controls.Add(setupCard);

            // Audio Controls Panel (Bottom Left)
            Panel voiceCard = new Panel
            {
                Location = new Point(20, 290),
                Size = new Size(340, 130),
                BackColor = bgCard
            };

            statusDot = new Panel { Size = new Size(12, 12), Location = new Point(15, 18), BackColor = Color.Gray };
            lblStatus = new Label { Text = "Offline", Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(35, 15), AutoSize = true, ForeColor = Color.Gray };

            btnMute = new Button
            {
                Text = "🎙️ Mute",
                Location = new Point(15, 45),
                Size = new Size(145, 65),
                BackColor = bgDark,
                ForeColor = textColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnMute.FlatAppearance.BorderSize = 0;
            btnMute.Click += BtnMute_Click;

            btnDeafen = new Button
            {
                Text = "🎧 Deafen",
                Location = new Point(180, 45),
                Size = new Size(145, 65),
                BackColor = bgDark,
                ForeColor = textColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnDeafen.FlatAppearance.BorderSize = 0;
            btnDeafen.Click += (s, e) => {
                isDeafened = !isDeafened;
                btnDeafen.Text = isDeafened ? "🎧 Undeafen" : "🎧 Deafen";
                btnDeafen.BackColor = isDeafened ? accentRed : bgDark;
            };

            voiceCard.Controls.AddRange(new Control[] { statusDot, lblStatus, btnMute, btnDeafen });
            this.Controls.Add(voiceCard);

            // Users List Panel (Right Column)
            Panel usersPanel = new Panel
            {
                Location = new Point(380, 80),
                Size = new Size(200, 340),
                BackColor = bgCard
            };

            Label lblUsersHeader = new Label
            {
                Text = "ROOM MEMBERS",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.DarkGray,
                Location = new Point(10, 12),
                AutoSize = true
            };

            lstUsers = new ListBox
            {
                Location = new Point(10, 35),
                Size = new Size(180, 290),
                BackColor = bgCard,
                ForeColor = textColor,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10, FontStyle.Regular)
            };

            usersPanel.Controls.AddRange(new Control[] { lblUsersHeader, lstUsers });
            this.Controls.Add(usersPanel);
        }

        private void BtnHost_Click(object? sender, EventArgs e)
        {
            try
            {
                tcpServer = new TcpListener(IPAddress.Any, PORT);
                tcpServer.Start();
                isHost = true;

                _ = Task.Run(() => AcceptIncomingClients());

                // Auto-connect localhost to own server
                txtIpAddress.Text = "127.0.0.1";
                StartClientConnection("127.0.0.1");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start Host Server: {ex.Message}", "FamCall Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (!isConnected)
            {
                StartClientConnection(txtIpAddress.Text.Trim());
            }
            else
            {
                DisconnectCall();
            }
        }

        private async void StartClientConnection(string hostIp)
        {
            try
            {
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(hostIp, PORT);
                netStream = tcpClient.GetStream();

                WaveFormat voiceFormat = new WaveFormat(16000, 16, 1);

                // Audio Playback
                waveOut = new WaveOutEvent();
                waveProvider = new BufferedWaveProvider(voiceFormat) { DiscardOnBufferOverflow = true };
                waveOut.Init(waveProvider);
                waveOut.Play();

                // Optional Microphone
                if (WaveInEvent.DeviceCount > 0)
                {
                    try
                    {
                        waveIn = new WaveInEvent { DeviceNumber = 0, WaveFormat = voiceFormat, BufferMilliseconds = 40 };
                        waveIn.DataAvailable += (s, a) =>
                        {
                            if (isConnected && !isMuted && a.BytesRecorded > 0)
                            {
                                SendPacket(PACKET_AUDIO, a.Buffer, a.BytesRecorded);
                            }
                        };
                        waveIn.StartRecording();
                    }
                    catch { waveIn = null; }
                }

                isConnected = true;
                btnConnect.Text = "Disconnect";
                btnConnect.BackColor = accentRed;
                btnHost.Enabled = false;
                txtUsername.Enabled = false;
                txtIpAddress.Enabled = false;

                if (waveIn != null)
                {
                    lblStatus.Text = isHost ? "Hosting & Connected" : "Connected (Mic Active)";
                    btnMute.Enabled = true;
                }
                else
                {
                    lblStatus.Text = "Connected (Listen Only)";
                    btnMute.Enabled = false;
                    btnMute.Text = "🔇 No Mic";
                }

                lblStatus.ForeColor = accentGreen;
                statusDot.BackColor = accentGreen;
                btnDeafen.Enabled = true;

                // Send Handshake
                SendHandshake();

                // Listen for Server Packets
                _ = Task.Run(() => ReadClientNetworkStream());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to server: {ex.Message}", "FamCall Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SendHandshake()
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(txtUsername.Text.Trim());
            byte[] payload = new byte[2 + nameBytes.Length];
            payload[0] = (byte)(waveIn != null ? 1 : 0);
            payload[1] = (byte)(isMuted ? 1 : 0);
            Array.Copy(nameBytes, 0, payload, 2, nameBytes.Length);

            SendPacket(PACKET_HANDSHAKE, payload, payload.Length);
        }

        private void BtnMute_Click(object? sender, EventArgs e)
        {
            if (waveIn == null) return;
            isMuted = !isMuted;
            btnMute.Text = isMuted ? "🔇 Unmute" : "🎙️ Mute";
            btnMute.BackColor = isMuted ? accentRed : bgDark;
            SendHandshake();
        }

        private void SendPacket(byte type, byte[] data, int length)
        {
            if (netStream == null || !netStream.CanWrite) return;
            try
            {
                byte[] lengthBytes = BitConverter.GetBytes((ushort)(length + 1));
                byte[] packet = new byte[2 + 1 + length];
                Array.Copy(lengthBytes, 0, packet, 0, 2);
                packet[2] = type;
                Array.Copy(data, 0, packet, 3, length);

                lock (netStream)
                {
                    netStream.Write(packet, 0, packet.Length);
                }
            }
            catch { }
        }

        private async Task ReadClientNetworkStream()
        {
            byte[] headerBuffer = new byte[3];
            while (isConnected && netStream != null)
            {
                try
                {
                    int read = await ReadExactAsync(netStream, headerBuffer, 3);
                    if (read < 3) break;

                    ushort packetSize = BitConverter.ToUInt16(headerBuffer, 0);
                    byte packetType = headerBuffer[2];
                    int payloadSize = packetSize - 1;

                    byte[] payload = new byte[payloadSize];
                    if (await ReadExactAsync(netStream, payload, payloadSize) < payloadSize) break;

                    if (packetType == PACKET_AUDIO && !isDeafened && waveProvider != null)
                    {
                        waveProvider.AddSamples(payload, 0, payload.Length);
                    }
                    else if (packetType == PACKET_USER_LIST)
                    {
                        string rawList = Encoding.UTF8.GetString(payload);
                        UpdateUserListUI(rawList);
                    }
                }
                catch { break; }
            }

            if (isConnected) DisconnectCall();
        }

        private async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, total, count - total);
                if (read == 0) break;
                total += read;
            }
            return total;
        }

        // ================= SERVER HUB LOGIC =================
        private async Task AcceptIncomingClients()
        {
            while (isHost && tcpServer != null)
            {
                try
                {
                    TcpClient client = await tcpServer.AcceptTcpClientAsync();
                    ConnectedClient cc = new ConnectedClient { Tcp = client };
                    lock (serverClients) { serverClients.Add(cc); }
                    _ = Task.Run(() => HandleServerClient(cc));
                }
                catch { break; }
            }
        }

        private async Task HandleServerClient(ConnectedClient client)
        {
            NetworkStream stream = client.Tcp.GetStream();
            byte[] headerBuffer = new byte[3];

            try
            {
                while (isHost)
                {
                    int read = await ReadExactAsync(stream, headerBuffer, 3);
                    if (read < 3) break;

                    ushort packetSize = BitConverter.ToUInt16(headerBuffer, 0);
                    byte packetType = headerBuffer[2];
                    int payloadSize = packetSize - 1;

                    byte[] payload = new byte[payloadSize];
                    if (await ReadExactAsync(stream, payload, payloadSize) < payloadSize) break;

                    if (packetType == PACKET_HANDSHAKE && payloadSize >= 2)
                    {
                        client.HasMic = payload[0] == 1;
                        client.IsMuted = payload[1] == 1;
                        client.Name = Encoding.UTF8.GetString(payload, 2, payloadSize - 2);
                        BroadcastServerUserList();
                    }
                    else if (packetType == PACKET_AUDIO)
                    {
                        // Relay audio packet to all other clients
                        RelayAudioToOthers(client, payload);
                    }
                }
            }
            catch { }

            lock (serverClients) { serverClients.Remove(client); }
            BroadcastServerUserList();
        }

        private void RelayAudioToOthers(ConnectedClient sender, byte[] audio)
        {
            lock (serverClients)
            {
                foreach (var c in serverClients.ToList())
                {
                    if (c != sender && c.Tcp.Connected)
                    {
                        try
                        {
                            NetworkStream ns = c.Tcp.GetStream();
                            byte[] lengthBytes = BitConverter.GetBytes((ushort)(audio.Length + 1));
                            byte[] packet = new byte[2 + 1 + audio.Length];
                            Array.Copy(lengthBytes, 0, packet, 0, 2);
                            packet[2] = PACKET_AUDIO;
                            Array.Copy(audio, 0, packet, 3, audio.Length);

                            lock (ns) { ns.Write(packet, 0, packet.Length); }
                        }
                        catch { }
                    }
                }
            }
        }

        private void BroadcastServerUserList()
        {
            List<string> userEntries = new List<string>();
            lock (serverClients)
            {
                foreach (var c in serverClients)
                {
                    if (!string.IsNullOrEmpty(c.Name))
                    {
                        string icon = "🟢";
                        if (!c.HasMic) icon = "🎧";
                        else if (c.IsMuted) icon = "🔇";
                        userEntries.Add($"{icon} {c.Name}");
                    }
                }
            }

            string serialized = string.Join(";", userEntries);
            byte[] payload = Encoding.UTF8.GetBytes(serialized);

            lock (serverClients)
            {
                foreach (var c in serverClients.ToList())
                {
                    if (c.Tcp.Connected)
                    {
                        try
                        {
                            NetworkStream ns = c.Tcp.GetStream();
                            byte[] lengthBytes = BitConverter.GetBytes((ushort)(payload.Length + 1));
                            byte[] packet = new byte[2 + 1 + payload.Length];
                            Array.Copy(lengthBytes, 0, packet, 0, 2);
                            packet[2] = PACKET_USER_LIST;
                            Array.Copy(payload, 0, packet, 3, payload.Length);

                            lock (ns) { ns.Write(packet, 0, packet.Length); }
                        }
                        catch { }
                    }
                }
            }
        }

        private void UpdateUserListUI(string rawList)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateUserListUI(rawList)));
                return;
            }

            lstUsers.Items.Clear();
            if (string.IsNullOrWhiteSpace(rawList)) return;

            string[] items = rawList.Split(';');
            foreach (var item in items)
            {
                string isSelf = item.EndsWith($" {txtUsername.Text.Trim()}") ? " (You)" : "";
                lstUsers.Items.Add($"{item}{isSelf}");
            }
        }

        private void DisconnectCall()
        {
            isConnected = false;

            waveIn?.StopRecording();
            waveIn?.Dispose();
            waveIn = null;

            waveOut?.Stop();
            waveOut?.Dispose();
            waveOut = null;

            netStream?.Dispose();
            tcpClient?.Close();

            if (isHost)
            {
                isHost = false;
                tcpServer?.Stop();
                lock (serverClients) { serverClients.Clear(); }
            }

            btnConnect.Text = "Connect to Family Voice Server";
            btnConnect.BackColor = accentOrange;
            btnHost.Enabled = true;
            lblStatus.Text = "Offline";
            lblStatus.ForeColor = Color.Gray;
            statusDot.BackColor = Color.Gray;
            btnMute.Enabled = false;
            btnMute.Text = "🎙️ Mute";
            btnMute.BackColor = bgDark;
            btnDeafen.Enabled = false;
            btnDeafen.Text = "🎧 Deafen";
            btnDeafen.BackColor = bgDark;
            txtUsername.Enabled = true;
            txtIpAddress.Enabled = true;
            lstUsers.Items.Clear();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DisconnectCall();
            base.OnFormClosing(e);
        }
    }
}
