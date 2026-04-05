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
        private readonly TextWriter original;
        private readonly StringBuilder capture;
        private readonly object lockObj;
        private readonly int maxSize;
        private readonly int trimToSize;

        public CapturingTextWriter(TextWriter original, StringBuilder capture, object lockObj, int maxSize, int trimToSize)
        {
            this.original = original;
            this.capture = capture;
            lockObj = lockObj;
            this.maxSize = maxSize;
            this.trimToSize = trimToSize;
        }

        public override Encoding Encoding => original.Encoding;

        public override void Write(char value)
        {
            original.Write(value);
            SafeAppend(value.ToString());
        }

        public override void Write(string? value)
        {
            original.Write(value);
            SafeAppend(value);
        }

        public override void WriteLine(string? value)
        {
            original.WriteLine(value);
            SafeAppend(value + Environment.NewLine);
        }

        public override void WriteLine()
        {
            original.WriteLine();
            SafeAppend(Environment.NewLine);
        }

        private void SafeAppend(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            try
            {
                lock (lockObj)
                {
                    capture.Append(value);

                    // Trim if exceeded max size (keep most recent content)
                    if (capture.Length > maxSize)
                    {
                        int removeCount = capture.Length - trimToSize;

                        // Find the next newline after removeCount to keep logs clean
                        int newlineIndex = -1;
                        for (int i = removeCount; i < capture.Length && i < removeCount + 1000; i++)
                        {
                            if (capture[i] == '\n')
                            {
                                newlineIndex = i + 1;
                                break;
                            }
                        }

                        removeCount = newlineIndex > 0 ? newlineIndex : removeCount;
                        capture.Remove(0, removeCount);
                        capture.Insert(0, $"[... {removeCount:N0} chars trimmed ...]{Environment.NewLine}");
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


