using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

namespace GPXVideoTools
{
    internal class Logger
    {
        private static readonly object _lock = new object();
        private static bool _enableFileLog = false;
        private static string _logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GPXVideoTools", "debug.log"
            );

        // Activa / desactiva el archivo
        public static bool EnableFileLog
        {
            get => _enableFileLog;
            set
            {
                _enableFileLog = value;
                if (value) EnsureLogDirectory();
            }
        }

        // Log en la consola
        public static void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss:fff");
            string fullMessage = $"[LOG {timestamp}]: {message}";

            // 1. Mostrar en AutoCAD (línea de comandos)
            TryWriteToAutoCAD(fullMessage);

            if (_enableFileLog) WriteToFile(fullMessage);
        }

        // Error en la consola
        public static void Error(string message, Exception ex = null)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string fullMessage = $"[ERROR {timestamp}] {message}";
            if (ex != null)
                fullMessage += $"\n    → {ex.GetType().Name}: {ex.Message}\n    Stack: {ex.StackTrace?.Split('\n')[0]}";

            TryWriteToAutoCAD(fullMessage);

            if (_enableFileLog)
                WriteToFile(fullMessage);
        }

        private static void TryWriteToAutoCAD(string message)
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc?.Editor != null)
                {
                    doc.Editor.WriteMessage("\n" + message);
                }
            }
            catch
            {
                // Silencio: no hay doc o editor
            }
        }

        private static void EnsureLogDirectory()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath));
            }
            catch { /* ignorar */ }
        }

        private static void WriteToFile(string message)
        {
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, message + Environment.NewLine);
                }
                catch { /* ignorar errores de escritura */ }
            }
        }

        // Limpia el log (útil para pruebas)
        public static void Clear()
        {
            if (File.Exists(_logFilePath))
                File.WriteAllText(_logFilePath, "");
        }

        // Abre el archivo de log
        public static void OpenLogFile()
        {
            if (File.Exists(_logFilePath))
                System.Diagnostics.Process.Start("notepad.exe", _logFilePath);
        }
    }
}
