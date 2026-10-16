using System.IO;
using System.Management;
using System.Text.Json.Nodes;
using BotPC.Acciones;
using SaludNotebook;
using SaludNotebook.Revisiones;

namespace BotPC;

// Recibe los mensajes de Telegram y ejecuta lo que se pide. Solo obedece al dueño (config.ChatId).
class Bot(Config config, Telegram tg)
{
    long Chat => config.ChatId;

    // Rutas y procesos mostrados en botones (los datos de un botón tienen máximo 64 bytes).
    readonly Dictionary<int, string> _rutas = new();
    int _siguienteRuta;
    readonly Dictionary<string, ProgramaAbierto> _programas = new();
    bool _avisoIntruso;

    static readonly object Menu = new
    {
        keyboard = new[]
        {
            new[] { "📊 Estado", "🌡 Temperatura" },
            new[] { "📸 Captura", "📷 Cámara" },
            new[] { "📋 Programas", "🔌 Energía" },
            new[] { "🔊 Sonido", "📁 Archivos" },
            new[] { "⚙ Ajustes", "❓ Ayuda" },
        },
        resize_keyboard = true,
        is_persistent = true,
    };

    static readonly (string, string)[] Comandos =
    {
        ("estado", "Resumen de salud del PC"),
        ("temperatura", "Temperaturas ahora"),
        ("bateria", "Carga y vida útil de la batería"),
        ("recursos", "Procesador, RAM y disco"),
        ("captura", "Foto de la pantalla"),
        ("camara", "Foto con la cámara"),
        ("programas", "Programas abiertos (para cerrarlos)"),
        ("energia", "Apagar, reiniciar, suspender, bloquear"),
        ("apagar_en", "Apagar en X minutos (ej: /apagar_en 60)"),
        ("cancelar", "Cancelar el apagado programado"),
        ("sonido", "Volumen y música"),
        ("carpeta", "Ver una carpeta (ej: /carpeta descargas)"),
        ("archivo", "Pedir un archivo del PC"),
        ("buscar", "Buscar un archivo por nombre"),
        ("informe", "Informe PDF de salud"),
        ("ajustes", "Opciones del bot"),
        ("ayuda", "Qué puedo hacer"),
    };

