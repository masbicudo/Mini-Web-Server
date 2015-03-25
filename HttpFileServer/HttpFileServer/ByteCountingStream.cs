using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace HttpFileServer
{
    public static class ByteCountingStream
    {
        public static ByteCountingStream<TStreamInner> Create<TStreamInner>(TStreamInner innerStream)
            where TStreamInner : Stream
        {
            return new ByteCountingStream<TStreamInner>(innerStream);
        }
    }

    public class ByteCountingStream<TStreamInner> : Stream
        where TStreamInner : Stream
    {
        protected readonly TStreamInner inner;

        public ByteCountingStream([NotNull] TStreamInner inner, [CanBeNull] IEnumerable<Stream> readLog = null, [CanBeNull] IEnumerable<Stream> writeLog = null)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            this.inner = inner;
        }

        public override void Flush()
        {
            this.inner.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await this.inner.FlushAsync(cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var r1 = this.inner.Seek(offset, origin);
            return r1;
        }

        public override void SetLength(long value)
        {
            this.inner.SetLength(value);
        }

        #region Read methods

        public override int Read(byte[] buffer, int offset, int count)
        {
            var rc = this.ReadAsync(buffer, offset, count).Result;
            return rc;
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return this.ReadAsync(buffer, offset, count).ContinueWith(task => callback(task));
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return ((Task<int>)asyncResult).Result;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var rc = await this.inner.ReadAsync(buffer, offset, count, cancellationToken);
            this.ReadCount += rc;
            return rc;
        }

        #endregion

        #region Write methods

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WriteAsync(buffer, offset, count).Wait();
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return this.WriteAsync(buffer, offset, count).ContinueWith(task => callback(task));
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            ((Task)asyncResult).Wait();
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await this.inner.WriteAsync(buffer, offset, count, cancellationToken);
            this.WriteCount += count;
        }

        #endregion

        public override bool CanRead
        {
            get { return this.inner.CanRead; }
        }

        public override bool CanSeek
        {
            get { return this.inner.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return this.inner.CanWrite; }
        }

        public override long Length
        {
            get { return this.inner.Length; }
        }

        /// <summary>
        /// Gets or sets the position within the current stream.
        /// </summary>
        /// <returns>
        /// The current position within the stream.
        /// </returns>
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception><exception cref="T:System.NotSupportedException">The stream does not support seeking. </exception><exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override long Position
        {
            get { return this.inner.Position; }
            set { this.inner.Position = value; }
        }

        public override void Close()
        {
            this.inner.Close();
        }

        public override bool CanTimeout
        {
            get { return this.inner.CanTimeout; }
        }

        public override int ReadTimeout
        {
            get { return this.inner.ReadTimeout; }
            set { this.inner.ReadTimeout = value; }
        }

        public override int WriteTimeout
        {
            get { return this.inner.WriteTimeout; }
            set { this.inner.WriteTimeout = value; }
        }

        public long ReadCount { get; set; }

        public long WriteCount { get; set; }

        protected override void Dispose(bool disposing)
        {
            this.inner.Dispose();
        }
    }
}