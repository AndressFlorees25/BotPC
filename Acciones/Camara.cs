using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace BotPC.Acciones;

static class Camara
{
    // Saca una foto con la cámara del notebook. Requiere que en Configuración > Privacidad > Cámara
    // esté permitido el acceso de las aplicaciones de escritorio.
    public static async Task<byte[]> Foto()
    {
        using var captura = new MediaCapture();
        await captura.InitializeAsync(new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Video,
            PhotoCaptureSource = PhotoCaptureSource.Auto,
        });

        // La primera imagen suele salir oscura mientras la cámara ajusta la exposición.
        await Task.Delay(1500);

        using var flujo = new InMemoryRandomAccessStream();
        await captura.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), flujo);

        var bytes = new byte[flujo.Size];
        using var lector = new DataReader(flujo.GetInputStreamAt(0));
        await lector.LoadAsync((uint)flujo.Size);
        lector.ReadBytes(bytes);
        return bytes;
    }
}
