using System.IO;

namespace BotPC;

static class Log
{
    static readonly string Archivo = Path.Combine(Config.Carpeta, "bot.log");
    static readonly Lock Bloqueo = new();
    const long MaxTamano = 1 << 20;

    public static void Escribir(string texto)
    {
        try
        {
            lock (Bloqueo)
            {
                Directory.CreateDirectory(Config.Carpeta);
                if (File.Exists(Archivo) && new FileInfo(Archivo).Length > MaxTamano)
                    File.Move(Archivo, Archivo + ".old", true);
                File.AppendAllText(Archivo, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {texto}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
