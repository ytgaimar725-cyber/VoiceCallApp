using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace CallApp
{
    public class MainForm : Form
    {
        private const int PORT = 5050;

        private WaveInEvent? waveIn;
        private WaveOutEvent? waveOut;
        private BufferedWaveProvider? waveProvider;
        private UdpClient? udpClient;
        private bool isConnected = false;
        private bool isMuted = false;
        private bool isDeafened = false;

        // UI Colors (Discord Theme)
        private readonly Color bgDark = Color.FromArgb(30, 31, 34);
        private readonly Color bgCard = Color.FromArgb(43, 45, 49);
        private readonly Color bgInput = Color.FromArgb(17, 18, 20);
        private readonly Color accentGreen = Color.FromArgb(35, 165, 89);
        private readonly Color accentRed = Color.FromArgb(242, 63, 67);
        private readonly Color textColor = Color.FromArgb(242, 243, 245);

        private TextBox txtUsername = null!;
        private Button btnConnect = null!;
        private Button btnMute = null!;
        private Button btnDeafen = null!;
        private Label lblStatus = null!;
        private Panel statusDot = null!;

        public MainForm()
        {
            SetupUI();
        }

        private void SetupUI()
        {
            this.Text = "Home LAN Voice Call";
            this.Size = new Size(380, 440);
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
                Text = "🏠 Family Voice Channel",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = textColor,
                AutoSize = true,
                Location = new Point(20, 18)
            };
            header.Controls.Add(lblTitle);
            this.Controls.Add(header);

            // User Profile Card
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

            // Audio Controls Card
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
                isMuted = !isMuted;
                btnMute.Text = isMuted ? "🔇 Unmute" : "🎙️ Mute";
                btnMute.BackColor = isMuted ? accentRed : bgDark;
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

                // Setup Audio Player
                waveOut = new WaveOutEvent();
                waveProvider = new BufferedWaveProvider(voiceFormat) { DiscardOnBufferOverflow = true };
                waveOut.Init(waveProvider);
                waveOut.Play();

                // Setup LAN UDP Socket (Broadcast Enabled)
                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.ExclusiveAddressUse = false;
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, PORT));
                udpClient.EnableBroadcast = true;

                IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, PORT);

                // Start Listening Task
                _ = Task.Run(() => ListenForLanAudio());

                // Setup Mic Recording
                waveIn = new WaveInEvent { WaveFormat = voiceFormat, BufferMilliseconds = 40 };
                waveIn.DataAvailable += (s, a) =>
                {
                    if (isConnected && !isMuted && a.BytesRecorded > 0)
                    {
                        try { udpClient.Send(a.Buffer, a.BytesRecorded, broadcastEndPoint); } catch { }
                    }
                };
                waveIn.StartRecording();

                // UI Updates
                isConnected = true;
                btnConnect.Text = "Leave Voice Channel";
                btnConnect.BackColor = accentRed;
                lblStatus.Text = "Connected (Broadcasting)";
                lblStatus.ForeColor = accentGreen;
                statusDot.BackColor = accentGreen;
                btnMute.Enabled = true;
                btnDeafen.Enabled = true;
                txtUsername.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start LAN voice: {ex.Message}", "CallApp Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ListenForLanAudio()
        {
            while (isConnected && udpClient != null)
            {
                try
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();
                    if (!isDeafened && waveProvider != null)
                    {
                        waveProvider.AddSamples(result.Buffer, 0, result.Buffer.Length);
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        private void EndLanCall()
        {
            isConnected = false;
            waveIn?.StopRecording();
            waveIn?.Dispose();
            waveOut?.Stop();
            waveOut?.Dispose();
            udpClient?.Close();

            btnConnect.Text = "Join Voice Channel";
            btnConnect.BackColor = accentGreen;
            lblStatus.Text = "Disconnected from LAN";
            lblStatus.ForeColor = Color.Gray;
            statusDot.BackColor = Color.Gray;
            btnMute.Enabled = false;
            btnDeafen.Enabled = false;
            txtUsername.Enabled = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            EndLanCall();
            base.OnFormClosing(e);
        }
    }
}
