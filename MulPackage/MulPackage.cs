using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Threading;
using System.IO;

namespace MulPackage
{
    public class ReceiveMessage
    {
        string Name = "";
        NamedPipeServerStream Listener;
        Thread thrListener;
        bool ACTIVE = false;
        public EventHandler NewMessage;

        private void Transmit(EventArgs Args)
        {
            NewMessage?.Invoke(this, Args);
        }

        public ReceiveMessage(string NameOfPipe)
        {
            Name = NameOfPipe;
            Create();
        }

        private void Create()
        {
            try {
                if (Name != "") {
                    Listener = new NamedPipeServerStream(Name);
                }
            }
            catch {  }
            Listener = null;
        }

        public void Start() {
            if (Listener != null && !Listener.IsConnected)
            {
                thrListener = new Thread(new ThreadStart(Listening));
                thrListener.Start();
                ACTIVE = true;
            }
        }

        public void Disconnect()
        {
            ACTIVE = false;
            try { thrListener.Interrupt(); }
            catch { }
            try { Listener.Disconnect(); }
            catch { }
        }

        public void Finish() {
            ACTIVE = false;
            Listener.Close();
            Listener.Dispose();
            try { thrListener.Interrupt(); }
            catch { }
            thrListener = null;
            Listener = null;
        }

        private void Listening() {
            Listener.WaitForConnection();
            StreamReader Reader = new StreamReader(Listener);

            while (ACTIVE)
            {
                string Message = "";
                if ((Message = Reader.ReadToEnd()) != null) {
                    Transmit(new MessageContent(Message)); 
                    
                }
            }
        }
    }

    public class SendMessage
    {
        string Name = "";
        NamedPipeClientStream Sender;
        StreamWriter Writer;

        public SendMessage(string NameOfReceiverPipe)
        {
            Name = NameOfReceiverPipe;
            Connect();
        }

        private void Connect()
        {
            try {
                if (Name != "") {
                    Sender = new NamedPipeClientStream(Name);
                    Writer = new StreamWriter(Sender);
                }
            }
            catch { }
            Sender = null;
        }

        public void Finish()
        {
            Sender.Close();
            Sender.Dispose();
            Sender = null;
        }

        public bool Send(string Message)
        {
            try
            {
                if (Writer != null)
                {
                    Writer.WriteLine(Message);
                    Writer.Flush();
                    return true;
                }
                
            }
            catch { }
            return false;
        }

        
    }

    public class MessageContent : EventArgs
    {
        string Msg = null;

        public MessageContent(string Message)
        {
            Msg = Message;
        }

        public string Message
        {
            get { return Msg; }
        }
    }
}
