using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Sits between a joining player's game and the tunnel to the host's world. The host uses it as a gate: a
    /// player can be turned away at login or disconnected later. It also adds the room status to the joining
    /// player's game: a welcome message in chat and a live header and footer in the player list opened with Tab.
    /// </summary>
    internal sealed class GameProxy : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly int _targetPort;
        private readonly Func<string, GameOverlayInfo> _overlay;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly List<Connection> _connections = new List<Connection>();

        private GameProxy(TcpListener listener, int targetPort, Func<string, GameOverlayInfo> overlay)
        {
            _listener = listener;
            _targetPort = targetPort;
            _overlay = overlay;
        }

        /// <param name="bind">Where the game connects.</param>
        /// <param name="targetPort">Loopback port of the tunnel to the host's game.</param>
        /// <param name="overlay">Tells a joining player's game about the room, or <c>null</c> to leave it untouched.</param>
        public static GameProxy Start(IPEndPoint bind, int targetPort, Func<string, GameOverlayInfo> overlay = null)
        {
            var listener = new TcpListener(bind);
            listener.Start();
            var proxy = new GameProxy(listener, targetPort, overlay);
            _ = proxy.AcceptLoopAsync();
            return proxy;
        }

        /// <summary>The loopback port the proxy listens on.</summary>
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>
        /// Decides about each login by player name: returns the reason to turn the player away, or <c>null</c> to
        /// let them in. Called on a worker thread.
        /// </summary>
        public Func<string, string> Refuse { get; set; }

        private volatile bool _overlayEnabled = true;

        /// <summary>
        /// Whether this proxy should add the room overlay for unencrypted connections. Turned off when
        /// the in-game Java agent is attached, so players do not receive the message twice.
        /// </summary>
        public bool OverlayEnabled
        {
            get => _overlayEnabled;
            set => _overlayEnabled = value;
        }


        /// <summary>Disconnects every player whose name matches, showing <paramref name="reason"/>; returns how many.</summary>
        public int Disconnect(Func<string, bool> match, string reason)
        {
            List<Connection> targets;
            lock (_connections)
                targets = _connections.Where(c => c.PlayerName != null && match(c.PlayerName)).ToList();
            foreach (var connection in targets)
                _ = connection.KickAsync(reason);
            return targets.Count;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            lock (_connections)
            {
                foreach (var connection in _connections)
                    connection.Close();
                _connections.Clear();
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException)
                {
                    return;
                }

                var connection = new Connection(this, client);
                lock (_connections)
                    _connections.Add(connection);
                _ = RunAsync(connection);
            }
        }

        private async Task RunAsync(Connection connection)
        {
            try
            {
                await connection.RunAsync(_targetPort, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException
                                       || ex is InvalidDataException || ex is OperationCanceledException)
            {
                // The game or the tunnel closed the connection.
            }
            finally
            {
                connection.Close();
                lock (_connections)
                    _connections.Remove(connection);
            }
        }

        /// <summary>One game connection.</summary>
        private sealed class Connection
        {
            private enum Phase
            {
                Login,
                Configuration,
                Play,
                PassThrough,
            }

            private const int MaxFrame = 8 * 1024 * 1024;

            private readonly GameProxy _owner;
            private readonly TcpClient _client;
            private readonly TcpClient _server = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
            private readonly CancellationTokenSource _closed = new CancellationTokenSource();

            private NetworkStream _toClient;
            private int _protocol;
            private int _threshold = -1;
            private int _startConfiguration = -1;
            private volatile Phase _phase = Phase.Login;
            private volatile bool _joined;
            private Task _overlayTask;

            public Connection(GameProxy owner, TcpClient client)
            {
                _owner = owner;
                _client = client;
                _client.NoDelay = true;
            }

            /// <summary>The player name this connection logged in with, once known.</summary>
            public string PlayerName { get; private set; }

            /// <summary>
            /// Ends the connection with <paramref name="reason"/> shown in the game where the protocol allows it;
            /// otherwise, as on encrypted connections, the connection is simply closed.
            /// </summary>
            public async Task KickAsync(string reason)
            {
                try
                {
                    // The lock is never released: nothing else may be written after the disconnect.
                    if (_toClient == null || !await _writeLock.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
                        return;

                    byte[] body = null;
                    switch (_phase)
                    {
                        case Phase.Login:
                            body = GameProtocol.BuildLoginDisconnect(reason);
                            break;
                        case Phase.Configuration:
                            body = GameProtocol.BuildDisconnect(_protocol, GameProtocol.ConfigurationDisconnectPacket(_protocol), reason);
                            break;
                        case Phase.Play:
                            int id = GameProtocol.DisconnectPacket(_protocol);
                            if (id >= 0)
                                body = GameProtocol.BuildDisconnect(_protocol, id, reason);
                            break;
                    }
                    if (body != null)
                    {
                        byte[] frame = GameProtocol.Frame(body, _threshold);
                        await _toClient.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false);
                        await Task.Delay(300).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                {
                }
                finally
                {
                    Close();
                }
            }

            public void Close()
            {
                try
                {
                    _closed.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                _client.Close();
                _server.Close();
            }

            public async Task RunAsync(int targetPort, CancellationToken cancellation)
            {
                await _server.ConnectAsync(IPAddress.Loopback, targetPort).ConfigureAwait(false);
                var fromClient = new InputBuffer(_client.GetStream());
                var toServer = _server.GetStream();
                var fromServer = new InputBuffer(_server.GetStream());
                _toClient = _client.GetStream();

                // The handshake decides everything: legacy pings and status requests are passed through as is.
                int first = await fromClient.ReadByteAsync(cancellation).ConfigureAwait(false);
                if (first < 0)
                    return;
                if (first == 0xFE)
                {
                    await toServer.WriteAsync(new[] { (byte)first }, 0, 1, cancellation).ConfigureAwait(false);
                    await PipeAsync(fromClient, toServer, fromServer, _toClient, cancellation).ConfigureAwait(false);
                    return;
                }

                byte[] handshake = await ReadFrameAsync(fromClient, first, cancellation).ConfigureAwait(false);
                await toServer.WriteAsync(handshake, 0, handshake.Length, cancellation).ConfigureAwait(false);
                if (!ParseHandshake(handshake))
                {
                    await PipeAsync(fromClient, toServer, fromServer, _toClient, cancellation).ConfigureAwait(false);
                    return;
                }

                // The next frame names the player, which is what admission and removal go by.
                int loginFirst = await fromClient.ReadByteAsync(cancellation).ConfigureAwait(false);
                if (loginFirst < 0)
                    return;
                byte[] login = await ReadFrameAsync(fromClient, loginFirst, cancellation).ConfigureAwait(false);
                try
                {
                    PlayerName = GameProtocol.ReadLoginName(login);
                }
                catch (InvalidDataException)
                {
                    PlayerName = null;
                }
                string refusal = PlayerName == null ? null : _owner.Refuse?.Invoke(PlayerName);
                if (refusal != null)
                {
                    LinkLog.Info("Refused a game login: the player was removed from the room.");
                    byte[] frame = GameProtocol.Frame(GameProtocol.BuildLoginDisconnect(refusal), -1);
                    await _toClient.WriteAsync(frame, 0, frame.Length, cancellation).ConfigureAwait(false);
                    await Task.Delay(300, cancellation).ConfigureAwait(false);
                    return;
                }
                await toServer.WriteAsync(login, 0, login.Length, cancellation).ConfigureAwait(false);

                _startConfiguration = GameProtocol.StartConfigurationPacket(_protocol);

                var upstream = fromClient.CopyToAsync(toServer, cancellation);
                var downstream = PumpServerAsync(fromServer, cancellation);
                await Task.WhenAny(upstream, downstream).ConfigureAwait(false);
            }

            /// <summary>Returns whether the connection is a login, the only kind worth watching.</summary>
            private bool ParseHandshake(byte[] frame)
            {
                int offset = 0;
                int length = GameProtocol.ReadVarInt(frame, ref offset);
                if (GameProtocol.ReadVarInt(frame, ref offset) != 0x00)
                    return false;
                _protocol = GameProtocol.ReadVarInt(frame, ref offset);
                GameProtocol.ReadString(frame, ref offset);
                offset += 2; // port
                int next = GameProtocol.ReadVarInt(frame, ref offset);
                return length > 0 && next == 2;
            }

            private async Task PumpServerAsync(InputBuffer fromServer, CancellationToken cancellation)
            {
                while (!cancellation.IsCancellationRequested)
                {
                    int first = await fromServer.ReadByteAsync(cancellation).ConfigureAwait(false);
                    if (first < 0)
                        return;
                    byte[] frame = await ReadFrameAsync(fromServer, first, cancellation).ConfigureAwait(false);

                    if (_phase != Phase.PassThrough)
                    {
                        try
                        {
                            Inspect(frame);
                        }
                        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException)
                        {
                            // Never risk the game connection: anything unexpected turns this into a plain pipe.
                            _phase = Phase.PassThrough;
                            LinkLog.Warn("Game connection could not be followed: " + ex.Message);
                        }
                    }

                    await _writeLock.WaitAsync(cancellation).ConfigureAwait(false);
                    if (_phase == Phase.PassThrough)
                    {
                        // The lock is kept, so no message can be written into the plain pipe.
                        await _toClient.WriteAsync(frame, 0, frame.Length, cancellation).ConfigureAwait(false);
                        await fromServer.CopyToAsync(_toClient, cancellation).ConfigureAwait(false);
                        return;
                    }
                    try
                    {
                        await _toClient.WriteAsync(frame, 0, frame.Length, cancellation).ConfigureAwait(false);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                }
            }

            /// <summary>Follows the connection's phase from the server's packets.</summary>
            private void Inspect(byte[] frame)
            {
                int offset = 0;
                GameProtocol.ReadVarInt(frame, ref offset);
                var payload = new byte[frame.Length - offset];
                Buffer.BlockCopy(frame, offset, payload, 0, payload.Length);

                // The join game packet carries the world and is far larger than the packets followed afterwards.
                // Only inflate packets that big until it has been seen.
                int maxSize = _phase == Phase.Play ? (_joined ? 64 : MaxFrame) : 8 * 1024;
                byte[] body = GameProtocol.Unwrap(payload, _threshold, maxSize);
                if (body == null || body.Length == 0)
                    return;

                int bodyOffset = 0;
                int id = GameProtocol.ReadVarInt(body, ref bodyOffset);
                switch (_phase)
                {
                    case Phase.Login:
                        if (id == GameProtocol.LoginEncryptionRequest)
                        {
                            _phase = Phase.PassThrough;
                            // A vanilla LAN world enables authentication even when the host uses an offline
                            // account, so this is the normal case. The Java agent covers it; the LAN entry
                            // remains the fallback when the agent cannot be attached.
                            LinkLog.Info("Game connection is encrypted; the proxy passes it through. " +
                                         "The in-game overlay is provided by the Java agent when available.");
                        }
                        else if (id == GameProtocol.LoginSetCompression)
                        {
                            _threshold = GameProtocol.ReadVarInt(body, ref bodyOffset);
                        }
                        else if (id == GameProtocol.LoginSuccess)
                        {
                            if (_protocol >= GameProtocol.FirstWithConfiguration)
                                _phase = Phase.Configuration;
                            else
                                EnterPlay();
                        }
                        break;

                    case Phase.Configuration:
                        if (id == GameProtocol.FinishConfigurationPacket(_protocol))
                            EnterPlay();
                        break;

                    case Phase.Play:
                        if (_startConfiguration >= 0 && id == _startConfiguration)
                        {
                            _joined = false;
                            _phase = Phase.Configuration;
                        }
                        else if (id == GameProtocol.JoinGamePacket(_protocol))
                        {
                            Joined();
                        }
                        break;
                }
            }

            private void EnterPlay()
            {
                _phase = Phase.Play;
            }

            /// <summary>Starts talking to the client once the server has actually put it into the world.</summary>
            private void Joined()
            {
                _joined = true;
                if (_overlayTask == null && _owner._overlay != null)
                    _overlayTask = RunOverlayAsync();
            }

            /// <summary>
            /// Waits for the world to arrive, then adds the welcome message and keeps the Tab list's header and
            /// footer up to date while the player stays in the game.
            /// </summary>
            private async Task RunOverlayAsync()
            {
                try
                {
                    // Login success and finish configuration are followed by the join game packet; give the client
                    // a moment to leave its loading screen before talking to it.
                    await Task.Delay(1500, _closed.Token).ConfigureAwait(false);

                    bool welcomed = false;
                    while (!_closed.IsCancellationRequested)
                    {
                        if (!_owner.OverlayEnabled)
                            return;

                        // A reconfiguration briefly leaves play; wait for the next join game instead of writing
                        // play packets into the configuration state.
                        if (_phase == Phase.Play && _joined)
                        {
                            var info = _owner._overlay(PlayerName);
                            if (info != null)
                            {
                                if (!welcomed)
                                {
                                    byte[] chat = GameProtocol.BuildSystemChat(_protocol, GameMessage.Welcome(info));
                                    if (chat != null)
                                        await SendInjectedAsync(chat).ConfigureAwait(false);
                                    welcomed = true;
                                }

                                byte[] list = GameProtocol.BuildPlayerListHeaderFooter(
                                    _protocol, GameMessage.Header(info), GameMessage.Footer());
                                if (list != null)
                                    await SendInjectedAsync(list).ConfigureAwait(false);
                            }
                        }

                        await Task.Delay(5000, _closed.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException)
                {
                    // The player left or the tunnel closed.
                }
                catch (Exception ex)
                {
                    LinkLog.Warn("Game overlay stopped: " + ex.Message);
                }
            }

            /// <summary>Writes a clientbound packet into the connection, unless it is no longer in play.</summary>
            private async Task SendInjectedAsync(byte[] body)
            {
                var token = _closed.Token;
                if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false))
                    return;
                try
                {
                    if (_phase != Phase.Play || _toClient == null)
                        return;

                    byte[] frame = GameProtocol.Frame(body, _threshold);
                    await _toClient.WriteAsync(frame, 0, frame.Length, token).ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }
            }

            /// <summary>Reads one frame whose first length byte is already read; returns it with its length prefix.</summary>
            private static async Task<byte[]> ReadFrameAsync(InputBuffer stream, int firstByte, CancellationToken cancellation)
            {
                var header = new byte[5];
                int headerLength = 0;
                int length = 0;
                int current = firstByte;
                while (true)
                {
                    header[headerLength] = (byte)current;
                    length |= (current & 0x7F) << (7 * headerLength);
                    headerLength++;
                    if ((current & 0x80) == 0)
                        break;
                    if (headerLength == header.Length)
                        throw new InvalidDataException("Frame length is too long.");
                    current = await stream.ReadByteAsync(cancellation).ConfigureAwait(false);
                    if (current < 0)
                        throw new EndOfStreamException();
                }
                if (length < 0 || length > MaxFrame)
                    throw new InvalidDataException("Frame is too large.");

                var frame = new byte[headerLength + length];
                Buffer.BlockCopy(header, 0, frame, 0, headerLength);
                await stream.ReadExactAsync(frame, headerLength, length, cancellation).ConfigureAwait(false);
                return frame;
            }

            private static async Task PipeAsync(InputBuffer fromClient, Stream toServer, InputBuffer fromServer, Stream toClient,
                CancellationToken cancellation)
            {
                var up = fromClient.CopyToAsync(toServer, cancellation);
                var down = fromServer.CopyToAsync(toClient, cancellation);
                await Task.WhenAny(up, down).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A read buffer that hands over whatever it holds as soon as asked. Unlike <see cref="BufferedStream"/>, a
        /// partly filled buffer never waits for more data, which would stall a game that sends its next packet
        /// together with the handshake.
        /// </summary>
        private sealed class InputBuffer
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer = new byte[64 * 1024];
            private int _start;
            private int _end;

            public InputBuffer(Stream stream)
            {
                _stream = stream;
            }

            /// <summary>Returns the next byte, or -1 at the end of the stream.</summary>
            public async Task<int> ReadByteAsync(CancellationToken cancellation)
            {
                if (_start == _end && !await FillAsync(cancellation).ConfigureAwait(false))
                    return -1;
                return _buffer[_start++];
            }

            public async Task ReadExactAsync(byte[] target, int offset, int count, CancellationToken cancellation)
            {
                while (count > 0)
                {
                    if (_start == _end && !await FillAsync(cancellation).ConfigureAwait(false))
                        throw new EndOfStreamException();
                    int n = Math.Min(count, _end - _start);
                    Buffer.BlockCopy(_buffer, _start, target, offset, n);
                    _start += n;
                    offset += n;
                    count -= n;
                }
            }

            /// <summary>Writes what is buffered, then everything else until the stream ends.</summary>
            public async Task CopyToAsync(Stream destination, CancellationToken cancellation)
            {
                if (_end > _start)
                {
                    await destination.WriteAsync(_buffer, _start, _end - _start, cancellation).ConfigureAwait(false);
                    _start = _end;
                }
                await _stream.CopyToAsync(destination, 81920, cancellation).ConfigureAwait(false);
            }

            private async Task<bool> FillAsync(CancellationToken cancellation)
            {
                _start = 0;
                _end = await _stream.ReadAsync(_buffer, 0, _buffer.Length, cancellation).ConfigureAwait(false);
                return _end > 0;
            }
        }
    }
}