    public async Task Ejecutar(CancellationToken ct)
    {
        await Reintentar(() => tg.EstablecerComandos(Comandos), ct);
        long desde = 0;
        while (!ct.IsCancellationRequested)
        {
            JsonArray novedades;
            try
            {
                novedades = await tg.ObtenerNovedades(desde, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Sin internet (ej. recién encendido): se reintenta.
                Log.Escribir($"getUpdates: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                continue;
            }

            foreach (var u in novedades)
            {
                desde = u!["update_id"]!.GetValue<long>() + 1;
                try
                {
                    if (u["message"] is JsonObject m) await AlRecibirMensaje(m);
                    else if (u["callback_query"] is JsonObject c) await AlTocarBoton(c);
                }
                catch (Exception ex)
                {
                    Log.Escribir($"Error atendiendo {u.ToJsonString()}: {ex}");
                    await EnviarSeguro($"😕 Algo falló: {ex.Message}");
                }
            }
        }
    }

    public async Task Avisar(string texto) => await EnviarSeguro(texto);

    public async Task AvisarConFoto(string texto)
    {
        await EnviarSeguro(texto);
        if (!config.FotoAlEncender) return;
        try
        {
            await tg.EnviarFoto(Chat, await Camara.Foto(), "📷 Quien está frente al PC");
        }
        catch (Exception ex)
        {
            await EnviarSeguro($"(No pude sacar la foto: {ex.Message})");
        }
    }

    async Task EnviarSeguro(string texto, object? botones = null)
    {
        try { await tg.Enviar(Chat, texto, botones); }
        catch (Exception ex) { Log.Escribir($"sendMessage: {ex.Message}"); }
    }

    static async Task Reintentar(Func<Task> accion, CancellationToken ct)
    {
        for (var i = 0; i < 30 && !ct.IsCancellationRequested; i++)
        {
            try { await accion(); return; }
            catch { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
        }
    }

    // ==================== Mensajes ====================

    async Task AlRecibirMensaje(JsonObject m)
    {
        var de = m["from"]?["id"]?.GetValue<long>() ?? 0;
        var chat = m["chat"]?["id"]?.GetValue<long>() ?? 0;
        if (de != Chat || chat != Chat)
        {
            await AvisarIntruso(m["from"]);
            return;
        }

        if (m["document"] is JsonObject doc)
        {
            await GuardarRecibido(doc["file_id"]!.GetValue<string>(), doc["file_name"]?.GetValue<string>() ?? "archivo",
                                  doc["file_size"]?.GetValue<long>() ?? 0);
            return;
        }
        if (m["photo"] is JsonArray fotos && fotos.Count > 0)
        {
            var mayor = fotos[^1]!;
            await GuardarRecibido(mayor["file_id"]!.GetValue<string>(), $"foto {DateTime.Now:yyyy-MM-dd HH-mm-ss}.jpg",
                                  mayor["file_size"]?.GetValue<long>() ?? 0);
            return;
        }
        foreach (var tipo in new[] { "video", "audio", "voice" })
        {
            if (m[tipo] is not JsonObject media) continue;
            var ext = tipo == "video" ? "mp4" : "ogg";
            await GuardarRecibido(media["file_id"]!.GetValue<string>(),
                                  media["file_name"]?.GetValue<string>() ?? $"{tipo} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.{ext}",
                                  media["file_size"]?.GetValue<long>() ?? 0);
            return;
        }

        var texto = m["text"]?.GetValue<string>()?.Trim() ?? "";
        var (comando, arg) = Separar(texto);
        switch (comando)
        {
            case "start" or "ayuda" or "❓ ayuda": await Ayuda(); break;
            case "estado" or "📊 estado": await MostrarEstado(); break;
            case "temperatura" or "🌡 temperatura": await Temperaturas(); break;
            case "bateria" or "batería": await BateriaInfo(); break;
            case "recursos": await Recursos(); break;
            case "captura" or "📸 captura": await Captura(); break;
            case "camara" or "cámara" or "📷 cámara": await FotoCamara(); break;
            case "programas" or "📋 programas": await ListarProgramas(); break;
            case "energia" or "energía" or "🔌 energía": await MenuEnergia(); break;
            case "apagar_en": await ApagarEn(arg); break;
            case "cancelar": await CancelarApagado(); break;
            case "sonido" or "🔊 sonido": await MenuSonido(); break;
            case "archivos" or "📁 archivos": await AyudaArchivos(); break;
            case "carpeta": await VerCarpeta(Archivos.Resolver(arg)); break;
            case "archivo": await PedirArchivo(arg); break;
            case "buscar": await Buscar(arg); break;
            case "informe": await InformePdf(); break;
            case "ajustes" or "⚙ ajustes": await Ajustes(); break;
            default:
                await tg.Enviar(Chat, "No entendí 🤔 Usa los botones de abajo o escribe /ayuda.", Menu);
                break;
        }
    }

    // "/carpeta descargas" → ("carpeta", "descargas"); "📊 Estado" → ("📊 estado", "")
    static (string, string) Separar(string texto)
    {
        if (!texto.StartsWith('/')) return (texto.ToLowerInvariant(), "");
        var espacio = texto.IndexOf(' ');
        var cmd = espacio < 0 ? texto[1..] : texto[1..espacio];
        cmd = cmd.Split('@')[0].ToLowerInvariant();
        return (cmd, espacio < 0 ? "" : texto[(espacio + 1)..].Trim());
    }

    async Task AvisarIntruso(JsonNode? de)
    {
        if (_avisoIntruso) return;
        _avisoIntruso = true;
        var nombre = $"{de?["first_name"]} {de?["last_name"]}".Trim();
        var usuario = de?["username"] is { } u ? $" (@{u})" : "";
        await EnviarSeguro($"⚠️ Alguien que no eres tú intentó usar tu bot: {nombre}{usuario}. Lo ignoré.");
    }

    Task Ayuda() => tg.Enviar(Chat,
        "🤖 Soy el bot de tu PC. Usa los botones de abajo o estos comandos:\n\n" +
        "📊 /estado: resumen de salud\n" +
        "🌡 /temperatura · 🔋 /bateria · 💻 /recursos\n" +
        "📸 /captura: foto de la pantalla\n" +
        "📷 /camara: foto con la cámara\n" +
        "📋 /programas: ver y cerrar programas\n" +
        "🔌 /energia: apagar, reiniciar, suspender, bloquear\n" +
        "⏱ /apagar_en 60: apagar en 60 minutos · /cancelar\n" +
        "🔊 /sonido: volumen y música\n" +
        "📁 /carpeta descargas · /archivo ruta · /buscar nombre\n" +
        "📥 Mándame cualquier archivo o foto y lo guardo en Descargas\\Desde Telegram\n" +
        "📄 /informe: informe PDF de salud\n" +
        "⚙ /ajustes\n\n" +
        "🔔 Te aviso solo si: la batería está baja o llena, el PC se calienta mucho, el disco se llena, " +
        "hubo un pantallazo azul, o alguien prende o desbloquea tu PC.", Menu);

    // ==================== Ver el PC ====================

    static string Bolita(Estado e) => e switch
    {
        Estado.Bien => "🟢",
        Estado.Atencion => "🟡",
        Estado.Problema => "🔴",
        _ => "⚪",
    };

    async Task MostrarEstado()
    {
        await tg.Enviar(Chat, "🔎 Revisando el PC...");
        var r = await Revisor.RevisarAsync();
        var lineas = new List<string>
        {
            $"{Bolita(r.EstadoGeneral)} {Colores.Etiqueta(r.EstadoGeneral)}",
            $"{r.Equipo.Fabricante} {r.Equipo.Modelo}",
            "",
        };
        lineas.AddRange(r.Secciones.Select(s => $"{Bolita(s.Estado)} {s.Nombre}: {s.Resumen}"));
        if (Vigilante.LeerBateria() is ({ } pct, { } enchufado))
            lineas.Add($"\n🔋 Batería: {pct}% {(enchufado ? "(enchufado)" : "(usando batería)")}");
        var problemas = r.Secciones.SelectMany(s => s.Hallazgos).Where(h => h.Estado is SaludNotebook.Estado.Atencion or SaludNotebook.Estado.Problema).ToList();
        if (problemas.Count > 0)
        {
            lineas.Add("\nQué hacer:");
            lineas.AddRange(problemas.Take(5).Select(h => $"• {h.Titulo}: {h.Explicacion}"));
        }
        await tg.Enviar(Chat, string.Join("\n", lineas));
    }

    async Task Temperaturas()
    {
        var (sistema, gpus) = await Task.Run(() => (Temperatura.LeerSistema(), Temperatura.LeerGpus()));
        var lineas = new List<string> { "🌡 Temperaturas ahora:" };
        if (sistema is { } s) lineas.Add($"{(s >= 90 ? "🔴" : s >= 80 ? "🟡" : "🟢")} Sistema: {s:0} °C");
        foreach (var g in gpus)
            lineas.Add($"{(g.Temp >= 87 ? "🔴" : g.Temp >= 80 ? "🟡" : "🟢")} {g.Nombre}: {g.Temp} °C (en uso {g.Uso}%)");
        if (lineas.Count == 1) lineas.Add("Este PC no muestra sus sensores de temperatura.");
        await tg.Enviar(Chat, string.Join("\n", lineas));
    }

    async Task BateriaInfo()
    {
        if (Vigilante.LeerBateria() is not ({ } pct, { } enchufado))
        {
            await tg.Enviar(Chat, "Este PC no tiene batería.");
            return;
        }
        var s = await Task.Run(Bateria.Revisar);
        var salud = s.Metricas.TryGetValue("salud", out var v) ? $"\n❤️ Vida útil: {v:0}% de su capacidad original" : "";
        var ciclos = s.Metricas.TryGetValue("ciclos", out var c) ? $"\n🔁 Ciclos de carga: {c:0}" : "";
        await tg.Enviar(Chat, $"🔋 {pct}% {(enchufado ? "🔌 enchufado" : "(usando batería)")}{salud}{ciclos}");
    }

    async Task Recursos()
    {
        var texto = await Task.Run(() =>
        {
            var os = Wmi.Primero("SELECT TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime FROM Win32_OperatingSystem")!;
            var total = Wmi.Numero(os, "TotalVisibleMemorySize") / 1024.0 / 1024;
            var libre = Wmi.Numero(os, "FreePhysicalMemory") / 1024.0 / 1024;
            var cpu = Wmi.Primero("SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
            var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            var encendido = Wmi.Fecha(os, "LastBootUpTime") is { } f ? DateTime.Now - f : TimeSpan.Zero;
            return $"💻 Procesador: {(cpu == null ? "-" : Wmi.Numero(cpu, "PercentProcessorTime") + "%")}\n" +
                   $"🧠 RAM: {total - libre:0.0} de {total:0} GB en uso ({(total - libre) * 100 / total:0}%)\n" +
                   $"💾 Disco {c.Name.TrimEnd('\\')}: {c.AvailableFreeSpace / 1e9:0} GB libres de {c.TotalSize / 1e9:0} GB\n" +
                   $"⏱ Encendido hace: {(encendido.TotalDays >= 1 ? $"{(int)encendido.TotalDays} d " : "")}{encendido.Hours} h {encendido.Minutes} min";
        });
        await tg.Enviar(Chat, texto);
    }

    async Task Captura()
    {
        var imagen = await Task.Run(Pantalla.Capturar);
        await tg.EnviarFoto(Chat, imagen, $"🖥 Pantalla a las {DateTime.Now:HH:mm}");
    }

    async Task FotoCamara()
    {
        await tg.Enviar(Chat, "📷 Sacando foto...");
        try
        {
            await tg.EnviarFoto(Chat, await Camara.Foto(), $"📷 {DateTime.Now:HH:mm}");
        }
        catch (Exception ex)
        {
            await tg.Enviar(Chat, "No pude usar la cámara. Revisa en Windows: Configuración > Privacidad y seguridad > Cámara, " +
                                  $"que esté activado \"Permitir que las aplicaciones de escritorio accedan a la cámara\".\n({ex.Message})");
        }
    }

    async Task InformePdf()
    {
        await tg.Enviar(Chat, "📄 Preparando el informe...");
        var r = await Revisor.RevisarAsync();
        var ruta = Path.Combine(Path.GetTempPath(), $"Informe Salud PC {DateTime.Now:yyyy-MM-dd HH-mm}.pdf");
        await Task.Run(() => Informe.Generar(r, new List<ActualizacionPendiente>(), Historial.LeerSesiones(), ruta));
        await tg.EnviarArchivo(Chat, ruta);
        File.Delete(ruta);
    }

    // ==================== Programas ====================

    async Task ListarProgramas()
    {
        await tg.Enviar(Chat, "📋 Midiendo programas abiertos...");
        var lista = await Task.Run(Programas.ListarAbiertos);
        _programas.Clear();
        foreach (var p in lista) _programas[p.Proceso] = p;

        if (lista.Count == 0)
        {
            await tg.Enviar(Chat, "No hay programas abiertos que se puedan cerrar.");
            return;
        }
        var texto = "Toca uno para cerrarlo:\n\n" + string.Join("\n", lista.Select(p =>
            $"{(p.TieneVentana ? "🪟" : "⚙")} {p.Nombre} · {p.RamTexto} · CPU {p.CpuTexto}"));
        var botones = lista.Take(20).Select(p => new[] { new { text = $"❌ {Recortar(p.Nombre, 30)}", callback_data = Dato("c", p.Proceso) } });
        await tg.Enviar(Chat, texto, new { inline_keyboard = botones });
    }

    // ==================== Energía ====================

    Task MenuEnergia() => tg.Enviar(Chat, "🔌 ¿Qué hago?", new
    {
        inline_keyboard = new object[]
        {
            new[] { Boton("🔒 Bloquear", "e:bloquear"), Boton("😴 Suspender", "e:suspender") },
            new[] { Boton("🔄 Reiniciar", "e:reiniciar"), Boton("⏻ Apagar", "e:apagar") },
            new[] { Boton("⏱ En 30 min", "e:t30"), Boton("⏱ En 1 h", "e:t60"), Boton("⏱ En 2 h", "e:t120") },
            new[] { Boton("❌ Cancelar apagado programado", "e:cancelar") },
        },
    });

    async Task ApagarEn(string arg)
    {
        if (!int.TryParse(arg, out var minutos) || minutos <= 0 || minutos > 24 * 60)
        {
            await tg.Enviar(Chat, "Dime en cuántos minutos, por ejemplo: /apagar_en 60");
            return;
        }
        Energia.ApagarEn(TimeSpan.FromMinutes(minutos));
        await tg.Enviar(Chat, $"⏱ Listo, el PC se apagará a las {DateTime.Now.AddMinutes(minutos):HH:mm}. Para cancelarlo: /cancelar");
    }

    async Task CancelarApagado() =>
        await tg.Enviar(Chat, Energia.CancelarApagado() ? "✅ Apagado programado cancelado." : "No había ningún apagado programado.");

    // ==================== Sonido ====================

    Task MenuSonido() => tg.Enviar(Chat, "🔊 Volumen y música:", new
    {
        inline_keyboard = new object[]
        {
            new[] { Boton("🔉 Bajar", "s:bajar"), Boton("🔊 Subir", "s:subir"), Boton("🔇 Silenciar", "s:mute") },
            new[] { Boton("⏮", "s:anterior"), Boton("⏯ Pausa/Play", "s:play"), Boton("⏭", "s:siguiente") },
        },
    });

    // ==================== Archivos ====================

    Task AyudaArchivos() => tg.Enviar(Chat,
        "📁 Archivos del PC:\n\n" +
        "• Ver una carpeta: /carpeta descargas (también: escritorio, documentos, imagenes, videos, juegos, o una ruta como C:\\Users)\n" +
        "• Pedir un archivo: /archivo descargas\\nombre.pdf\n" +
        "• Buscar por nombre: /buscar factura\n" +
        "• Mandarme un archivo o foto: lo guardo en Descargas\\Desde Telegram\n\n" +
        "En las listas puedes tocar las carpetas y archivos para abrirlos o pedírmelos.", new
        {
            inline_keyboard = new object[]
            {
                new[] { Boton("🖥 Escritorio", Dato("d", Archivos.Resolver("escritorio"))), Boton("📥 Descargas", Dato("d", Archivos.Resolver("descargas"))) },
                new[] { Boton("📄 Documentos", Dato("d", Archivos.Resolver("documentos"))), Boton("🖼 Imágenes", Dato("d", Archivos.Resolver("imagenes"))) },
            },
        });

    async Task VerCarpeta(string ruta)
    {
        if (!Directory.Exists(ruta))
        {
            await tg.Enviar(Chat, $"No encontré la carpeta: {ruta}");
            return;
        }
        var dir = new DirectoryInfo(ruta);
        var botones = new List<object[]>();
        if (dir.Parent != null) botones.Add(new[] { Boton("⬆ Subir", Dato("d", dir.Parent.FullName)) });
        try
        {
            botones.AddRange(dir.GetDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden)).OrderBy(d => d.Name).Take(15)
                .Select(d => new[] { Boton($"📁 {Recortar(d.Name, 40)}", Dato("d", d.FullName)) }));
            botones.AddRange(dir.GetFiles().Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden)).OrderByDescending(f => f.LastWriteTime).Take(15)
                .Select(f => new[] { Boton($"📄 {Recortar(f.Name, 32)} ({Formato.Tamano(f.Length)})", Dato("f", f.FullName)) }));
            await tg.Enviar(Chat, Archivos.Listar(ruta), new { inline_keyboard = botones });
        }
        catch (UnauthorizedAccessException)
        {
            await tg.Enviar(Chat, $"No tengo permiso para ver {ruta}.");
        }
    }

