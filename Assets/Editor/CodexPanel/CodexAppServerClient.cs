using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VoxelDemo.Codex.Editor
{
    // Private stdio transport: no listening TCP port and no authentication tokens in Unity assets.
    public sealed class CodexAppServerClient : IDisposable
    {
        readonly ConcurrentQueue<Action> events = new ConcurrentQueue<Action>();
        readonly ConcurrentDictionary<long, TaskCompletionSource<JObject>> pending =
            new ConcurrentDictionary<long, TaskCompletionSource<JObject>>();
        readonly object writeLock = new object();
        Process process;
        WindowsChildJob job;
        long nextId;
        volatile bool disposed;
        public event Action<string, JObject> Notification;
        public event Action<JToken, string, JObject> ServerRequest;
        public event Action<string> Disconnected;

        public void Start(string executable, string project)
        {
            if (process != null) throw new InvalidOperationException("Already started.");
            if (!File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("请选择有效的 codex.exe。", executable);
            process = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable, Arguments = "app-server --listen stdio://",
                WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }, EnableRaisingEvents = true };
            process.OutputDataReceived += OnOutput;
            // Drain stderr to prevent deadlocks. Raw auth/diagnostic output is never exposed or persisted.
            process.ErrorDataReceived += IgnoreError;
            process.Exited += OnExit;
            if (!process.Start()) throw new IOException("无法启动 Codex。");
            try { job = new WindowsChildJob(process); }
            catch { Dispose(); throw; }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public void Pump()
        {
            int limit = 2000;
            while (limit-- > 0 && events.TryDequeue(out var callback)) callback();
        }

        void OnOutput(object sender, DataReceivedEventArgs args)
        {
            if (disposed || string.IsNullOrWhiteSpace(args.Data)) return;
            JObject message;
            try { message = JObject.Parse(args.Data); }
            catch (JsonException) { return; }
            var id = message["id"];
            var method = (string)message["method"];
            if (method != null)
            {
                var payload = message["params"] as JObject ?? new JObject();
                if (id != null)
                    events.Enqueue(() => ServerRequest?.Invoke(id, method, payload));
                else events.Enqueue(() => Notification?.Invoke(method, payload));
            }
            else if (id != null && long.TryParse(id.ToString(), out var requestId) &&
                pending.TryRemove(requestId, out var completion))
            {
                if (message["error"] != null)
                    completion.TrySetException(new IOException((string)message["error"]["message"] ?? "Codex 请求失败。"));
                else completion.TrySetResult(message["result"] as JObject ?? new JObject());
            }
        }

        static void IgnoreError(object sender, DataReceivedEventArgs args) { }

        void OnExit(object sender, EventArgs args)
        {
            if (disposed) return;
            FailPending("Codex 连接已结束，请重新连接。");
            events.Enqueue(() => Disconnected?.Invoke("Codex 连接已结束，请重新连接。"));
        }

        public async Task<JObject> RequestAsync(string method, JObject payload, int timeoutSeconds = 90)
        {
            long id = Interlocked.Increment(ref nextId);
            var completion = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = completion;
            try
            {
                Write(new JObject { ["id"] = id, ["method"] = method, ["params"] = payload ?? new JObject() });
                if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))).ConfigureAwait(false) != completion.Task)
                    throw new TimeoutException("Codex 响应超时。可重新连接后继续，避免重复发送同一任务。");
                return await completion.Task.ConfigureAwait(false);
            }
            finally { pending.TryRemove(id, out _); }
        }

        public void Notify(string method, JObject payload = null)
        { Write(new JObject { ["method"] = method, ["params"] = payload ?? new JObject() }); }

        public void Reply(JToken id, JObject result)
        { Write(new JObject { ["id"] = id.DeepClone(), ["result"] = result }); }

        public void ReplyUnsupported(JToken id)
        {
            Write(new JObject { ["id"] = id.DeepClone(), ["error"] = new JObject {
                ["code"] = -32601, ["message"] = "This Unity client does not support this request; no action was approved." } });
        }

        void Write(JObject message)
        {
            lock (writeLock)
            {
                if (disposed || process == null || process.HasExited) throw new IOException("Codex 未连接。");
                process.StandardInput.WriteLine(message.ToString(Formatting.None));
                process.StandardInput.Flush();
            }
        }

        void FailPending(string message)
        {
            foreach (var pair in pending)
                if (pending.TryRemove(pair.Key, out var source)) source.TrySetException(new IOException(message));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            FailPending("连接已关闭。");
            if (process != null)
            {
                process.OutputDataReceived -= OnOutput;
                process.ErrorDataReceived -= IgnoreError;
                process.Exited -= OnExit;
                try { process.StandardInput.Close(); } catch (Exception e) when (e is InvalidOperationException || e is IOException) { }
                job?.Dispose();
                try { if (!process.HasExited) process.Kill(); }
                catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }
        }
    }

    // The panel owns this one process tree. Closing/recompiling Unity cannot leave its commands running.
    sealed class WindowsChildJob : IDisposable
    {
        IntPtr handle;
        public WindowsChildJob(Process process)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) throw new IOException("无法创建 Codex 进程管理器。");
            var info = new ExtendedInfo();
            info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int size = Marshal.SizeOf(typeof(ExtendedInfo));
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, pointer, false);
                if (!SetInformationJobObject(handle, 9, pointer, (uint)size) || !AssignProcessToJobObject(handle, process.Handle))
                    throw new IOException("无法管理 Codex 子进程；连接已取消，避免遗留后台任务。");
            }
            catch { Dispose(); throw; }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
        [StructLayout(LayoutKind.Sequential)] struct BasicInfo
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoInfo
        { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedInfo
        {
            public BasicInfo BasicLimitInformation;
            public IoInfo IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr data, uint size);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    }
}
