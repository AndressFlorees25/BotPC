using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BotPC.Acciones;

static class Energia
{
    [DllImport("powrprof.dll", SetLastError = true)]
    static extern bool SetSuspendState(bool hibernar, bool forzar, bool desactivarDespertadores);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool LockWorkStation();

    public static void Apagar(int segundos = 5) => Shutdown($"/s /t {segundos}");
    public static void Reiniciar(int segundos = 5) => Shutdown($"/r /t {segundos}");

    // Apagado programado (Windows muestra su propio aviso antes de apagar).
    public static void ApagarEn(TimeSpan espera) => Shutdown($"/s /t {(int)espera.TotalSeconds}");

    public static bool CancelarApagado() => Shutdown("/a") == 0;

    public static void Suspender() => SetSuspendState(false, false, false);

    public static bool Bloquear() => LockWorkStation();

    static int Shutdown(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit(10000);
        return p.ExitCode;
    }
}