    async Task PedirArchivo(string arg)
    {
        var ruta = Archivos.Resolver(arg);
        if (Directory.Exists(ruta)) await VerCarpeta(ruta);
        else await EnviarArchivo(ruta);
    }

    async Task EnviarArchivo(string ruta)
    {
        if (!File.Exists(ruta))
        {
            await tg.Enviar(Chat, $"No encontré el archivo: {ruta}\nPrueba con /buscar parte_del_nombre");
            return;
        }
        var largo = new FileInfo(ruta).Length;
        if (largo > Telegram.MaxSubida)
        {
            await tg.Enviar(Chat, $"Ese archivo pesa {Formato.Tamano(largo)} y Telegram solo deja mandar hasta 50 MB por bot.");
            return;
        }
        await tg.Enviar(Chat, $"📤 Enviando {Path.GetFileName(ruta)}...");
        await tg.EnviarArchivo(Chat, ruta);
    }

    async Task Buscar(string texto)
    {
        if (texto.Length < 2)
        {
            await tg.Enviar(Chat, "Dime qué buscar, por ejemplo: /buscar factura");
            return;
        }
        var encontrados = await Task.Run(() => Archivos.Buscar(texto));
        if (encontrados.Count == 0)
        {
            await tg.Enviar(Chat, $"No encontré archivos con \"{texto}\" en Escritorio, Documentos, Descargas, Imágenes, Videos ni Juegos.");
            return;
        }
        var botones = encontrados.Select(f => new[] { Boton($"📄 {Recortar(f.Name, 32)} ({Formato.Tamano(f.Length)})", Dato("f", f.FullName)) });
        await tg.Enviar(Chat, $"🔎 Encontré {encontrados.Count}" + (encontrados.Count == 15 ? " (o más)" : "") + ". Toca uno para que te lo mande:\n\n" +
                              string.Join("\n", encontrados.Select(f => f.FullName)), new { inline_keyboard = botones });
    }

