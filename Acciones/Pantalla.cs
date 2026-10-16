using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace BotPC.Acciones;

static class Pantalla
{
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int indice);

    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    // Captura todas las pantallas juntas, en JPG para que pese poco.
    public static byte[] Capturar()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int ancho = GetSystemMetrics(SM_CXVIRTUALSCREEN), alto = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        using var imagen = new Bitmap(ancho, alto);
        using (var g = Graphics.FromImage(imagen))
            g.CopyFromScreen(x, y, 0, 0, imagen.Size);

        using var ms = new MemoryStream();
        var jpg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parametros = new EncoderParameters(1);
        parametros.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
        imagen.Save(ms, jpg, parametros);
        return ms.ToArray();
    }
}
