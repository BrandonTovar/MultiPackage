using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Threading;
using System.IO;
using System.Data.SqlClient;
using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.Shared;

namespace MulPackage
{
    public class ReceiveMessage
    {
        string Name = "";
        NamedPipeServerStream Listener;
        Thread thrListener;
        bool ACTIVE = false;
        public bool isReady { get {return ACTIVE; } }
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

        public bool Create()
        {
            try {
                if (Name != "") {
                    Listener = new NamedPipeServerStream(Name);
                    return true;
                }
            }
            catch {  }
            Listener = null;
            return false;
        }

        public void Start() {
            if (Listener != null && !Listener.IsConnected)
            {
                Listening();
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
            try
            {
                Listener.WaitForConnection();


                string Message = null;
                while (Listener.IsConnected)
                {
                    List<byte> msg = new List<byte>();
                    Message = null;

                    while (true)
                    {
                        try
                        {
                            int nm = 0;
                            nm = Listener.ReadByte();
                            if (nm == 0 || nm == 10 || nm == 13) break;
                            msg.Add(Convert.ToByte(nm));
                        }
                        catch { break; }
                    }

                    Message = Encoding.UTF8.GetString(msg.ToArray()).Trim();
                    Message = Message.Replace("\n", string.Empty);
                    Message = Message.Replace("\r", string.Empty);

                    if (Message != null && Message != " ")
                    {
                        if (Message == "Disconnect") {
                            try { Listener.Disconnect(); } catch { }
                            Start();
                            break;
                        }
                        else if (Message.Length > 0) Transmit(new MessageContent(Message));
                        
                    }
                }
                //Listener.Close();
                try { Listener.Disconnect(); } catch { }
                Start();
            }
            catch (Exception e)
            {
                try { Listener.Disconnect(); } catch { }
                Start();
            }
        }
    } // Crea una instancia a la escucha de mensajes mediante Pipes

    public class SendMessage
    {
        string Name = "";
        NamedPipeClientStream Sender;
        StreamWriter Writer;
        public bool Connected = false;

        public SendMessage(string NameOfReceiverPipe)
        {
            Name = NameOfReceiverPipe;
        }

        public bool Connect()
        {
            try {
                if (Name != "" && Sender == null) {
                    Sender = new NamedPipeClientStream(Name);
                    Sender.Connect();
                    Writer = new StreamWriter(Sender);
                    Connected = true;
                    return true;
                }
            }
            catch { }
            Sender = null;
            return false;
        }

        public void Finish()
        {
            Send("Disconnect");
            Connected = false; 
            Sender.Close();
            Sender.Dispose();
            Sender = null;
        }

        public bool Send(string Message)
        {
            try
            {
                if (Sender != null && Writer != null)
                {
                    Writer.WriteLine(Message);
                    Writer.Flush();
                    return true;
                }
                
            }
            catch { }
            return false;
        }

        
    } // Envia mensajes usando Pipes a un proceso en espera

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
    } // Estructura de los mensajes enviados por Pipes entre procesos

    public static class SQL // Clase para realizar consultas momentaneas a la base de datos
    {
        /// <summary>
        /// Crea una conexion a una base de datos y realiza una consulta. sin realizar una conexion persistente.
        /// </summary>
        /// <param name="Cred">Credenciales de la base de datos.</param>
        /// <param name="Query">Query sql que se ejecutara.</param>
        /// <returns>Devuelve una lista con todos los valores obtenidos.</returns>
        public static List<object[]> Excute(ConnectionInfo Cred, string Query)
        {
            SQLConnection SQL = new SQLConnection(Cred);
            List<object[]> Table = SQL.Excute(Query);
            SQL.Close();
            return Table;

        }

        public static bool TestConnection(ConnectionInfo Cred)
        {
            SQLConnection SQL = new SQLConnection(Cred);
            return SQL.TestConnection();
        }
    }

    public class SQLConnection // Conexion continua con la base de datos, la conexion no se cierra a menos que se solicite
    {
        #region
        public string E_SQL_OPEN = "Ocurrio un error al intentar abrir una conexion";
        public string E_SQL_QUERY = "Ocurrio un error al realizar la consulta";
        #endregion  

        SqlConnection Conn;