    async Task GuardarRecibido(string idArchivo, string nombre, long tamano)
    {
        if (tamano > Telegram.MaxBajada)
        {
            await tg.Enviar(Chat, "Ese archivo pesa más de 20 MB y Telegram no deja que los bots descarguen archivos tan grandes.");
            return;
        }
        var destino = Archivos.RutaRecibido(nombre);
        await tg.Descargar(idArchivo, destino);
        await tg.Enviar(Chat, $"📥 Guardado en:\n{destino}");
    }

    // ==================== Ajustes ====================

    Task Ajustes() => tg.Enviar(Chat, "⚙ Ajustes del bot:", BotonesAjustes());

    object BotonesAjustes() => new
    {
        inline_keyboard = new[]
        {
            new[] { Boton($"📷 Foto al encender o desbloquear: {(config.FotoAlEncender ? "SÍ" : "NO")}", "a:foto") },
        },
    };

    // ==================== Botones ====================

    async Task AlTocarBoton(JsonObject c)
    {
        var id = c["id"]!.GetValue<string>();
        if (c["from"]?["id"]?.GetValue<long>() != Chat)
        {
            await tg.ResponderBoton(id, "No autorizado");
            return;
        }
        var datos = c["data"]?.GetValue<string>() ?? "";
        var mensaje = c["message"]?["message_id"]?.GetValue<long>() ?? 0;
        var (tipo, valor) = (datos.Split(':', 2)[0], datos.Contains(':') ? datos.Split(':', 2)[1] : "");

        switch (tipo)
        {
            case "c":
                await tg.ResponderBoton(id, "Cerrando...");
                if (!_programas.TryGetValue(valor, out var prog))
                {
                    await tg.Enviar(Chat, "Esa lista ya es antigua. Pide /programas de nuevo.");
                    break;
                }
                var resultado = await Task.Run(() => Programas.Cerrar(prog));
                await tg.Enviar(Chat, $"{prog.Nombre}: {resultado}");
                break;

            case "e":
                await AccionEnergia(id, mensaje, valor);
                break;

            case "s":
                switch (valor)
                {
                    case "bajar": Sonido.BajarVolumen(); break;
                    case "subir": Sonido.SubirVolumen(); break;
                    case "mute": Sonido.Silenciar(); break;
                    case "anterior": Sonido.CancionAnterior(); break;
                    case "play": Sonido.PausarReanudar(); break;
                    case "siguiente": Sonido.CancionSiguiente(); break;
                }
                await tg.ResponderBoton(id, "✔");
                break;

            case "d" or "f":
                await tg.ResponderBoton(id);
                if (int.TryParse(valor, out var n) && _rutas.TryGetValue(n, out var ruta))
                {
                    if (tipo == "d") await VerCarpeta(ruta);
                    else await EnviarArchivo(ruta);
                }
                else
                {
                    await tg.Enviar(Chat, "Esa lista ya es antigua (reinicié el bot). Pídela de nuevo.");
                }
                break;

            case "a" when valor == "foto":
                config.FotoAlEncender = !config.FotoAlEncender;
                config.Guardar();
                await tg.ResponderBoton(id, config.FotoAlEncender ? "Activado" : "Desactivado");
                await tg.Editar(Chat, mensaje, "⚙ Ajustes del bot:", BotonesAjustes());
                break;

            default:
                await tg.ResponderBoton(id);
                break;
        }
    }

