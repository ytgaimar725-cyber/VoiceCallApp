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
        private const byte HEADER_AUDIO = 0x01;
        private const byte HEADER_HEARTBEAT = 0x02;
        private const byte HEADER_LEAVE = 0x03;

        private WaveInEvent? waveIn;
        private WaveOutEvent? waveOut;
        private BufferedWaveProvider? waveProvider;
        private UdpClient? udpClient;
        private bool isConnected = false;
        private bool isMuted = false;
        private bool isDeafened = false;

        // Active Users Tracking
        private readonly Dictionary<string, UserState> activeUsers = new Dictionary<string, UserState>();
        private System.Windows.Forms.Timer? cleanupTimer;
        private System.Windows.Forms.Timer? heartbeatTimer;

        // Visual Colors (Discord Dark Theme)
        private readonly Color bgDark = Color.FromArgb(30, 31, 34);
        private readonly Color bgCard = Color.FromArgb(43, 45, 49);
        private readonly Color bgInput = Color.FromArgb(17, 18, 20);
        private readonly Color accentGreen = Color.FromArgb(35, 165, 89);
        private readonly Color accentRed = Color.FromArgb(242, 63, 67);
        private readonly Color textColor = Color.FromArgb(242, 243, 245);

        // UI Components
        private TextBox txtUsername = null!;
        private Button btnConnect = null!;
        private Button btnMute = null!;
        private Button btnDeafen = null!;
        private Label lblStatus = null!;
        private Panel statusDot = null!;
        private ListBox lstUsers = null!;

        private class UserState
        {
            public string Name { get; set; } = "";
            public bool HasMic { get; set; }
            public bool IsMuted { get; set; }
            public DateTime LastSeen { get; set; }
        }

        public MainForm()
        {
            SetupUI();
        }

        private void SetupUI()
        {
            this.Text = "Home LAN Voice Call";
            this.Size = new Size(600, 440);
            this.BackColor = bgDark;
            this.ForeColor = textColor;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Top Header
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.FromArgb(23, 24, 28)
            };
            Label lblTitle = new Label
            {
                Text = "🏠 Family Voice Channel",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = textColor,
                AutoSize = true,
                Location = new Point(20, 18)
            };
            header.Controls.Add(lblTitle);
            this.Controls.Add(header);

            // User Profile Section (Left Column)
            Panel userCard = new Panel
            {
                Location = new Point(20, 80),
                Size = new Size(325, 110),
                BackColor = bgCard
            };

            Label lblUsername = new Label { Text = "Your Display Name:", Location = new Point(15, 15), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            txtUsername = new TextBox { Location = new Point(15, 35), Width = 295, BackColor = bgInput, ForeColor = textColor, BorderStyle = BorderStyle.FixedSingle, Text = Environment.UserName };

            btnConnect = new Button
            {
                Text = "Join Voice Channel",
                Location = new Point(15, 68),
                Size = new Size(295, 32),
                BackColor = accentGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Click += BtnConnect_Click;

            userCard.Controls.AddRange(new Control[] { lblUsername, txtUsername, btnConnect });
            this.Controls.Add(userCard);

            // Audio Controls Panel (Left Column)
            Panel voiceCard = new Panel
            {
                Location = new Point(20, 205),
                Size = new Size(325, 175),
                BackColor = bgCard
            };

            statusDot = new Panel
            {
                Size = new Size(12, 12),
                Location = new Point(20, 25),
                BackColor = Color.Gray
            };

            lblStatus = new Label
            {
                Text = "Disconnected from LAN",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(40, 22),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            btnMute = new Button
            {
                Text = "🎙️ Mute",
                Location = new Point(20, 60),
                Size = new Size(135, 90),
                BackColor = bgDark,
                ForeColor = textColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnMute.FlatAppearance.BorderSize = 0;
            btnMute.Click += (s, e) => {
                if (waveIn == null) return;
                isMuted = !isMuted;
                btnMute.Text = isMuted ? "🔇 Unmute" : "🎙️ Mute";
                btnMute.BackColor = isMuted ? accentRed : bgDark;
                SendHeartbeat();
            };

            btnDeafen = new Button
            {
                Text = "🎧 Deafen",
                Location = new Point(170, 60),
                Size = new Size(135, 90),
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

            // Connected Users Side Panel (Right Column)
            Panel usersPanel = new Panel
            {
                Location = new Point(365, 80),
                Size = new Size(195, 300),
                BackColor = bgCard
            };

            Label lblUsersHeader = new Label
            {
                Text = "VOICE USERS — 0",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.DarkGray,
                Location = new Point(10, 10),
                AutoSize = true
            };

            lstUsers = new ListBox
            {
                Location = new Point(10, 30),
                Size = new Size(175, 255),
                BackColor = bgCard,
                ForeColor = textColor,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10, FontStyle.Regular)
            };

            usersPanel.Controls.AddRange(new Control[] { lblUsersHeader, lstUsers });
            this.Controls.Add(usersPanel);

            // Timers for User Tracking
            cleanupTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            cleanupTimer.Tick += CleanupExpiredUsers;

            heartbeatTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            heartbeatTimer.Tick += (s, e) => SendHeartbeat();
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (!isConnected)
            {
                StartLanCall();
            }
            else
            {
                EndLanCall();
            }
        }

        private void StartLanCall()
        {
            try
            {
                WaveFormat voiceFormat = new WaveFormat(16000, 16, 1);

                // 1. Setup Audio Output Speaker
                waveOut = new WaveOutEvent();
                waveProvider = new BufferedWaveProvider(voiceFormat) { DiscardOnBufferOverflow = true };
                waveOut.Init(waveProvider);
                waveOut.Play();

                // 2. Setup UDP Network Socket
                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, PORT));
                udpClient.EnableBroadcast = true;

                // Start Receiving Network Data
                _ = Task.Run(() => ListenForLanPackets());

                // 3. Optional Microphone Setup
                if (WaveInEvent.DeviceCount > 0)
                {
                    try
                    {
                        waveIn = new WaveInEvent
                        {
                            DeviceNumber = 0,
                            WaveFormat = voiceFormat,
                            BufferMilliseconds = 40
                        };

                        waveIn.DataAvailable += (s, a) =>
                        {
                            if (isConnected && !isMuted && a.BytesRecorded > 0)
                            {
                                SendAudioChunk(a.Buffer, a.BytesRecorded);
                            }
                        };

                        waveIn.StartRecording();
                    }
                    catch
                    {
                        waveIn = null;
                    }
                }

                // 4. Update UI & Timers
                isConnected = true;
                btnConnect.Text = "Leave Voice Channel";
                btnConnect.BackColor = accentRed;
                
                if (waveIn != null)
                {
                    lblStatus.Text = "Connected (Mic Active)";
                    btnMute.Enabled = true;
                    btnMute.Text = "🎙️ Mute";
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
                txtUsername.Enabled = false;

                heartbeatTimer?.Start();
                cleanupTimer?.Start();
                SendHeartbeat();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start LAN voice: {ex.Message}", "CallApp Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SendAudioChunk(byte[] audioData, int length)
        {
            if (udpClient == null) return;
            try
            {
                byte[] packet = new byte[length + 1];
                packet[0] = HEADER_AUDIO;
                Array.Copy(audioData, 0, packet, 1, length);
                IPEndPoint ep = new IPEndPoint(IPAddress.Broadcast, PORT);
                udpClient.Send(packet, packet.Length, ep);
            }
            catch { }
        }

        private void SendHeartbeat()
        {
            if (!isConnected || udpClient == null) return;
            try
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(txtUsername.Text.Trim());
                byte[] packet = new byte[3 + nameBytes.Length];
                packet[0] = HEADER_HEARTBEAT;
                packet[1] = (byte)(waveIn != null ? 1 : 0);
                packet[2] = (byte)(isMuted ? 1 : 0);
                Array.Copy(nameBytes, 0, packet, 3, nameBytes.Length);

                IPEndPoint ep = new IPEndPoint(IPAddress.Broadcast, PORT);
                udpClient.Send(packet, packet.Length, ep);
            }
            catch { }
        }

        private void SendLeavePacket()
        {
            if (udpClient == null) return;
            try
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(txtUsername.Text.Trim());
                byte[] packet = new byte[1 + nameBytes.Length];
                packet[0] = HEADER_LEAVE;
                Array.Copy(nameBytes, 0, packet, 1, nameBytes.Length);

                IPEndPoint ep = new IPEndPoint(IPAddress.Broadcast, PORT);
                udpClient.Send(packet, packet.Length, ep);
            }
            catch { }
        }

        private async Task ListenForLanPackets()
        {
            while (isConnected && udpClient != null)
            {
                try
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();
                    byte[] data = result.Buffer;
                    if (data.Length < 1) continue;

                    byte packetType = data[0];

                    if (packetType == HEADER_AUDIO)
                    {
                        if (!isDeafened && waveProvider != null)
                        {
                            waveProvider.AddSamples(data, 1, data.Length - 1);
                        }
                    }
                    else if (packetType == HEADER_HEARTBEAT && data.Length >= 3)
                    {
                        bool hasMic = data[1] == 1;
                        bool muted = data[2] == 1;
                        string username = Encoding.UTF8.GetString(data, 3, data.Length - 3);

                        UpdateUserList(username, hasMic, muted);
                    }
                    else if (packetType == HEADER_LEAVE && data.Length > 1)
                    {
                        string username = Encoding.UTF8.GetString(data, 1, data.Length - 1);
                        RemoveUser(username);
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        private void UpdateUserList(string username, bool hasMic, bool isMuted)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateUserList(username, hasMic, isMuted)));
                return;
            }

            activeUsers[username] = new UserState
            {
                Name = username,
                HasMic = hasMic,
                IsMuted = isMuted,
                LastSeen = DateTime.Now
            };

            RefreshUserListUI();
        }

        private void RemoveUser(string username)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => RemoveUser(username)));
                return;
            }

            if (activeUsers.ContainsKey(username))
            {
                activeUsers.Remove(username);
                RefreshUserListUI();
            }
        }

        private void CleanupExpiredUsers(object? sender, EventArgs e)
        {
            DateTime cutoff = DateTime.Now.AddSeconds(-3);
            var expired = activeUsers.Where(kvp => kvp.Value.LastSeen < cutoff).Select(kvp => kvp.Key).ToList();

            foreach (var key in expired)
            {
                activeUsers.Remove(key);
            }

            if (expired.Count > 0)
            {
                RefreshUserListUI();
            }
        }

        private void RefreshUserListUI()
        {
            lstUsers.Items.Clear();
            foreach (var user in activeUsers.Values)
            {
                string statusIcon = "🟢";
                if (!user.HasMic) statusIcon = "🎧";
                else if (user.IsMuted) statusIcon = "🔇";

                string isSelf = user.Name == txtUsername.Text.Trim() ? " (You)" : "";
                lstUsers.Items.Add($"{statusIcon} {user.Name}{isSelf}");
            }
        }

        private void EndLanCall()
        {
            isConnected = false;

            heartbeatTimer?.Stop();
            cleanupTimer?.Stop();

            SendLeavePacket();

            waveIn?.StopRecording();
            waveIn?.Dispose();
            waveIn = null;

            waveOut?.Stop();
            waveOut?.Dispose();
            waveOut = null;

            udpClient?.Close();
            udpClient = null;

            activeUsers.Clear();
            lstUsers.Items.Clear();

            btnConnect.Text = "Join Voice Channel";
            btnConnect.BackColor = accentGreen;
            lblStatus.Text = "Disconnected from LAN";
            lblStatus.ForeColor = Color.Gray;
            statusDot.BackColor = Color.Gray;
            btnMute.Enabled = false;
            btnMute.Text = "🎙️ Mute";
            btnMute.BackColor = bgDark;
            btnDeafen.Enabled = false;
            btnDeafen.Text = "🎧 Deafen";
            btnDeafen.BackColor = bgDark;
            isMuted = false;
            isDeafened = false;
            txtUsername.Enabled = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            EndLanCall();
            base.OnFormClosing(e);
        }
    }
}
