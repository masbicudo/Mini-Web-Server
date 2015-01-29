using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace HttpFileServer
{
    /// <summary>
    /// A stream that will intercept reads and writes to another inner stream,
    /// and log reads and writes using other logging streams.
    /// </summary>
    public class InterceptorStream : Stream
    {
        private readonly Stream inner;
        private readonly Stream[] readLog;
        private readonly Stream[] writeLog;

        public InterceptorStream([NotNull] Stream inner, [CanBeNull] IEnumerable<Stream> readLog = null, [CanBeNull] IEnumerable<Stream> writeLog = null)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            this.inner = inner;
            this.readLog = (readLog ?? Enumerable.Empty<Stream>()).ToArray();
            this.writeLog = (writeLog ?? Enumerable.Empty<Stream>()).ToArray();
        }

        public override void Flush()
        {
            this.inner.Flush();

            foreach (var stream in this.readLog)
                stream.Flush();

            foreach (var stream in this.writeLog)
                stream.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await this.inner.FlushAsync(cancellationToken);

            foreach (var stream in this.readLog)
                await stream.FlushAsync(cancellationToken);

            foreach (var stream in this.writeLog)
                await stream.FlushAsync(cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var r1 = this.inner.Seek(offset, origin);

            foreach (var stream in this.readLog)
                stream.Seek(offset, origin);

            foreach (var stream in this.writeLog)
                stream.Seek(offset, origin);

            return r1;
        }

        public override void SetLength(long value)
        {
            this.inner.SetLength(value);

            foreach (var stream in this.readLog)
                stream.SetLength(value);

            foreach (var stream in this.writeLog)
                stream.SetLength(value);
        }

        #region Read methods

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.ReadAsync(buffer, offset, count).Result;
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
            var readBytes = await this.inner.ReadAsync(buffer, offset, count, cancellationToken);

            foreach (var stream in this.readLog)
                await stream.WriteAsync(buffer, offset, readBytes, cancellationToken);

            return readBytes;
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

            foreach (var stream in this.writeLog)
                await stream.WriteAsync(buffer, offset, count, cancellationToken);
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
            get
            {
                return this.inner.Position;
            }

            set
            {
                this.inner.Position = value;

                foreach (var stream in this.readLog)
                    stream.Position = value;

                foreach (var stream in this.writeLog)
                    stream.Position = value;
            }
        }

        public override void Close()
        {
            this.inner.Close();

            foreach (var stream in this.readLog)
                stream.Close();

            foreach (var stream in this.writeLog)
                stream.Close();
        }

        public override bool CanTimeout
        {
            get
            {
                return this.inner.CanTimeout || this.readLog.Any(x => x.CanTimeout) ||
                       this.writeLog.Any(x => x.CanTimeout);
            }
        }

        public override int ReadTimeout
        {
            get
            {
                return this.inner.ReadTimeout
                       + this.readLog.Sum(x => x.ReadTimeout)
                       + this.writeLog.Sum(x => x.ReadTimeout);
            }

            set
            {
                var logStreams = this.readLog.Length + this.writeLog.Length;
                var part = value / (1 + logStreams);

                this.inner.ReadTimeout = value - logStreams * part;

                foreach (var stream in this.readLog)
                    stream.ReadTimeout = part;

                foreach (var stream in this.writeLog)
                    stream.ReadTimeout = part;
            }
        }

        public override int WriteTimeout
        {
            get
            {
                return this.inner.WriteTimeout
                       + this.readLog.Sum(x => x.WriteTimeout)
                       + this.writeLog.Sum(x => x.WriteTimeout);
            }

            set
            {
                var logStreams = this.readLog.Length + this.writeLog.Length;
                var part = value / (1 + logStreams);

                this.inner.WriteTimeout = value - logStreams * part;

                foreach (var stream in this.readLog)
                    stream.WriteTimeout = part;

                foreach (var stream in this.writeLog)
                    stream.WriteTimeout = part;
            }
        }

        protected override void Dispose(bool disposing)
        {
            this.inner.Dispose();
            foreach (var stream in this.readLog)
                stream.Dispose();
            foreach (var stream in this.writeLog)
                stream.Dispose();
        }
    }
}