    async Task AccionEnergia(string id, long mensaje, string accion)
    {
        switch (accion)
        {
            case "bloquear":
                await tg.ResponderBoton(id, Energia.Bloquear() ? "🔒 Bloqueado" : "No se pudo bloquear");
                break;
            case "suspender":
                await tg.ResponderBoton(id, "😴 Suspendiendo");
                await tg.Enviar(Chat, "😴 Suspendiendo el PC. No te voy a poder responder hasta que lo despiertes.");
                Energia.Suspender();
                break;
            case "apagar" or "reiniciar":
                // Las acciones que cierran todo piden confirmación.
                await tg.ResponderBoton(id);
                var verbo = accion == "apagar" ? "apagar" : "reiniciar";
                await tg.Editar(Chat, mensaje, $"⚠️ ¿Seguro que quieres {verbo} el PC? Lo que no esté guardado se pierde.", new
                {
                    inline_keyboard = new[] { new[] { Boton($"✅ Sí, {verbo}", $"e:{accion}!"), Boton("Cancelar", "e:no") } },
                });
                break;
            case "apagar!":
                await tg.ResponderBoton(id, "⏻ Apagando");
                await tg.Editar(Chat, mensaje, "⏻ Apagando el PC en 5 segundos. ¡Chao! 👋");
                Energia.Apagar();
                break;
            case "reiniciar!":
                await tg.ResponderBoton(id, "🔄 Reiniciando");
                await tg.Editar(Chat, mensaje, "🔄 Reiniciando el PC en 5 segundos. Te aviso cuando vuelva.");
                Energia.Reiniciar();
                break;
            case "no":
                await tg.ResponderBoton(id, "Cancelado");
                await tg.Editar(Chat, mensaje, "👌 No hice nada.");
                break;
            case "cancelar":
                await tg.ResponderBoton(id);
                await CancelarApagado();
                break;
            case var t when t.StartsWith('t') && int.TryParse(t[1..], out var minutos):
                await tg.ResponderBoton(id);
                await ApagarEn(minutos.ToString());
                break;
        }
    }

    static object Boton(string texto, string datos) => new { text = texto, callback_data = datos };

    // Guarda la ruta y devuelve un identificador corto para el botón.
    string Dato(string tipo, string ruta)
    {
        if (tipo == "c") return $"c:{Recortar(ruta, 60)}";
        var n = ++_siguienteRuta;
        _rutas[n] = ruta;
        return $"{tipo}:{n}";
    }

    static string Recortar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
