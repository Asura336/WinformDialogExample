using System.IO.Pipes;
using System.Text;

namespace FormConsole
{
    internal class PipeServerListener : IDisposable
    {
        public readonly string pipeName;

        readonly Thread m_server;
        readonly NamedPipeServerStream? m_serverPipe;

        bool m_working = false;

        public string? m_messageResult;
        public Func<string, string>? getInput;

        public PipeServerListener(string pipeName)
        {
            this.pipeName = pipeName;
            m_serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 16);

            m_server = new Thread(target =>
            {
                var @this = (PipeServerListener)target!;
                var pipe = @this.m_serverPipe!;
                try
                {
                    pipe.WaitForConnection();
                    @this.m_working = true;
                    //then...
                    using var sr = new StreamReader(pipe, Encoding.UTF8);
                    using var sw = new StreamWriter(pipe, Encoding.UTF8);
                    while (@this.m_working)
                    {
                        try
                        {
                            // read
                            string? input;
                            if ((input = sr.ReadLine()) != null)
                            {
                                @this.InputMessage(input);
                                Console.WriteLine($"input => {input}");
                                pipe.Flush();
                                // write
                                Console.WriteLine($"result => {@this.m_messageResult}");
                                sw.WriteLine(@this.m_messageResult);
                                @this.m_messageResult = null;
                                sw.Flush();
                            }
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
                catch (IOException)
                {
                    // pass
                }
                catch (ObjectDisposedException)
                {
                    // pass
                }
            })
            {
                IsBackground = true,
            };
            m_server.Start(this);
        }

        public void Dispose()
        {
            m_working = false;
            getInput = null;
            if (m_serverPipe != null && m_serverPipe.IsConnected)
            {
                m_serverPipe.Disconnect();
            }
            m_serverPipe?.Dispose();
        }

        void InputMessage(string message)
        {
            m_messageResult = getInput?.Invoke(message);
        }
    }
}
