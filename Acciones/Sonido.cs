using System.Runtime.InteropServices;

namespace BotPC.Acciones;

// Volumen y música simulando las teclas multimedia del teclado.
static class Sonido
{
    [DllImport("user32.dll")]
    static extern void keybd_event(byte tecla, byte scan, uint flags, UIntPtr extra);

    const uint KEYUP = 0x2;
    const byte Silencio = 0xAD, Bajar = 0xAE, Subir = 0xAF, Siguiente = 0xB0, Anterior = 0xB1, PlayPausa = 0xB3;

    // Cada pulsación cambia el volumen un 2%.
    public static void SubirVolumen(int pasos = 5) => Pulsar(Subir, pasos);
    public static void BajarVolumen(int pasos = 5) => Pulsar(Bajar, pasos);
    public static void Silenciar() => Pulsar(Silencio);
    public static void PausarReanudar() => Pulsar(PlayPausa);
    public static void CancionSiguiente() => Pulsar(Siguiente);
    public static void CancionAnterior() => Pulsar(Anterior);

    static void Pulsar(byte tecla, int veces = 1)
    {
        for (var i = 0; i < veces; i++)
        {
            keybd_event(tecla, 0, 0, UIntPtr.Zero);
            keybd_event(tecla, 0, KEYUP, UIntPtr.Zero);
        }
    }
}
