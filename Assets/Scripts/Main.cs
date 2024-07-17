using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public class Main : MonoBehaviour
{
    public int x;
    public int y;

    public int width = 200;
    public int height = 200;

    PipeClientMessageSource m_messageSource;
    PipeClientListener m_listener;
    Process m_process;

    readonly Dictionary<string, JToken> m_messageResults = new Dictionary<string, JToken>(64);

    private void OnEnable()
    {
        string pipeSrcName = "pipe.kiana.msg";
        string pipeListenerName = "pipe.kiana.res";

        // start process
        string fileName = "FormConsole.exe";
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"file {fileName} not exists");
            return;
        }
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = $"{pipeSrcName} {pipeListenerName}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        //m_process = Process.Start(startInfo);
        m_process = new Process
        {
            StartInfo = startInfo
        };
        m_process.OutputDataReceived += Process_OutputDataReceived;
        m_process.Start();
        m_process.BeginOutputReadLine();

        // then start pipe 
        StartCoroutine(DoConnect(pipeSrcName, pipeListenerName));
    }
    private void Process_OutputDataReceived(object sender, System.Diagnostics.DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            Debug.Log($"[Console] {e.Data}");
        }
    }
    IEnumerator DoConnect(string pipeSrcName, string pipeListenerName)
    {
        m_messageSource = new PipeClientMessageSource(pipeSrcName);
        m_listener = new PipeClientListener(HandleMessageResult, pipeListenerName);

        var _await = new WaitForSeconds(0.5f);
        yield return _await;
        for (float t = 0; !m_messageSource.Working && t < 3f; t += 0.5f)
        {
            m_messageSource.Connect();
            m_listener.Connect();
            yield return _await;
        }
        if (m_messageSource.Working)
        {
            string res = m_messageSource.Send(BuildMessageT("HWND", ApplicationWindow.ApplicationWindowHandle.ToInt64()));
            //string res = m_sender.Send(BuildMessage("OwnerProcessName", Process.GetCurrentProcess().ProcessName));
            print(ApplicationWindow.ApplicationWindowHandle.ToInt64());
            print($"connected: {res}");
        }

    }

    private void OnDisable()
    {
        // end pipe
        m_messageSource?.Send(@"{""msg"": ""Quit""}");
        m_messageSource?.Dispose();
        m_listener?.Dispose();
        // then end process
        m_process.WaitForExit(200);
        if (!m_process.HasExited)
        {
            m_process.Kill();
        }
        m_process.Dispose();
        m_process = null;
    }

    private void OnGUI()
    {
        if (m_messageSource == null || !m_messageSource.Working) { return; }

        Rect rect = new Rect(x, y, width, height);
        GUILayout.BeginArea(rect);
        GUILayout.Box("Commands: ");
        GUILayout.BeginVertical();

        if (GUILayout.Button("Hello world"))
        {
            //string result = m_sender.Send("Hello");
            string result = m_messageSource.Send(BuildMessage("Hello"));
            Debug.Log(result);
        }
        if (GUILayout.Button("MessageBox"))
        {
            StartCoroutine(MessageBoxCoroutine());
        }
        if (GUILayout.Button("OpenFile"))
        {
            // 跨进程操作处理为异步，返回一个操作编号，然后等待另一个进程上传编号对应的结果
            StartCoroutine(OpenFileCoroutine());
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    IEnumerator MessageBoxCoroutine()
    {
        string result = m_messageSource.Send(BuildMessage("MessageBox", new
        {
            text = "message context",
            caption = "Title",
            buttons = "YesNoCancel",
            icon = "Exclamation",
        }));
        Debug.Log(result);
        yield return null;

        var jObj = JObject.Parse(result);
        string msgId = jObj.Value<string>("msg");
        string msgResult = null;
        for (float t = 0; t < 10f; t += Time.deltaTime)
        {
            if (m_messageResults.TryGetValue(msgId, out var res))
            {
                msgResult = res.Value<string>();
                goto LoopEnd;
            }
            yield return null;
        }
        Debug.Log($"[MessageBox] Id({msgId}) 过期");

LoopEnd:
        Debug.Log($"MessageBox => {msgResult}");
    }

    IEnumerator OpenFileCoroutine()
    {
        string result = m_messageSource.Send(BuildMessage("OpenFile", new
        {
            Title = "选择文件",
            MultiSelect = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = "All files|*.*",
        }));
        //string file = NativeOpenFile("打开文件",
        //    "文件.txt",
        //    ".txt",
        //    "*.txt|文本文件|*.*|任意文件");

        if (string.IsNullOrEmpty(result)) { Debug.Log("操作取消"); }
        else
        {
            Debug.Log($"打开文件：{result}");

            yield return null;
            var jObj = JObject.Parse(result);
            string msgId = jObj.Value<string>("msg");
            for (float t = 0; t < 10f; t += Time.deltaTime)
            {
                if (m_messageResults.TryGetValue(msgId, out var res))
                {
                    switch (res.Type)
                    {
                        case JTokenType.Array:
                            string[] files = res.Values().Select(t => t.Value<string>()).ToArray();
                            Debug.Log($"OpenFile => [{string.Join(", ", files.Select(s => $"\"{s}\""))}]");
                            goto LoopEnd;
                        default:
                            // as string
                            string file = res.Value<string>();
                            Debug.Log($"OpenFile => \"{file}\"");
                            goto LoopEnd;
                    }
                }
                yield return null;
            }
            Debug.Log($"[OpenFile] Id({msgId}) 过期");
LoopEnd:
            yield break;
        }
    }

    string HandleMessageResult(string msg)
    {
        Debug.Log($"res = {msg}");
        try
        {
            var jObj = JToken.Parse(msg);
            var key = jObj.Value<string>("msg");
            var value = jObj["args"];
            m_messageResults.Add(key, value);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        return string.Empty;
    }

    /*
     * {
     *   "msg": "",
     *   "args": "valueOrObject",
     * }
     * 
     */

    static string BuildMessage(string head) => $"{{\"msg\": \"{head}\"}}";

    static string BuildMessageT<T>(string head, T arg) => arg switch
    {
        string _ => $"{{\"msg\": \"{head}\", \"args\": \"{arg}\"}}",
        _ => $"{{\"msg\": \"{head}\", \"args\": {arg}}}"
    };

    static string BuildMessage(string head, object args)
    {
        JObject obj = new JObject
        {
            { "msg", head },
            { "args", JToken.FromObject(args) },
        };
        return obj.ToString(Formatting.None);
    }

    public struct ApplicationWindow
    {
        static IntPtr applicationWindowHandle;
        public static IntPtr ApplicationWindowHandle => applicationWindowHandle;

        static ApplicationWindow()
        {
            static bool _call(IntPtr hWnd, IntPtr lParam)
            {
                // 检索此线程对应的第一个窗口
                applicationWindowHandle = hWnd;
                return false;
            }
            uint threadID = GetCurrentThreadId();
            if (threadID > 0)
            {
                EnumThreadWindows(threadID, _call, IntPtr.Zero);
                // 在主线程以外可能找不到窗口，使用回退的方法
                if (applicationWindowHandle == IntPtr.Zero)
                {
                    applicationWindowHandle = GetForegroundWindow();
                }
            }
        }

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern UInt32 GetCurrentThreadId();

        [return: MarshalAs(UnmanagedType.U1)]
        public delegate bool EnumThreadWndProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("User32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool EnumThreadWindows(UInt32 dwThreadId, EnumThreadWndProc lpfn, IntPtr lParam);

        [DllImport("User32.dll", EntryPoint = "GetForegroundWindow")]
        public static extern IntPtr GetForegroundWindow();
    }
}
