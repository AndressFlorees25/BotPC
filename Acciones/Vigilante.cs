using System.IO;
using System.Runtime.InteropServices;
using SaludNotebook;
using SaludNotebook.Revisiones;

namespace BotPC.Acciones;

// Revisa el PC cada minuto y avisa por Telegram solo cuando pasa algo.
class Vigilante(Config config, Func<string, Task> avisar)
{
    static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);
    static readonly TimeSpan PausaCalor = TimeSpan.FromMinutes(15);
    static readonly TimeSpan PausaDisco = TimeSpan.FromHours(24);

    const int BateriaBaja = 15;
    const double GpuCaliente = 87, SistemaCaliente = 95, DiscoLleno = 10;

    bool _avisoBateriaBaja, _avisoBateriaLlena;
    DateTime _ultimoCalor = DateTime.MinValue, _ultimoDisco = DateTime.MinValue;

    public async Task Ejecutar(CancellationToken ct)
    {
        await Intentar(RevisarFallosNuevos);
        while (!ct.IsCancellationRequested)
        {
            await Intentar(RevisarBateria);
            await Intentar(RevisarTemperatura);
            await Intentar(RevisarDisco);
            try { await Task.Delay(Intervalo, ct); } catch (TaskCanceledException) { break; }
        }
    }

    static async Task Intentar(Func<Task> revision)
    {
        try { await revision(); }
        catch (Exception ex) { Log.Escribir($"Vigilante: {ex.Message}"); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS estado);

    public static (int Porcentaje, bool Enchufado)? LeerBateria()
    {
        if (!GetSystemPowerStatus(out var e) || e.BatteryLifePercent == 255 || (e.BatteryFlag & 128) != 0) return null;
        return (e.BatteryLifePercent, e.ACLineStatus == 1);
    }

    Task RevisarBateria()
    {
        if (LeerBateria() is not { } bateria) return Task.CompletedTask;
        var (pct, enchufado) = bateria;

        if (enchufado) _avisoBateriaBaja = false;
        if (pct < 90) _avisoBateriaLlena = false;

        if (!enchufado && pct <= BateriaBaja && !_avisoBateriaBaja)
        {
            _avisoBateriaBaja = true;
            return avisar($"🪫 Batería baja: {pct}%. Enchufa el cargador o guarda lo que estés haciendo.");
        }
        if (enchufado && pct >= 100 && !_avisoBateriaLlena)
        {
            _avisoBateriaLlena = true;
            return avisar("🔋 Batería al 100%. Si no lo vas a usar mucho rato, puedes desenchufarlo para cuidar la batería.");
        }
        return Task.CompletedTask;
    }

    Task RevisarTemperatura()
    {
        if (DateTime.Now - _ultimoCalor < PausaCalor) return Task.CompletedTask;
        var gpu = Temperatura.LeerGpus().FirstOrDefault();
        var sistema = Temperatura.LeerSistema();
        if ((gpu?.Temp ?? 0) < GpuCaliente && (sistema ?? 0) < SistemaCaliente) return Task.CompletedTask;

        _ultimoCalor = DateTime.Now;
        var partes = new List<string>();
        if (gpu != null) partes.Add($"tarjeta gráfica {gpu.Temp} °C");
        if (sistema is { } s) partes.Add($"sistema {s:0} °C");
        return avisar($"🔥 ¡Tu PC está muy caliente! ({string.Join(", ", partes)})\n" +
                      "Revisa que las rejillas no estén tapadas (no lo uses sobre la cama). Si pasa seguido, necesita limpieza.");
    }

    Task RevisarDisco()
    {
        if (DateTime.Now - _ultimoDisco < PausaDisco) return Task.CompletedTask;
        var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        var libre = c.AvailableFreeSpace * 100.0 / c.TotalSize;
        if (libre >= DiscoLleno) return Task.CompletedTask;

        _ultimoDisco = DateTime.Now;
        return avisar($"💾 El disco está casi lleno: quedan {c.AvailableFreeSpace / 1e9:0} GB ({libre:0}%).\n" +
                      "Abre Salud del Notebook > Liberar espacio para hacer lugar.");
    }

    // Pantallazos azules y apagados de golpe ocurridos desde la última vez que se avisó.
    async Task RevisarFallosNuevos()
    {
        var (_, fallos) = await Task.Run(Fallos.Revisar);
        var nuevos = fallos.Where(f => f.Fecha > config.UltimoFalloInformado && f.Tipo != "Apagado con el botón")
                           .OrderBy(f => f.Fecha).ToList();
        foreach (var f in nuevos)
            await avisar($"⚠️ {f.Tipo} ({f.FechaTexto})\nCausa: {f.Causa}\n{f.Explicacion}");

        config.UltimoFalloInformado = DateTime.Now;
        config.Guardar();
    }
}
