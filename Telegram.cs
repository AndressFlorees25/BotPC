using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BotPC;

// Cliente mínimo de la API de bots de Telegram (long polling).
public class Telegram(string token)
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    readonly string _api = $"https://api.telegram.org/bot{token}/";
    readonly string _archivos = $"https://api.telegram.org/file/bot{token}/";

    // Límites de la API para bots.
    public const long MaxSubida = 50L << 20;
    public const long MaxBajada = 20L << 20;

    public async Task<JsonArray> ObtenerNovedades(long desde, CancellationToken ct)
    {
        var r = await Llamar("getUpdates", new { offset = desde, timeout = 50, allowed_updates = new[] { "message", "callback_query" } }, ct);
        return r?.AsArray() ?? new JsonArray();
    }

    public Task Enviar(long chat, string texto, object? botones = null) =>
        Llamar("sendMessage", new { chat_id = chat, text = texto, reply_markup = botones, disable_web_page_preview = true });

    public Task Editar(long chat, long mensaje, string texto, object? botones = null) =>
        Llamar("editMessageText", new { chat_id = chat, message_id = mensaje, text = texto, reply_markup = botones });

    public Task ResponderBoton(string idConsulta, string? texto = null) =>
        Llamar("answerCallbackQuery", new { callback_query_id = idConsulta, text = texto });

    public Task EnviarFoto(long chat, byte[] imagen, string? texto = null) =>
        Subir("sendPhoto", chat, "photo", new ByteArrayContent(imagen), "foto.jpg", texto);

    public async Task EnviarArchivo(long chat, string ruta, string? texto = null)
    {
        await using var f = File.OpenRead(ruta);
        await Subir("sendDocument", chat, "document", new StreamContent(f), Path.GetFileName(ruta), texto);
    }

    public Task EstablecerComandos(IEnumerable<(string Comando, string Descripcion)> comandos) =>
        Llamar("setMyCommands", new { commands = comandos.Select(c => new { command = c.Comando, description = c.Descripcion }) });

    // Descarga un archivo que el usuario mandó al bot.
    public async Task Descargar(string idArchivo, string destino)
    {
        var info = await Llamar("getFile", new { file_id = idArchivo });
        var ruta = info?["file_path"]?.GetValue<string>() ?? throw new InvalidOperationException("Telegram no entregó el archivo.");
        await using var origen = await _http.GetStreamAsync(_archivos + ruta);
        await using var salida = File.Create(destino);
        await origen.CopyToAsync(salida);
    }

    async Task Subir(string metodo, long chat, string campo, HttpContent contenido, string nombre, string? texto)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chat.ToString()), "chat_id" },
            { contenido, campo, nombre },
        };
        if (texto != null) form.Add(new StringContent(texto), "caption");
        using var r = await _http.PostAsync(_api + metodo, form);
        await Revisar(r);
    }

    async Task<JsonNode?> Llamar(string metodo, object datos, CancellationToken ct = default)
    {
        using var r = await _http.PostAsJsonAsync(_api + metodo, datos,
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }, ct);
        return await Revisar(r);
    }

    static async Task<JsonNode?> Revisar(HttpResponseMessage r)
    {
        var json = JsonNode.Parse(await r.Content.ReadAsStringAsync());
        if (json?["ok"]?.GetValue<bool>() != true)
            throw new HttpRequestException($"Telegram: {json?["description"] ?? r.StatusCode.ToString()}");
        return json["result"];
    }
}
