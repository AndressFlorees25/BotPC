using System.IO;
using System.Text.Json;

namespace BotPC;

// Ajustes del bot, guardados en %LOCALAPPDATA%\BotPC\config.json (fuera del repositorio: tiene el token).
public class Config
{
    public static readonly string Carpeta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BotPC");
    static readonly string Archivo = Path.Combine(Carpeta, "config.json");
    static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    public string Token { get; set; } = "";

    // Único chat de Telegram al que el bot obedece.
    public long ChatId { get; set; }

    // Mandar una foto de la cámara cuando alguien prende o desbloquea el PC.
    public bool FotoAlEncender { get; set; }

    // Para no repetir avisos de fallos ya informados.
    public DateTime UltimoFalloInformado { get; set; } = DateTime.Now;

    public static Config Cargar()
    {
        if (!File.Exists(Archivo))
            throw new FileNotFoundException("Falta la configuración del bot (token).", Archivo);
        return JsonSerializer.Deserialize<Config>(File.ReadAllText(Archivo), Opciones)
               ?? throw new InvalidDataException("config.json vacío.");
    }

    readonly Lock _bloqueo = new();

    public void Guardar()
    {
        lock (_bloqueo)
            File.WriteAllText(Archivo, JsonSerializer.Serialize(this, Opciones));
    }
}
