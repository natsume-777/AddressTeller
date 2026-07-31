using System;
using System.Diagnostics;
using UnityEditor;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Interface used to notify the caller of ApplyAll/ValidateAll progress.
    /// </summary>
    public interface IProgressReporter
    {
        /// <summary>
        /// Reports progress. If this returns false, the caller returns the results accumulated so far
        /// and aborts processing.
        /// </summary>
        bool Report(int current, int total, string description);
    }

    /// <summary>
    /// A no-op <see cref="IProgressReporter"/> that always returns true.
    /// Used internally in place of callers that omit the progress argument.
    /// </summary>
    public sealed class NullProgressReporter : IProgressReporter
    {
        /// <summary>The shared singleton instance.</summary>
        public static readonly NullProgressReporter Instance = new NullProgressReporter();

        private NullProgressReporter()
        {
        }

        /// <summary>No-op; always returns true (never aborts processing).</summary>
        public bool Report(int current, int total, string description) => true;
    }

    /// <summary>
    /// An <see cref="IProgressReporter"/> that displays a progress bar via
    /// EditorUtility.DisplayCancelableProgressBar. Manage its lifetime with <c>using</c>; disposing it
    /// closes the progress bar. If not disposed, the progress bar remains visible and blocks the Editor
    /// UI, so callers must always dispose an instance (typically via <c>using</c>) once they are done
    /// with it, including on exception paths.
    /// Create a new instance per operation (<see cref="WasCancelled"/> cannot be reused).
    /// </summary>
    public sealed class EditorProgressReporter : IProgressReporter, IDisposable
    {
        // 毎アセットで DisplayCancelableProgressBar を呼ぶと描画コストが大きいため、
        // この間隔（ミリ秒）未満の連続呼び出しは描画とキャンセルチェックをスキップする。
        private const long MinIntervalMilliseconds = 100;

        private readonly string _title;
        private readonly Stopwatch _stopwatch;
        private bool _hasReportedOnce;

        /// <summary>True if the user cancelled the progress bar.</summary>
        public bool WasCancelled { get; private set; }

        /// <summary>Creates a reporter that shows a cancelable progress bar titled <paramref name="title"/>.</summary>
        public EditorProgressReporter(string title)
        {
            _title = title;
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Updates the progress bar, throttled to at most once per <c>100 ms</c>
        /// (the first and last call are always drawn). Returns false, and sets <see cref="WasCancelled"/>,
        /// if the user cancelled the progress bar.
        /// </summary>
        public bool Report(int current, int total, string description)
        {
            if (WasCancelled) return false;

            // 最初の1件と最後の1件は間引かず必ず描画する。
            var isLast = total > 0 && current >= total;
            if (!isLast && _hasReportedOnce && _stopwatch.ElapsedMilliseconds < MinIntervalMilliseconds)
                return true;

            _hasReportedOnce = true;
            _stopwatch.Restart();

            var progress = total > 0 ? (float)current / total : 0f;
            if (EditorUtility.DisplayCancelableProgressBar(_title, description, progress))
            {
                WasCancelled = true;
                return false;
            }

            return true;
        }

        /// <summary>Closes the progress bar. Must be called (typically via <c>using</c>) once the caller is done, including on exception paths.</summary>
        public void Dispose()
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