        /// <summary>
        /// Esta clase sirve para realizar una conexion persistente con la base de datos. y 
        /// permite realizar consultas.
        /// </summary>
        /// <param name="Cred">Credenciales de la base de datos.</param>
        public SQLConnection(ConnectionInfo Cred)
        {
            var StrConn = new SqlConnectionStringBuilder();
            StrConn.UserID = Cred.UserID;
            StrConn.Password = Cred.Password;
            StrConn.DataSource = Cred.ServerName;
            StrConn.InitialCatalog = Cred.DatabaseName;
            StrConn.ApplicationName = "Generador de Documentos";

            Conn = new SqlConnection(StrConn.ConnectionString);
            Open();
        }

        /// <summary>
        /// Ejecuta una query sql y devuelve una lisa con todos los valores obtenidos.
        /// </summary>
        /// <param name="Query">Query sql</param>
        /// <returns>Devuelve una lista con todos los valores obtenidos.</returns>
        public List<object[]> Excute(string Query)
        {
            try
            {
                SqlCommand Comm = new SqlCommand(Query, Conn);
                SqlDataReader Result = Comm.ExecuteReader();

                List<object[]> Table = new List<object[]>();

                if (Result.HasRows)
                    while (Result.Read())
                    {
                        object[] Values = new object[Result.FieldCount];
                        Result.GetValues(Values);
                        Table.Add(Values);
                    }

                Result.Close();
                return Table;
            }
            catch (CrystalReportsException CrException) { Log.Write(E_SQL_QUERY, CrException.Message); }

            return new List<object[]>();
        }

        public void Open()
        {
            try { Conn.Open(); }
            catch (CrystalReportsException CrException) { Log.Write(E_SQL_OPEN, CrException.Message); }
        }

        public bool TestConnection()
        {
            if (Conn != null && Conn.State == System.Data.ConnectionState.Open) return true;
            try { Conn.Open(); return true; }
            catch { return false; }
        }

        public void Close()
        {
            if (Conn != null) Conn.Close();
            Conn.Dispose();
        }
    }

    public class Report
    {
        #region ErrorCode
        private string E_RPT_LOAD = "Ocurrio un error al cargar el reporte";
        private string E_RPT_EXP = "Ocurrio un error al intentar exportar el reporte";
        #endregion

        // Exportar reportes rpt to pdf
        ReportDocument Document;
        string FileName = "report.pdf";
        List<object[]> Fields = new List<object[]>();
        public ExportFormatType ExpType = ExportFormatType.PortableDocFormat;
        public string OldFiles = "";
        public string nameRPT { get { try { return Document.FileName; } catch { return ""; } } }
        public bool isLoad { get { try { return Document.IsLoaded; } catch { return false; } } }

        /// <summary>
        /// Esta clase permite inicializar una instancia de CrystalReports y actualizar campos,
        /// y exportar los documentos a disco local o stream.
        /// </summary>
        /// <param name="RPT">Archivo .rpt que usara CrystalReports</param>
        /// <param name="Cred">Credenciales que usara para conectarse a la base de datos.</param>
        public Report(string RPT, ConnectionInfo Cred)
        {
            if (File.Exists(RPT) && new FileInfo(RPT).Extension == ".rpt")
            {
                try
                {
                    Document = new ReportDocument();
                    Document.Load(RPT, OpenReportMethod.OpenReportByTempCopy);
                    DataSourceConnections Conn = Document.DataSourceConnections;
                    foreach (IConnectionInfo item in Conn)
                        item.SetConnection(Cred.ServerName, Cred.DatabaseName, Cred.UserID, Cred.Password);
                    GetFields();
                    FileName = Document.Name + ".pdf";
                    Document.VerifyDatabase();
                }
                catch (CrystalReportsException CrException)
                {
                    Log.Write(E_RPT_LOAD, CrException.Message);
                }

            }
        }

        /// <summary>
        /// Obten los parametros que el reporte necesita para funcionar.
        /// </summary>
        /// <returns>Lista de parametros del reporte.</returns>
        public List<object[]> GetFields()
        {
            List<object[]> Fields = new List<object[]>();
            foreach (ParameterField item in Document.ParameterFields)
                Fields.Add(new object[2] { item.Name, item.ParameterValueType });

            return this.Fields = Fields;
        }

        /// <summary>
        /// Establece el valor de un parametro por nombre.
        /// </summary>
        /// <param name="Field">Nombre del parametro.</param>
        /// <param name="Value">Valor que se le asignara.</param>
        public void SetField(string Field, object Value)
        {
            string Name;
            ParameterValueKind Type;

            foreach (object[] Data in Fields)
            {
                Name = (string)Data[0];
                Type = (ParameterValueKind)Data[1];

                if (Name == Field) SetValue(Name, Value, Type);
            }
        }

