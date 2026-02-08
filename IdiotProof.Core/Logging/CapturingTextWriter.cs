using System;
using System.IO;
using System.Text;

namespace IdiotProof.Logging {
    /// <summary>
    /// TextWriter that captures output while passing through to original.
    /// Automatically trims old content to prevent memory leaks during long runs.
    /// </summary>
    internal sealed class CapturingTextWriter : TextWriter
    {
        private readonly TextWriter _original;
        private readonly StringBuilder _capture;
        private readonly object _lock;
        private readonly int _maxSize;
        private readonly int _trimToSize;

        public CapturingTextWriter(TextWriter original, StringBuilder capture, object lockObj, int maxSize, int trimToSize)
        {
            _original = original;
            _capture = capture;
            _lock = lockObj;
            _maxSize = maxSize;
            _trimToSize = trimToSize;
        }

        public override Encoding Encoding => _original.Encoding;

        public override void Write(char value)
        {
            _original.Write(value);
            SafeAppend(value.ToString());
        }

        public override void Write(string? value)
        {
            _original.Write(value);
            SafeAppend(value);
        }

        public override void WriteLine(string? value)
        {
            _original.WriteLine(value);
            SafeAppend(value + Environment.NewLine);
        }

        public override void WriteLine()
        {
            _original.WriteLine();
            SafeAppend(Environment.NewLine);
        }

        private void SafeAppend(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            try
            {
                lock (_lock)
                {
                    _capture.Append(value);

                    // Trim if exceeded max size (keep most recent content)
                    if (_capture.Length > _maxSize)
                    {
                        int removeCount = _capture.Length - _trimToSize;

                        // Find the next newline after removeCount to keep logs clean
                        int newlineIndex = -1;
                        for (int i = removeCount; i < _capture.Length && i < removeCount + 1000; i++)
                        {
                            if (_capture[i] == '\n')
                            {
                                newlineIndex = i + 1;
                                break;
                            }
                        }

                        removeCount = newlineIndex > 0 ? newlineIndex : removeCount;
                        _capture.Remove(0, removeCount);
                        _capture.Insert(0, $"[... {removeCount:N0} chars trimmed ...]{Environment.NewLine}");
                    }
                }
            }
            catch
            {
                // Fail gracefully - logging should never crash the app
            }
        }
    }
}


