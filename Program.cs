using Microsoft.Win32;
using BotPC.Acciones;

namespace BotPC;

static class Program
{
    static async Task Main()
    {
        // Una sola copia del bot a la vez (si no, Telegram entregaría los mensajes a medias).
        using var unico = new Mutex(true, @"Global\BotPC", out var esPrimero);
        if (!esPrimero) return;

        Config config;
        try
        {
            config = Config.Cargar();
        }
        catch (Exception ex)
        {
            Log.Escribir($"No se pudo iniciar: {ex.Message}");
            return;
        }

        Log.Escribir("Bot iniciado");
        var tg = new Telegram(config.Token);
        var bot = new Bot(config, tg);
        var monitor = new Vigilante(config, bot.Avisar);
        using var cts = new CancellationTokenSource();

        // Desbloqueo de la sesión (alguien escribió la contraseña o PIN).
        SystemEvents.SessionSwitch += async (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
                await bot.AvisarConFoto($"🔓 Alguien desbloqueó tu PC ({DateTime.Now:HH:mm}).");
        };
        SystemEvents.PowerModeChanged += async (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) await bot.Avisar($"☀️ Tu PC despertó de la suspensión ({DateTime.Now:HH:mm}).");
        };

        // El bot arranca al iniciar sesión, así que esto equivale a "alguien prendió el PC".
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20)); // dar tiempo a que haya internet
            await bot.AvisarConFoto($"🟢 Tu PC se encendió y está listo ({DateTime.Now:HH:mm}). Escribe /ayuda para ver qué puedo hacer.");
        });

        await Task.WhenAll(bot.Ejecutar(cts.Token), monitor.Ejecutar(cts.Token));
    }
}
