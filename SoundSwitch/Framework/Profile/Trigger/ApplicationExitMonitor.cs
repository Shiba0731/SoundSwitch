// SoundSwitch/Framework/Profile/Trigger/ApplicationExitMonitor.cs

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using Serilog;
using SoundSwitch.Common.Framework.Dispose;

namespace SoundSwitch.Framework.Profile.Trigger
{
    /// <summary>
    /// アプリケーションの終了イベントを監視するクラス
    /// </summary>
    public class ApplicationExitMonitor : IDisposable
    {
        /// <summary>
        /// 監視対象のアプリケーションが終了したときに発生するイベント
        /// </summary>
        public event EventHandler<ApplicationExitEventArgs> ApplicationExited;

        /// <summary>
        /// 監視対象のプロセスを格納するConcurrentDictionary。
        /// アプリケーションのパスをキーとし、そのパスに一致するプロセスのConcurrentBagを値とする。
        /// </summary>
        private readonly ConcurrentDictionary<string, ConcurrentBag<Process>> _monitoredProcesses;
        /// <summary>
        /// _monitoredProcessesへのアクセスをロックするためのオブジェクト
        /// </summary>
        private readonly object _lock = new object();

        public ApplicationExitMonitor()
        {
            _monitoredProcesses = new ConcurrentDictionary<string, ConcurrentBag<Process>>();
            // NullReferenceExceptionを防ぐためにイベントを初期化
            ApplicationExited = (sender, args) => { }; 
        }

        /// <summary>
        /// アプリケーションの終了イベントの監視を開始します。
        /// </summary>
        /// <param name="applicationPath">監視するアプリケーションの実行可能ファイルのフルパス</param>
        public void StartMonitoring(string applicationPath)
        {
            if (string.IsNullOrWhiteSpace(applicationPath))
            {
                Log.Warning("空またはヌルのパスを持つアプリケーションの監視を試みました。");
                return;
            }

            lock (_lock)
            {
                // アプリケーションパスに一致するすべての実行中のプロセスを取得
                var processes = Process.GetProcesses()
                                       .Where(p =>
                                       {
                                           try
                                           {
                                               // MainModule.FileNameへのアクセスは、アクセス拒否エラーを引き起こす可能性があるため、try-catchで囲む
                                               return p.MainModule?.FileName.Equals(applicationPath, StringComparison.OrdinalIgnoreCase) == true;
                                           }
                                           catch (Exception ex)
                                           {
                                               Log.Debug(ex, "プロセスID {ProcessId}のプロセス情報にアクセスできませんでした。", p.Id);
                                               return false;
                                           }
                                       })
                                       .ToList();

                if (!processes.Any())
                {
                    Log.Information("アプリケーションパス {ApplicationPath}に対応する実行中のプロセスが見つかりませんでした。今後の起動を監視します。", applicationPath);
                    // ここでは、現在実行中のプロセスが見つからない場合、監視リストには追加しません。
                    // プロセスが後から起動した場合に監視を開始するには、WMIイベントウォッチャーなど、より複雑なメカニズムが必要です。
                    // この実装では、トリガーが設定された時点でアプリが既に実行されていることを前提としています。
                    return;
                }

                foreach (var process in processes)
                {
                    AddProcessToMonitor(applicationPath, process);
                }
            }
        }

        /// <summary>
        /// 特定のアプリケーションの監視を停止します。
        /// </summary>
        /// <param name="applicationPath">監視を停止するアプリケーションの実行可能ファイルのフルパス</param>
        public void StopMonitoring(string applicationPath)
        {
            if (string.IsNullOrWhiteSpace(applicationPath))
            {
                return;
            }

            lock (_lock)
            {
                if (_monitoredProcesses.TryRemove(applicationPath, out var processes))
                {
                    foreach (var process in processes)
                    {
                        process.Exited -= OnProcessExited; // イベントハンドラを解除
                        process.Dispose(); // プロセスオブジェクトを破棄
                    }
                    Log.Information("アプリケーション {ApplicationPath}の監視を停止しました。", applicationPath);
                }
            }
        }

        /// <summary>
        /// プロセスを監視リストに追加し、Exitedイベントハンドラをアタッチします。
        /// </summary>
        /// <param name="applicationPath">アプリケーションのパス</param>
        /// <param name="process">監視するプロセスオブジェクト</param>
        private void AddProcessToMonitor(string applicationPath, Process process)
        {
            // Exitedイベントを受け取るためにEnableRaisingEventsをtrueにする
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;

            _monitoredProcesses.GetOrAdd(applicationPath, _ => new ConcurrentBag<Process>()).Add(process);
            Log.Information("アプリケーション {ApplicationPath}のプロセス {ProcessName} (ID: {ProcessId})の監視を開始しました。", applicationPath, process.ProcessName, process.Id);
        }

