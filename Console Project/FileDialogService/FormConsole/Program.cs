using System.Diagnostics;
using System.Text.Json;
using MessageBox = System.Windows.Forms.MessageBox;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace FormConsole
{
    internal class Program
    {
        static bool s_working = false;
        static IntPtr s_hWnd;
        static IWin32Window? s_targetWindow;

        static PipeServerListener? s_pipeListener;
        static PipeServerMessageSource? s_pipeMessageSource;
        static string? s_messageResult;

        [STAThread]
        static void Main(string[] args)
        {
            string pipeListenerName = args.Length != 0 ? args[0] : "pipe.teriteri.serviceListener";
            Console.WriteLine($"listener name: {pipeListenerName}");
            s_pipeListener = new PipeServerListener(pipeListenerName)
            {
                getInput = OnMessage,
            };

            string pipeSenderName = args.Length > 1 ? args[1] : "pipe.teriteri.serviceSource";
            Console.WriteLine($"sender name: {pipeSenderName}");
            s_pipeMessageSource = new PipeServerMessageSource(pipeSenderName);

            s_working = true;
            var th = new Thread(() =>
            {
                while (s_working) { Thread.Sleep(50); }
                s_pipeListener.Dispose();
                s_pipeMessageSource.Dispose();
            })
            {
                IsBackground = false,
            };
            th.Start();
        }

        /*
         * {
         *   "msg": "",
         *   "args": [],
         * }
         * 
         */

        static string OnMessage(string message)
        {
            Console.WriteLine(message);

            JsonDocument jdoc;
            string head;
            bool hasArgs;
            JsonElement args;
            try
            {
                jdoc = JsonDocument.Parse(message);
                head = GetPropStr(jdoc.RootElement, "msg") ?? string.Empty;
                hasArgs = jdoc.RootElement.TryGetProperty("args", out args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
                return string.Empty;
            }

            s_messageResult = string.Empty;
            try
            {
                Console.WriteLine($"head = {head}");
                switch (head)
                {
                    case "Hello":
                        Thread.Sleep(1000);
                        s_messageResult = "World";
                        break;
                    case "Quit":
                        s_working = false;
                        break;
                    case "HWND":
                        if (hasArgs)
                        {
                            if (args.TryGetInt64(out long _hwndVal))
                            {
                                s_hWnd = new IntPtr(_hwndVal);
                                Console.WriteLine(s_hWnd.ToInt64());
                                s_targetWindow = new InternalWindowHandle(s_hWnd);
                                s_messageResult = s_targetWindow?.ToString() ?? "Nothing";
                            }
                        }
                        break;
                    case "OwnerProcessName":
                    {
                        string? processName = args.GetString();
                        if (processName != null)
                        {
                            var ps = Process.GetProcessesByName(processName);
                            s_hWnd = ps[0].MainWindowHandle;
                            Console.WriteLine(s_hWnd.ToInt64());
                            s_targetWindow = new InternalWindowHandle(s_hWnd);
                            s_messageResult = s_targetWindow?.ToString() ?? "Nothing";
                        }
                    }
                    break;
                    case "MessageBox":
                        s_messageResult = HandleMessageBox(args);
                        break;
                    case "OpenFile":
                        s_messageResult = HandleOpenFileThread(args);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
            }


            // fallback
            return s_messageResult;
        }

        static string HandleOpenFileThread(JsonElement args)
        {
            Console.WriteLine($"value kind => {args.ValueKind}");
            Console.WriteLine($"value => {args}");

            string _op = GetGlobalOperationId();
            var th = new Thread(() =>
            {
                using var openFile = new OpenFileDialog()
                {
                    Title = GetPropStr(args, "Title"),
                    Multiselect = GetPropBoolean(args, "MultiSelect") ?? false,
                    Filter = GetPropStr(args, "Filter"),
                    InitialDirectory = GetPropStr(args, "InitialDirectory"),
                };
                var res = openFile.ShowDialog(s_targetWindow);

                string resultJson;
                if (res == DialogResult.OK)
                {
                    if (openFile.Multiselect)
                    {
                        string[] fileNames = openFile.FileNames;
                        var resObj = new
                        {
                            msg = _op,
                            args = fileNames,
                        };
                        resultJson = JsonSerializer.Serialize(resObj);
                        Console.WriteLine($"result => [{string.Join(", ", fileNames)}] ({_op})");
                    }
                    else
                    {
                        string fileName = openFile.FileName;
                        var resObj = new
                        {
                            msg = _op,
                            args = fileName,
                        };
                        resultJson = JsonSerializer.Serialize(resObj);
                        Console.WriteLine($"result => \"{fileName}\" ({_op})");
                    }
                }
                else
                {
                    resultJson = BuildMessage(_op);
                    Console.WriteLine($"result => Cancel ({_op})");
                }
                s_pipeMessageSource?.Send(resultJson);
            })
            {
                IsBackground = true
            };
            // 激活会话框需要在 STA 线程，而且不能阻塞当前的线程
            // https://learn.microsoft.com/zh-cn/dotnet/api/system.threading.apartmentstate?view=netstandard-2.1
            // https://www.cnblogs.com/winzheng/archive/2008/12/02/1345656.html
            th.SetApartmentState(ApartmentState.STA);
            th.Start();

            return BuildMessage(_op);
        }

        static string HandleMessageBox(JsonElement args)
        {
            Console.WriteLine(args.ValueKind);
            Console.WriteLine(args);

            string _op = GetGlobalOperationId();
            DialogResult _res = DialogResult.None;

            var th = new Thread(() =>
            {
                switch (args.ValueKind)
                {
                    case JsonValueKind.Object:
                    {
                        _res = MessageBox.Show(s_targetWindow,
                           text: GetPropStr(args, "text") ?? string.Empty,
                           caption: GetPropStr(args, "caption") ?? string.Empty,
                           buttons: GetPropEnum<MessageBoxButtons>(args, "buttons") ?? MessageBoxButtons.OK,
                           icon: GetPropEnum<MessageBoxIcon>(args, "icon") ?? 0,
                           defaultButton: GetPropEnum<MessageBoxDefaultButton>(args, "defaultButton") ?? 0);
                    }
                    break;
                    case JsonValueKind.String:
                    {
                        _res = MessageBox.Show(s_targetWindow, args.GetString(), string.Empty, MessageBoxButtons.OK);
                    }
                    break;
                    default: s_messageResult = string.Empty; break;
                }
                var resObj = new
                {
                    msg = _op,
                    args = _res.ToString(),
                };
                Console.WriteLine($"result => \"{_res}\" ({_op})");
                string resultJson = JsonSerializer.Serialize(resObj);
                s_pipeMessageSource?.Send(resultJson);
            })
            {
                IsBackground = true
            };
            th.SetApartmentState(ApartmentState.STA);
            th.Start();

            return BuildMessage(_op);
        }

        static string? GetPropStr(JsonElement e, string t) => e.TryGetProperty(t, out var v)
            ? v.GetString() : null;
        static T? GetPropEnum<T>(JsonElement e, string t) where T : unmanaged, Enum => e.TryGetProperty(t, out var v)
            && Enum.TryParse(v.GetString(), out T ev) ? ev : null;

        static bool? GetPropBoolean(JsonElement e, string t)
        {
            if (e.TryGetProperty(t, out var v))
            {
                switch (v.ValueKind)
                {
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.Number: return v.GetInt64() != 0;
                    case JsonValueKind.String: return bool.TryParse(v.GetString(), out bool vb) ? vb : null;
                }
            }
            return null;
        }

        static string BuildMessage(string head) => $"{{\"msg\": \"{head}\"}}";


        static int s_opId = 0;
        static string GetGlobalOperationId()
        {
            unchecked
            {
                s_opId++;
            }
            return s_opId.ToString();
        }
    }
}