        private bool SetValue(string Name, object Value, ParameterValueKind Type)
        {
            switch (Type)
            {
                // Instancie typos para las validaciones.
                case ParameterValueKind.BooleanParameter:
                    if (Value.GetType() != true.GetType()) return false;
                    break;
                case ParameterValueKind.CurrencyParameter:
                    if (Value.GetType() != (0.0).GetType()) return false;
                    break;
                case ParameterValueKind.DateParameter:
                    if (Value.GetType() != DateTime.Now.Date.GetType()) return false;
                    break;
                case ParameterValueKind.DateTimeParameter:
                    if (Value.GetType() != DateTime.Now.GetType()) return false;
                    break;
                case ParameterValueKind.NumberParameter:
                    if (Value.GetType() != (0).GetType()) return false;
                    break;
                case ParameterValueKind.StringParameter:
                    if (Value.GetType() != ("").GetType()) return false;
                    break;
                case ParameterValueKind.TimeParameter:
                    if (Value.GetType() != new TimeSpan().GetType()) return false;
                    break;
            }

            Document.SetParameterValue(Name, Value);
            return true;
        }

        /// <summary>
        /// Exporta el archivo directamente a pdf en disco, este metodo asigna el parametro
        /// en linea.
        /// </summary>
        /// <param name="Field">Nombre del parametro.</param>
        /// <param name="Value">Valor que se le asignara al parametro.</param>
        /// <param name="FileName">El nombre del archivo que se exportara.</param>
        public void Export(string Field, object Value, string FileName)
        {
            SetField(Field, Value);
            Export(FileName);
        }

        /// <summary>
        /// Exporta a disco el archivo en formato pdf utilizando los parametros asignados manualmente.
        /// </summary>
        /// <param name="FileName">El nombre y ruta del archivo para guardar. </param>
        public void Export(string FileName)
        {
            try { Document.ExportToDisk(ExpType, FileName); }
            catch (CrystalReportsException CrException) { Log.Write(E_RPT_EXP, CrException.Message); }
        }

        public void Export() => Export(FileName);

        /// <summary>
        /// Genera el documento y almacenalo en un stream.
        /// </summary>
        /// <returns>Devuelve un stream que contendra el archivo generado</returns>
        public Stream ToStream()
        {
            try { return Document.ExportToStream(ExpType); }
            catch (CrystalReportsException CrException) { Log.Write(E_RPT_EXP, CrException.Message); }
            return null;
        }

        /// <summary>
        /// Genera el archivo a pdf asignando a un parametro su valor.
        /// </summary>
        /// <param name="Field">El nombre del parametro.</param>
        /// <param name="Value">El valor que se asignara.</param>
        public void ToStream(string Field, object Value)
        {
            SetField(Field, Value);
            ToStream();
        }

        public void Close()
        {
            Document.Close();
            Document.Dispose();
            FileName = null;
            Fields.Clear();
            Fields = null;
            OldFiles = null;
        }

    }

    public class Documents
    {
        bool isOpen = false;
        StreamWriter Document = null;

        /// <summary>
        /// Este metodo creara el archivo y escribira el contenido en el, si ya existe 
        /// se sobreescribira y si no se creara, este metodo no genera una intancia 
        /// persistente del documento y no lo mantiene abierto.
        /// </summary>
        /// <param name="FileName">Nombre y ruta completa del archivo</param>
        /// <param name="Content">El contenido que se escribira en el archivo.</param>
        public static void Write(string FileName, string Content)
        {
            StreamWriter Document = Create(FileName);
            if (Document != null)
            {
                Document.WriteLine(Content);
                Document.Flush();
                Document.Close();
                Document.Dispose();
            }
        }

        public static StreamWriter Create(string FileName)
        {
            StreamWriter Document = null;
            string LocalPath = new FileInfo(FileName).DirectoryName;
            string Name = new FileInfo(FileName).Name;

            if (!Directory.Exists(LocalPath))
                try { Directory.CreateDirectory(LocalPath); }
                catch
                {
                    try { LocalPath = Path.Combine(Path.GetTempPath(), @"Multielectrico\Documents"); }
                    catch { LocalPath = ""; }
                }

            if (LocalPath != "") Document = new StreamWriter(Path.Combine(LocalPath, Name));

            return Document;
        }