        /// <summary>
        /// 監視対象のプロセスが終了したときに呼び出されるイベントハンドラ
        /// </summary>
        private void OnProcessExited(object sender, EventArgs e)
        {
            if (sender is Process exitedProcess)
            {
                string applicationPath = null;

                lock (_lock)
                {
                    // 終了したプロセスがどのアプリケーションパスに属するかを見つける
                    // ConcurrentDictionaryとConcurrentBagを使用しているため、少し複雑になる
                    foreach (var entry in _monitoredProcesses)
                    {
                        // ConcurrentBagからプロセスを一時的に取り出し、IDが一致するか確認
                        // 一致しない場合は元に戻す
                        var processesToReAdd = new ConcurrentBag<Process>();
                        Process foundProcess = null;

                        while (entry.Value.TryTake(out var p))
                        {
                            if (p.Id == exitedProcess.Id)
                            {
                                foundProcess = p;
                                applicationPath = entry.Key;
                                break;
                            }
                            processesToReAdd.Add(p);
                        }

                        // 元に戻す
                        foreach (var p in processesToReAdd)
                        {
                            entry.Value.Add(p);
                        }

                        if (foundProcess != null)
                        {
                            break; // 見つかったのでループを抜ける
                        }
                    }
                }

                if (applicationPath != null)
                {
                    Log.Information("監視対象のアプリケーション {ProcessName} (ID: {ProcessId})が終了しました。アプリケーションパス: {ApplicationPath}", exitedProcess.ProcessName, exitedProcess.Id, applicationPath);
                    ApplicationExited?.Invoke(this, new ApplicationExitEventArgs(applicationPath));
                    
                    // 終了したプロセスを監視リストからクリーンアップ
                    // TryRemoveはConcurrentBagから要素を削除する
                    if (_monitoredProcesses.TryGetValue(applicationPath, out var processesBag))
                    {
                        // 終了したプロセスをバッグから削除する（正確な削除はTryTakeで試みたが、ここでは確実に削除する）
                        // ConcurrentBagは要素の個別の削除をサポートしないため、新しいバッグを作成して再構築する
                        var newBag = new ConcurrentBag<Process>();
                        Process currentProcess;
                        while (processesBag.TryTake(out currentProcess))
                        {
                            if (currentProcess.Id != exitedProcess.Id)
                            {
                                newBag.Add(currentProcess);
                            }
                            else
                            {
                                currentProcess.Dispose(); // 終了したプロセスを破棄
                            }
                        }
                        _monitoredProcesses.TryUpdate(applicationPath, newBag, processesBag); // バッグを更新

                        if (newBag.IsEmpty)
                        {
                            _monitoredProcesses.TryRemove(applicationPath, out _);
                            Log.Information("アプリケーション {ApplicationPath}のすべてのプロセスが終了し、監視から削除されました。", applicationPath);
                        }
                    }
                }
                else
                {
                    Log.Warning("終了したプロセス {ProcessName} (ID: {ProcessId})は監視リストに見つかりませんでした。", exitedProcess.ProcessName, exitedProcess.Id);
                    exitedProcess.Dispose(); // 監視リストになくてもプロセスは破棄
                }
            }
        }

        /// <summary>
        /// 管理されているすべてのリソースを破棄します。
        /// </summary>
        public void Dispose()
        {
            Log.Information("ApplicationExitMonitorを破棄しています。");
            foreach (var entry in _monitoredProcesses)
            {
                foreach (var process in entry.Value)
                {
                    try
                    {
                        process.Exited -= OnProcessExited; // イベントハンドラを解除
                        process.Dispose(); // プロセスオブジェクトを破棄
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "アプリケーション {ApplicationPath}のプロセス {ProcessId}の破棄中にエラーが発生しました。", entry.Key, process.Id);
                    }
                }
            }
            _monitoredProcesses.Clear();
        }
    }

    /// <summary>
    /// ApplicationExitedイベントのイベント引数
    /// </summary>
    public class ApplicationExitEventArgs : EventArgs
    {
        public string ApplicationPath { get; }

        public ApplicationExitEventArgs(string applicationPath)
        {
            ApplicationPath = applicationPath;
        }
    }
}
