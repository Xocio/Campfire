using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// Read-only wrapper around a download stream that reports progress and gives up when the server stalls.
    /// </summary>
    /// <remarks>
    /// Network reads on .NET Framework ignore cancellation tokens, so a stalled read is ended by disposing
    /// <paramref name="owner"/> (usually the HTTP response), which aborts the connection.
    /// </remarks>
    internal sealed class ProgressStream : Stream
    {
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);

        private readonly Stream _inner;
        private readonly IDisposable _owner;
        private readonly Action<long> _onProgress;

        public ProgressStream(Stream inner, IDisposable owner, Action<long> onProgress)
        {
            _inner = inner;
            _owner = owner;
            _onProgress = onProgress;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read = _inner.ReadAsync(buffer, offset, count, cancellationToken);
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var finished = await Task.WhenAny(read, Task.Delay(StallTimeout, timer.Token)).ConfigureAwait(false);
                if (finished != read)
                {
                    _owner?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("The download stalled.");
                }
                timer.Cancel();
            }

            int bytes = await read.ConfigureAwait(false);
            if (bytes > 0)
                _onProgress?.Invoke(bytes);
            return bytes;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
