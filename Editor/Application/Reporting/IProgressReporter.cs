using System;
using System.Diagnostics;
using UnityEditor;

namespace AddressTeller.Editor
{
    /// <summary>
    /// ApplyAll/ValidateAll の進捗を呼び出し元に通知するためのインターフェース。
    /// </summary>
    public interface IProgressReporter
    {
        /// <summary>
        /// 処理の進捗を報告する。false を返した場合、呼び出し元はその時点までの結果を返して処理を中断する。
        /// </summary>
        bool Report(int current, int total, string description);
    }

    /// <summary>
    /// 何もしない <see cref="IProgressReporter"/>。常に true を返す。
    /// progress 引数を省略した従来の呼び出しを内部的にこれに差し替える。
    /// </summary>
    public sealed class NullProgressReporter : IProgressReporter
    {
        public static readonly NullProgressReporter Instance = new NullProgressReporter();

        private NullProgressReporter()
        {
        }

        public bool Report(int current, int total, string description) => true;
    }

    /// <summary>
    /// EditorUtility.DisplayCancelableProgressBar を使ってプログレスバーを表示する <see cref="IProgressReporter"/>。
    /// using で生存期間を管理し、Dispose でプログレスバーを閉じる想定。
    /// 1回の処理につき新しいインスタンスを生成すること（<see cref="WasCancelled"/> は再利用不可）。
    /// </summary>
    public sealed class EditorProgressReporter : IProgressReporter, IDisposable
    {
        // 毎アセットで DisplayCancelableProgressBar を呼ぶと描画コストが大きいため、
        // この間隔（ミリ秒）未満の連続呼び出しは描画とキャンセルチェックをスキップする。
        private const long MinIntervalMilliseconds = 100;

        private readonly string _title;
        private readonly Stopwatch _stopwatch;
        private bool _hasReportedOnce;

        /// <summary>ユーザーがプログレスバーをキャンセルした場合 true。</summary>
        public bool WasCancelled { get; private set; }

        public EditorProgressReporter(string title)
        {
            _title = title;
            _stopwatch = Stopwatch.StartNew();
        }

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

        public void Dispose()
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
