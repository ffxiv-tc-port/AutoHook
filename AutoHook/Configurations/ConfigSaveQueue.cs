using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Newtonsoft.Json;

namespace AutoHook.Configurations;

/// <summary>
/// 存檔合流：<see cref="Request"/> 只記下「要存」，任何執行緒都可以呼叫、不做任何 I/O；
/// 框架執行緒在最後一次要求後 <see cref="QuietMs"/>（連續要求最多拖 <see cref="MaxDelayMs"/>）才序列化一次，
/// 寫檔交給背景依序做（後送的不會被先送的蓋掉）。
/// </summary>
/// <remarks>
/// 契約：序列化只在框架執行緒（或卸載時）做——設定物件同時被框架執行緒改，序列化出來的位元組就是快照。
/// 刻意不先深拷貝再到背景序列化：深拷貝本身就是整份走訪一次，成本與序列化同級，省不到主執行緒。
/// 代價：要求後幾百毫秒內遊戲被強制結束＝那段改動沒落地；正常卸載會等最後一次寫完（有上限）。
/// </remarks>
internal static class ConfigSaveQueue
{
    private const long QuietMs = 250;
    private const long MaxDelayMs = 2000;
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static Configuration? _pending;
    private static long _firstRequestAt;
    private static long _lastRequestAt;
    private static bool _attached;
    private static MemoryStream? _buffer;

    private static readonly object WriteGate = new();
    private static byte[]? _queuedBytes;
    private static bool _writerRunning;
    private static Task _writer = Task.CompletedTask;

    public static void Initialize()
    {
        Service.Framework.Update += OnUpdate;
        _attached = true;
    }

    public static void Request(Configuration cfg)
    {
        // 順序是契約：先放設定、再蓋時間戳，OnUpdate 看到時間戳時一定拿得到這份設定。
        Volatile.Write(ref _pending, cfg);
        var now = Environment.TickCount64;
        Interlocked.Exchange(ref _lastRequestAt, now);
        Interlocked.CompareExchange(ref _firstRequestAt, now, 0);
    }

    /// <summary>卸載用：立刻序列化尚未存的那份，並等背景寫完（最多 <see cref="ShutdownWait"/>）。</summary>
    public static void Shutdown()
    {
        if (_attached)
        {
            Service.Framework.Update -= OnUpdate;
            _attached = false;
        }

        SaveNow();
        Volatile.Write(ref _buffer, null);

        Task writer;
        lock (WriteGate)
            writer = _writer;

        if (!writer.Wait(ShutdownWait))
            Service.PluginLog.Warning($"[AutoHook] 卸載時等設定檔寫入超過 {ShutdownWait.TotalSeconds:0} 秒，最後一次的設定可能沒有寫進檔案");
    }

    private static void OnUpdate(IFramework _)
    {
        var first = Interlocked.Read(ref _firstRequestAt);
        if (first == 0)
            return;

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastRequestAt) < QuietMs && now - first < MaxDelayMs)
            return;

        SaveNow();
    }

    private static void SaveNow()
    {
        // 先清時間戳再取設定：取完之後才進來的要求會重新蓋時間戳，不會被這次吞掉。
        Interlocked.Exchange(ref _firstRequestAt, 0);
        var cfg = Interlocked.Exchange(ref _pending, null);
        if (cfg == null)
            return;

        byte[] bytes;
        try
        {
            bytes = Serialize(cfg);
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e, "[AutoHook] 設定序列化失敗，這次沒有存檔");
            return;
        }

        lock (WriteGate)
        {
            _queuedBytes = bytes;
            if (_writerRunning)
                return;

            _writerRunning = true;
            _writer = Task.Run(WriteLoop);
        }
    }

    /// <summary>與 Dalamud <c>SavePluginConfig</c> 的序列化逐位元組相同（設定、縮排、UTF-8 無 BOM、根物件帶 <c>$type</c>）。</summary>
    internal static byte[] Serialize(Configuration cfg)
    {
        var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings
        {
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            TypeNameHandling = TypeNameHandling.Objects,
        });
        serializer.Formatting = Formatting.Indented;

        // 緩衝區留著重用（每次重配會多出一份整檔大小的配置）；同時有兩個序列化時後到的自己配一個。
        var buffer = Interlocked.Exchange(ref _buffer, null) ?? new MemoryStream();
        try
        {
            buffer.SetLength(0);
            using (var jsonWriter = new JsonTextWriter(new StreamWriter(buffer, Utf8NoBom, 1024, leaveOpen: true)))
            {
                jsonWriter.Formatting = serializer.Formatting;
                serializer.Serialize(jsonWriter, cfg, null);
            }

            return buffer.ToArray();
        }
        finally
        {
            Volatile.Write(ref _buffer, buffer);
        }
    }

    private static async Task WriteLoop()
    {
        while (true)
        {
            byte[]? bytes;
            lock (WriteGate)
            {
                bytes = _queuedBytes;
                _queuedBytes = null;
                if (bytes == null)
                {
                    _writerRunning = false;
                    return;
                }
            }

            await WriteFile(bytes).ConfigureAwait(false);
        }
    }

    // 走 Dalamud 的 IReliableFileStorage：與 SavePluginConfig 寫同一個檔、同一筆備份資料庫列。
    private static async Task WriteFile(byte[] bytes)
    {
        try
        {
            var path = Service.PluginInterface.ConfigFile.FullName;
#pragma warning disable Dalamud001
            if (bytes.LongLength <= Service.FileStorage.MaxFileSizeBytes)
                await Service.FileStorage.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
#pragma warning restore Dalamud001
            else
                FilesystemUtil.WriteAllBytesSafe(path, bytes);
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e, "[AutoHook] 設定檔寫入失敗（下一次存檔會整份重寫）");
        }
    }
}
