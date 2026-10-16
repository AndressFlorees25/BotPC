# Bot de Telegram para el PC

Bot (@Andresito_pc_bot) que corre en segundo plano y permite ver y controlar el PC desde Telegram:
estado de salud (reutiliza las revisiones de `..\SaludNotebook`), temperaturas, batería, captura de pantalla,
foto con la cámara, cerrar programas, apagar/reiniciar/suspender/bloquear, volumen y música, y enviar/recibir archivos.

Avisa solo cuando: la batería está baja o llena, el PC se calienta mucho, el disco se llena, hubo un pantallazo azul
o apagado inesperado, o alguien prende/desbloquea el PC (con foto opcional).

Solo obedece al chat configurado. El token y el chat van en `%LOCALAPPDATA%\BotPC\config.json` (fuera del repositorio):

```json
{ "Token": "123:ABC...", "ChatId": 123456789, "FotoAlEncender": false }
```

## Instalar

Como administrador: `.\instalar.ps1` (publica en `%LOCALAPPDATA%\Programs\BotPC` y crea la tarea programada
"BotPC\Bot de Telegram" que lo arranca al iniciar sesión). Registro en `%LOCALAPPDATA%\BotPC\bot.log`.
