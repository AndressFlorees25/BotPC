using System.IO;
using SaludNotebook;

namespace BotPC.Acciones;

static class Archivos
{
    static readonly string Perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly string CarpetaRecibidos = Path.Combine(Perfil, "Downloads", "Desde Telegram");

    // Atajos para no tener que escribir rutas completas.
    static readonly Dictionary<string, string> Atajos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["escritorio"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        ["documentos"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ["descargas"] = Path.Combine(Perfil, "Downloads"),
        ["imagenes"] = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        ["imágenes"] = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        ["videos"] = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        ["juegos"] = Path.Combine(Perfil, "Juegos"),
    };

    public static string Resolver(string ruta)
    {
        ruta = ruta.Trim().Trim('"');
        if (ruta.Length == 0) return Perfil;
        var partes = ruta.Split(['\\', '/'], 2);
        if (Atajos.TryGetValue(partes[0], out var baseAtajo))
            return partes.Length > 1 ? Path.Combine(baseAtajo, partes[1]) : baseAtajo;
        return Path.IsPathRooted(ruta) ? ruta : Path.Combine(Perfil, ruta);
    }

    public static string Listar(string carpeta)
    {
        var dir = new DirectoryInfo(carpeta);
        var carpetas = dir.GetDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden)).OrderBy(d => d.Name).ToList();
        var archivos = dir.GetFiles().Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden)).OrderByDescending(f => f.LastWriteTime).ToList();

        var lineas = new List<string> { $"📂 {dir.FullName}", "" };
        lineas.AddRange(carpetas.Take(25).Select(d => $"📁 {d.Name}"));
        if (carpetas.Count > 25) lineas.Add($"   … y {carpetas.Count - 25} carpetas más");
        lineas.AddRange(archivos.Take(25).Select(f => $"📄 {f.Name}  ({Formato.Tamano(f.Length)})"));
        if (archivos.Count > 25) lineas.Add($"   … y {archivos.Count - 25} archivos más");
        if (carpetas.Count + archivos.Count == 0) lineas.Add("(vacía)");
        lineas.Add("");
        lineas.Add("Para pedir un archivo: /archivo " + Path.Combine(dir.FullName, "nombre.ext"));
        return string.Join("\n", lineas);
    }

    // Busca por nombre en las carpetas personales (no en todo el disco, para que sea rápido).
    public static List<FileInfo> Buscar(string texto)
    {
        var opciones = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
        var donde = new[] { "escritorio", "documentos", "descargas", "imagenes", "videos", "juegos" }
            .Select(a => Atajos[a]).Where(Directory.Exists).Distinct();
        return donde.SelectMany(d =>
            {
                try { return new DirectoryInfo(d).EnumerateFiles($"*{texto}*", opciones); }
                catch { return Enumerable.Empty<FileInfo>(); }
            })
            .Take(15)
            .ToList();
    }

    public static string RutaRecibido(string nombre)
    {
        Directory.CreateDirectory(CarpetaRecibidos);
        var ruta = Path.Combine(CarpetaRecibidos, string.Join("_", nombre.Split(Path.GetInvalidFileNameChars())));
        if (!File.Exists(ruta)) return ruta;
        var sinExt = Path.GetFileNameWithoutExtension(ruta);
        return Path.Combine(CarpetaRecibidos, $"{sinExt} ({DateTime.Now:HHmmss}){Path.GetExtension(ruta)}");
    }
}
