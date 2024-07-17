using System.IO.Pipes;

namespace FormConsole
{
    internal class PipeServerMessageSource : IDisposable
    {
        public readonly string pipeName;

        readonly NamedPipeServerStream m_serverPipe;
        readonly StreamWriter m_writer;
        readonly StreamReader m_reader;

        bool m_working = false;

        public PipeServerMessageSource(string pipeName)
        {
            this.pipeName = pipeName;
            m_serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 16);
            m_writer = new StreamWriter(m_serverPipe);
            m_reader = new StreamReader(m_serverPipe);

            var th = new Thread(target =>
            {
                var @this = (PipeServerMessageSource)target!;
                @this.m_serverPipe.WaitForConnection();
                @this.m_working = true;
            })
            {
                IsBackground = true,
            };
            th.Start(this);
        }

        public void Dispose()
        {
            m_working = false;
            m_serverPipe.Disconnect();
            m_serverPipe.Dispose();
        }

        public string? Send(string message)
        {
            if (m_working)
            {
                m_writer.WriteLine(message);
                m_writer.Flush();
                // recieve?
                string? result = m_reader.ReadLine();
                m_serverPipe.Flush();
                return result;
            }
            return null;
        }
    }
}
