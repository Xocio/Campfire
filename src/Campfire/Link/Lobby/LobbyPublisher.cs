using System;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Lobby
{
    internal enum LobbyState
    {
        /// <summary>Being listed, or listed again after a network problem.</summary>
        Publishing,
        Listed,
        /// <summary>No longer listed and not trying again; <see cref="LobbyPublisher.Error"/> says why.</summary>
        Stopped,
    }

    /// <summary>
    /// Keeps one open room listed in the lobby: lists it, sends heartbeats at the pace the service asks for,
    /// lists it again when the service has dropped it, and takes it down when disposed. Events are raised on the
    /// context passed in.
    /// </summary>
    internal sealed class LobbyPublisher : IDisposable
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(60);

        private readonly LobbyListing _listing;
        private readonly Func<int> _players;
        private readonly SynchronizationContext _ui;
        private readonly CancellationTokenSource _run = new CancellationTokenSource();
        private LobbyTicket _ticket;

        /// <param name="ticket">The listing when the room was just listed, or <c>null</c> to list it first.</param>
        /// <param name="players">Current number of players; read from a background thread.</param>
        public LobbyPublisher(LobbyListing listing, LobbyTicket ticket, Func<int> players, SynchronizationContext ui)
        {
            _listing = listing;
            _ticket = ticket;
            _players = players;
            _ui = ui;
            if (ticket != null)
                State = LobbyState.Listed;
        }

        public event EventHandler Changed;

        public LobbyState State { get; private set; } = LobbyState.Publishing;

        /// <summary>The last problem: why it stopped, or what is being retried. <c>null</c> when all is well.</summary>
        public LobbyException Error { get; private set; }

        /// <summary>The title as it is listed.</summary>
        public string Title => _ticket?.Title ?? _listing.Title;

        /// <summary>Whether joining from the lobby asks for a password.</summary>
        public bool Locked => !string.IsNullOrEmpty(_listing.Password);

        public void Start()
        {
            _ = RunAsync(_run.Token);
        }

        private async Task RunAsync(CancellationToken token)
        {
            if (_ticket != null)
            {
                try
                {
                    await Task.Delay(_ticket.HeartbeatInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            while (!token.IsCancellationRequested)
            {
                TimeSpan wait;
                try
                {
                    if (_ticket == null)
                    {
                        var ticket = await LobbyClient.PublishAsync(_listing, _players(), token).ConfigureAwait(false);
                        if (token.IsCancellationRequested)
                        {
                            // Taken down while it was being listed.
                            _ = CloseQuietlyAsync(ticket);
                            return;
                        }
                        _ticket = ticket;
                        wait = ticket.HeartbeatInterval;
                        LinkLog.Info("The room is listed in the lobby.");
                    }
                    else
                    {
                        wait = await LobbyClient.HeartbeatAsync(_ticket, _players(), _listing.MaxPlayers, token).ConfigureAwait(false);
                    }
                    Set(LobbyState.Listed, null);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (LobbyException ex) when (ex.Kind == LobbyError.NotFound && _ticket != null)
                {
                    // The service dropped the listing, for example after the computer slept; list it again.
                    LinkLog.Info("The lobby listing expired; listing the room again.");
                    _ticket = null;
                    continue;
                }
                catch (LobbyException ex) when (IsTemporary(ex.Kind))
                {
                    LinkLog.Warn("The lobby could not be reached: " + ex.Message);
                    Set(LobbyState.Publishing, ex);
                    wait = RetryDelay;
                }
                catch (LobbyException ex)
                {
                    LinkLog.Warn("The room is no longer listed in the lobby: " + ex.Message);
                    _ticket = null;
                    Set(LobbyState.Stopped, ex);
                    return;
                }

                try
                {
                    await Task.Delay(wait, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static bool IsTemporary(LobbyError kind)
        {
            return kind == LobbyError.Network || kind == LobbyError.TooMany || kind == LobbyError.Unexpected;
        }

        private void Set(LobbyState state, LobbyException error)
        {
            if (_run.IsCancellationRequested)
                return;
            _ui.Post(_ =>
            {
                if (_run.IsCancellationRequested)
                    return;
                State = state;
                Error = error;
                Changed?.Invoke(this, EventArgs.Empty);
            }, null);
        }

        /// <summary>Stops the heartbeats and takes the room down; the returned task ends once the service answered.</summary>
        public Task CloseAsync()
        {
            if (_run.IsCancellationRequested)
                return Task.CompletedTask;
            _run.Cancel();
            var ticket = _ticket;
            _ticket = null;
            return ticket == null ? Task.CompletedTask : CloseQuietlyAsync(ticket);
        }

        public void Dispose()
        {
            _ = CloseAsync();
        }

        /// <summary>Takes a listing down, logging instead of throwing when the service cannot be reached.</summary>
        internal static async Task CloseQuietlyAsync(LobbyTicket ticket)
        {
            try
            {
                await LobbyClient.CloseAsync(ticket).ConfigureAwait(false);
            }
            catch (LobbyException ex)
            {
                // The listing drops out by itself once the heartbeats stop.
                LinkLog.Warn("The room could not be taken down from the lobby: " + ex.Message);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
