using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Debug = UnityEngine.Debug;

/* 跨进程递送消息
 *   - 程序入口定为外部应用（比如 WPF 应用），外部应用作为服务端，Unity 应用作为客户端
 *   - 同步读写：服务端发指令，客户端接收后回传结果，服务端立即读取结果
 *     - 服务端随机时间发消息
 *     - 客户端始终监听读
 *     - 读取后立即（阻塞）返回结果
 *     - 客户端随后读消息
 *  - 异步读写？
 *     - 用同步读写立即返回一个序号
 *     - 另开一个从客户端发消息、服务端监听读的管道
 *   
 * 
 * 数据
 *   文本 vs 二进制
 *   简单起见单行指令直接使用文本？
 *   
 * ----
 * magic number
 * ----
 *   id : uint
 *   type : enum, short
 *   size : uint
 *   buffer : bytes
 * ----
 */

public class PipeClientMessageSource : IDisposable
{
    public readonly string pipeName;

    readonly NamedPipeClientStream m_pipe;
    readonly StreamWriter m_writer;
    readonly StreamReader m_reader;

    bool m_working = false;

    public PipeClientMessageSource(string pipeName = "pipe.teriteri.sender")
    {
        this.pipeName = pipeName;
        m_pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        m_reader = new StreamReader(m_pipe);
        m_writer = new StreamWriter(m_pipe);
    }

    public void Dispose()
    {
        m_working = false;
        m_pipe.Dispose();
    }

    public void Connect()
    {
        if (m_pipe.IsConnected)
        {
            return;
        }
        try
        {
            m_pipe.Connect(5000);
            m_working = m_pipe.IsConnected;
            return;
        }
        catch (TimeoutException)
        {
            // failed
            return;
        }
    }

    public string Send(string message)
    {
        if (m_working)
        {
            m_writer.WriteLine(message);
            m_writer.Flush();

            string result = m_reader.ReadLine();
            m_pipe.Flush();
            return result;
        }
        return null;
    }

    public bool Working => m_working;

    public bool IsConnected => m_working && m_pipe.IsConnected;
}

public class PipeClientListener : IDisposable
{
    public readonly string pipeName;

    readonly Func<string, string> m_handleMessage;
    readonly NamedPipeClientStream m_pipe;
    bool m_working = false;

    public PipeClientListener(Func<string, string> handleMessage, string pipeName = "pipe.teriteri.listener")
    {
        m_handleMessage = handleMessage;
        m_pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);

    }

    public void Dispose()
    {
        m_working = false;
        m_pipe.Close();
        m_pipe.Dispose();
    }

    public void Connect()
    {
        if (m_pipe.IsConnected) { return; }
        new Thread(target =>
        {
            var @this = (PipeClientListener)target;
            try
            {
                @this.m_pipe.Connect(3000);
                if (@this.m_pipe.IsConnected)
                {
                    @this.m_working = true;

                    var sr = new StreamReader(@this.m_pipe, Encoding.UTF8);
                    var sw = new StreamWriter(@this.m_pipe, Encoding.UTF8);
                    while (@this.m_working)
                    {
                        // read
                        string input;
                        if ((input = sr?.ReadLine()) != null)
                        {
                            string result = @this.HandleMessage(input);
                            @this.m_pipe.Flush();
                            sw.WriteLine(result);
                            sw.Flush();
                        }
                    }
                }
                else
                {
                    // time out
                }
            }
            catch (TimeoutException tmex)
            {
                // failed
                if (@this.m_working)
                {
                    Debug.LogException(tmex);
                }
            }
            catch (IOException ioex)
            {
                if (@this.m_working)
                {
                    Debug.LogException(ioex);
                }
            }
        })
        {
            IsBackground = true,
        }.Start(this);
    }

    public bool Working => m_working;

    public bool IsConnected => m_working && m_pipe.IsConnected;

    string HandleMessage(string message)
    {
        return m_handleMessage(message);
    }
}