        /// <summary>
        /// Este metodo escribira en el archivo el contenido, es necesaria una instancia 
        /// del archivo para escribir en el.
        /// </summary>
        /// <param name="Content">El cotenido que se escribira en el archivo.</param>
        public void Write(string Content)
        {
            if (isOpen && Document != null)
            {
                Document.Write(Content);
                Document.Flush();
            }
        }

        public void WriteLine(string Content)
        {
            if (isOpen && Document != null)
            {
                Document.WriteLine(Content);
                Document.Flush();
            }
        }

        public void Open(string FileName)
        {
            Document = Create(FileName);
            if (Document != null) isOpen = true;
        }

        /// <summary>
        /// Esta clase sirve para crear un archivo y escribir en el, si el archivo
        /// ya existe lo sobreescribira, si no existe lo creara y escribira en el.
        /// </summary>
        /// <param name="FileName">El nombre del archvo que se creara junto con ruta, la ruta por omision 
        /// sera la ruta actual del programa y en caso de fallar lo creara en la carpeta %temp% de windows
        /// </param>
        public Documents(string FileName) { Open(FileName); }

        public void Close()
        {
            if (isOpen && Document != null) Document.Close();
            try { Document.Dispose(); }
            catch { }
        }

        public Documents() { }

    }

    public class MemoryDocument
    {
        bool isOpen = false;
        MemoryStream Document = null;

        /// <summary>
        /// Este metodo creara un archivo mediante un stream en memoria, ya creado 
        /// escribira el contenido que se le pase.
        /// </summary>
        /// <param name="Content">Informacion que se escribira en el stream.</param>
        /// <returns></returns>
        public static MemoryStream CreateFile(string Content)
        {
            MemoryStream Document = Create();
            if (Document != null)
            {
                byte[] Text = Encoding.UTF8.GetBytes(Content);
                Document.Write(Text, 0, Text.Length);
                Document.Flush();
                Document.Close();
            }

            return Document;

        }

        public static MemoryStream Create()
        {
            return new MemoryStream();
        }

        public void Write(string Content)
        {
            if (isOpen && Document != null)
            {
                byte[] Text = Encoding.UTF8.GetBytes(Content);
                Document.Write(Text, (int)Document.Length, Text.Length);
                Document.Flush();
            }
        }

        public void WriteLine(string Content) => Write("\n\r" + Content);

        /// <summary>
        /// Esta clase sirve para crear archivos en memoria y escribir en ellos, 
        /// se toman medidas contra errores asi que nunca creara una excepcion.
        /// </summary>
        public MemoryDocument()
        {
            Document = Create();
            if (Document != null) isOpen = true;
        }

        public void Close()
        {
            try { Document.Dispose(); }
            catch { }
        }

    }

    public static class Log
    {
        /// <summary>
        /// Este metodo intentara crear un archivo .txt con nombre fecha actual, si ya existe 
        /// agregara contenido en el existente.
        /// </summary>
        /// <param name="Msg">El mensaje que se escribira en el log.</param>
        /// <param name="Exception">El mensaje que produjo la excepcion</param>
        public static void Write(string Msg, string Exception)
        {
            StreamWriter LogFile = Create();
            if (LogFile != null)
            {
                LogFile.WriteLine(DateTime.Now + "\t" +
                    Msg.Replace("\r\n", string.Empty) + "\t" +
                    Exception.Replace("\r\n", string.Empty)
                  );
                LogFile.Flush();
                LogFile.Close();
            }
            try { LogFile.Dispose(); } catch { }
        }

        /// <summary>
        /// Este metodo creara un archivo .txt con nombre fecha actual y devolvera un stream 
        /// para escribir en el.
        /// </summary>
        /// <returns></returns>
        public static StreamWriter Create()
        {
            StreamWriter LogFile = null;

            try
            {

                string LocalPath = Path.Combine(Directory.GetCurrentDirectory(), "Log");

                if (!Directory.Exists(LocalPath))
                    try { Directory.CreateDirectory(LocalPath); }
                    catch
                    {
                        try { LocalPath = Path.Combine(Path.GetTempPath(), @"Multielectrico\Log"); }
                        catch { LocalPath = ""; }
                    }

                if (LocalPath != "") LogFile = new StreamWriter(Path.Combine(LocalPath, Name), true);
            }
            catch { }

            return LogFile;
        }

        static string Name
        {
            get { return DateTime.Now.ToLongDateString() + ".txt"; }
        }
    }

}
