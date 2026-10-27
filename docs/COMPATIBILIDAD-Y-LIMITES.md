# Compatibilidad real de Games for Windows LIVE 2026

> **Estado de esta documentación:** describe la compatibilidad objetivo y las fuentes públicas que existen. No significa que todos los adaptadores estén implementados en esta versión. Consulta la tabla de estado del final antes de asumir que un servicio ya funciona dentro de la aplicación.

Games for Windows LIVE 2026 es un overlay comunitario inspirado en el Guide de Xbox 360. No es un producto oficial de Microsoft, Steam, Epic, GOG ni RetroAchievements. No solicita contraseñas de esas plataformas ni debe hacerse pasar por ellas.

## Resumen rápido

| Fuente | Qué puede aportar | Límite real |
|---|---|---|
| Logros locales de este overlay | Eventos que el propio programa puede detectar y su pequeño catálogo local | No son logros de los juegos ni se sincronizan con tiendas |
| RAWG | Metadatos de juegos y logros que existan en su catálogo: nombre, descripción, imagen y datos globales | Requiere una clave API; no conoce el progreso personal del usuario |
| RetroAchievements | Catálogo de juegos, sets de logros, metadatos y datos de cuenta mediante su API | Solo juegos/sistemas con set de logros y hashes compatibles; requiere cuenta/clave API para API; el seguimiento de desbloqueos ocurre normalmente dentro de un emulador compatible |
| Steam | Logros y estadísticas de juegos que los implementen en Steamworks | No todos los juegos tienen logros; acceso y eventos dependen de las APIs y permisos disponibles. La app todavía no tiene adaptador Steam terminado |
| Epic Games Store / Epic Online Services | Logros definidos por los desarrolladores para sus juegos y cuentas | Solo productos que hayan implementado y publicado logros; no es un catálogo universal de cualquier ejecutable |
| GOG GALAXY | Logros que el juego/cliente de GOG soporte | La cobertura depende del título y del cliente; aún no hay adaptador de GOG conectado |
| Xbox / Xbox PC | Datos de logros disponibles a través de las interfaces oficiales o servicios autorizados aplicables | No se debe asumir que todo el catálogo de Xbox 360/PC se puede leer con una API pública universal; todavía no hay adaptador Xbox conectado |
| EA app, Ubisoft Connect, Battle.net, Microsoft Store y otros | Solo los logros que exponga el juego/cliente o una interfaz permitida y documentada | Sin una API pública adecuada o un evento fiable, el overlay no puede garantizar lectura del progreso |
| Juegos sin tienda, instaladores independientes o copias sin integración de tienda | El overlay puede lanzar el ejecutable y puede mostrar logros locales si existe un detector compatible | No se detectan automáticamente todos los logros internos de cualquier juego. Se necesita un detector específico, soporte del juego o un sistema comunitario como RetroAchievements |

## RetroAchievements: emuladores y sistemas

RetroAchievements mantiene una lista viva de sistemas, juegos con sets de logros y emuladores compatibles. La página oficial de descargas permite filtrar por sistema y plataforma; la documentación de compatibilidad enumera los cores/emuladores recomendados y también los no compatibles o no probados.

### Emuladores con soporte publicado por RetroAchievements

Los siguientes aparecen en la página oficial de descargas/documentación de RetroAchievements. La disponibilidad depende del sistema, plataforma y versión del emulador:

- **RetroArch** — múltiples sistemas mediante cores libretro compatibles.
- **RALibRetro** — múltiples sistemas mediante cores compatibles.
- **BizHawk** — usa sus propios cores compatibles; no todos los cores funcionan.
- **DuckStation** — PlayStation (PS1).
- **PCSX2** — PlayStation 2.
- **PPSSPP** — PlayStation Portable (PSP).
- **Dolphin** — GameCube y Wii, sujeto a versión y configuración indicadas por RetroAchievements.
- **Flycast** — Dreamcast y arcade compatibles (por ejemplo, Naomi/Atomiswave según el juego/core).
- **SkyEmu** — sistemas portátiles clásicos compatibles, incluidos GB/GBC/GBA según versión.
- **Emuladores específicos adicionales** en la lista oficial, por ejemplo RANes, RASnes9x, RAP64, RAppleWin, RAQUASI88, RAMeka, WinArcadia y otros, según sistema.

**Importante:** que un emulador figure aquí no significa que todos sus cores o todas las ROMs funcionen. Usa la página oficial actualizada y el apartado de emuladores/cores no compatibles para elegir la combinación recomendada. Las versiones y los requisitos pueden cambiar después de publicarse esta documentación.

### Sistemas que aparecen en el catálogo de RetroAchievements

La web oficial muestra decenas de sistemas (actualmente la página de descargas anuncia 54). Entre ellos se encuentran, según el catálogo vigente: 3DO, Amstrad CPC, Apple II, Arcade, Arduboy, Atari 2600/7800/Jaguar/Lynx, ColecoVision, Dreamcast, Famicom/NES y Famicom Disk System, Game Boy, Game Boy Color, Game Boy Advance, Game Gear, GameCube, Genesis/Mega Drive, Master System, MSX, Neo Geo CD, Neo Geo Pocket, Nintendo 64, Nintendo DS/DSi, PC Engine/TurboGrafx-16 y CD, PC-FX, PlayStation, PlayStation 2, PSP, Saturn, Sega CD, SG-1000, SNES/Super Famicom, Virtual Boy, Wii, WonderSwan y otros sistemas retro.

La lista exacta es dinámica. **La compatibilidad de un sistema no equivale a que todos sus juegos tengan logros**: cada juego debe tener un set publicado y la ROM/imagen debe coincidir con un hash compatible. Las traducciones, hacks, revisiones regionales, prototipos, dumps modificados o versiones distintas pueden no coincidir.

### Qué necesita RetroAchievements

1. Una cuenta de RetroAchievements y su clave API para las funciones web que la requieran.
2. Un emulador/core compatible con el sistema y con la versión recomendada.
3. Un juego que tenga un set de logros publicado y una imagen de juego reconocida.
4. Configurar la autenticación en el emulador o integración compatible.
5. Para que Games for Windows LIVE 2026 muestre notificaciones de desbloqueo, hace falta implementar y probar el adaptador de eventos/polling. **Ese adaptador aún no está conectado en esta versión.** La clave API no debe incluirse en el repositorio ni en un instalador público.

## Juegos de PC y tiendas: qué significa “compatible”

No existe una lista única de todos los juegos de PC con logros que pueda consultarse desde una API pública universal. La disponibilidad depende de cada juego y del servicio con el que se publicó.

### Steam
- Steamworks tiene logros y estadísticas para juegos que el desarrollador configuró para ello.
- Los títulos pueden no tener logros, tenerlos incompletos o usar un sistema propio.
- La documentación de Steamworks explica cómo los desarrolladores integran y publican logros.
- **Estado de este proyecto:** no hay un adaptador Steam real conectado todavía. La presencia de Steam instalado o de un juego en la biblioteca no significa que el overlay ya pueda leer sus logros.

### Epic Games Store / Epic Online Services
- Epic ofrece logros de Epic Games Store y de Epic Online Services para juegos que los desarrolladores hayan configurado y publicado.
- La interfaz de logros de EOS no convierte automáticamente todos los juegos de una biblioteca en juegos con logros.
- **Estado de este proyecto:** sin adaptador EOS/Epic conectado.

### GOG GALAXY
- Los logros dependen de la integración y soporte del título/cliente.
- Un juego DRM-free puede funcionar sin que el overlay pueda observar su progreso interno.
- **Estado de este proyecto:** sin adaptador GOG conectado.

### Xbox y juegos de Microsoft
- La disponibilidad depende del juego, la plataforma, la cuenta y las interfaces oficiales disponibles.
- No se debe prometer acceso universal a los logros de Xbox 360, Xbox PC o juegos de Microsoft Store por el simple hecho de detectar un proceso.
- **Estado de este proyecto:** sin adaptador Xbox conectado.

### EA app, Ubisoft Connect, Battle.net y otros launchers
- Sus logros, desafíos y estadísticas son específicos del juego y del servicio.
- El overlay no interceptará de forma universal ventanas privadas ni datos internos de otras aplicaciones.
- **Estado de este proyecto:** no hay adaptadores implementados.

### Juegos independientes, instalados manualmente o sin tienda
- La aplicación puede lanzar ejecutables mediante su navegador de archivos.
- Detectar que un juego está abierto solo identifica una sesión; **no demuestra que se haya desbloqueado un logro**.
- Para generar logros reales hacen falta eventos que el juego exponga, un SDK/API autorizada, un plugin/mod compatible o un detector específico por juego.
- Se puede crear un catálogo de logros locales del overlay, pero estos deben etiquetarse como locales y no como logros oficiales del juego.

## RAWG: catálogo de metadatos, no progreso personal

El proveedor opcional `Achievements/RawgCatalogProvider.cs` permite buscar juegos y consultar metadatos de logros por ID de RAWG. Requiere una clave API del usuario, aplica una caché local de siete días y guarda la configuración bajo:

`%LOCALAPPDATA%\GamesForWindowsLIVE2026\providers.json`

La clave nunca debe añadirse a Git. RAWG puede no tener todos los juegos ni todos los logros. Los porcentajes disponibles son estadísticas globales, no el estado de desbloqueo de la persona. Hay que respetar las condiciones y atribución de RAWG.

## Estado de implementación dentro de este repositorio

| Función | Estado |
|---|---|
| Guide compacto con navegación y temas | Existe código; validar compilación y funcionamiento en cada equipo |
| Persistencia del pequeño catálogo local de logros del overlay | Implementada en código |
| Toast de logros mediante puente local de named pipe | Implementado como frontera de integración; no prueba integración con tiendas |
| Proveedor de metadatos RAWG | Código añadido; requiere clave y aún no está conectado a la interfaz |\n| Cliente API de RetroAchievements | Código añadido para catálogo por ID de sistema y progreso de usuario; requiere usuario/clave API y aún no está conectado a la interfaz ni validado en ejecución |
| Catálogo completo de emuladores/juegos sincronizado automáticamente | No implementado |
| RetroAchievements: cliente API de catálogo por sistema y consulta de progreso del usuario | Cliente API añadido en `Achievements/RetroAchievementsCatalogProvider.cs`; todavía no está conectado a la interfaz ni verificado contra una cuenta real |\n| RetroAchievements: notificaciones en vivo de desbloqueos | No implementado; consultar progreso no equivale a recibir eventos en vivo |
| Steam: lectura real de logros y notificaciones | No implementado |
| Epic/EOS: lectura real de logros y notificaciones | No implementado |
| GOG, Xbox, EA, Ubisoft, Battle.net y Microsoft Store | No implementado |
| Detector universal de logros para cualquier juego o copia local | No existe ni debe prometerse |
| Pantalla completa de colección de logros fiel a Blades | Pendiente de la captura de referencia del usuario |
| Compilación verificada de Windows tras estos cambios | Pendiente de ejecutar y revisar el resultado |

## Enlaces oficiales para comprobar la cobertura actual

- [RetroAchievements — descargas, sistemas y emuladores](https://retroachievements.org/downloads)
- [RetroAchievements — compatibilidad de emuladores y cores](https://docs.retroachievements.org/general/emulator-support-and-issues.html)
- [RetroAchievements — emuladores/cores no compatibles o no probados](https://docs.retroachievements.org/developer-docs/unsupported-emulators-and-cores.html)
- [RetroAchievements — documentación de la API](https://api-docs.retroachievements.org/)
- [Steamworks — guía oficial de logros](https://partner.steamgames.com/doc/features/achievements/ach_guide)
- [Epic Online Services — referencia de logros](https://dev.epicgames.com/docs/epic-online-services/player-and-game-data/achievements-interface/achievements-reference)
- [Epic Games Store — descripción de logros](https://dev.epicgames.com/docs/epic-games-store/services/epic-achievements/overview)
- [RAWG — documentación de API](https://rawg.io/apidocs)

## Política de honestidad del producto

La interfaz y la página de descarga deben diferenciar siempre estos estados:

- **Catálogo disponible:** se conocen metadatos del juego o sus logros.
- **Seguimiento disponible:** existe una integración probada que puede leer el progreso real.
- **Detección experimental:** funciona solo con determinadas versiones, cores o configuraciones.
- **No compatible:** no hay una fuente de datos fiable.

No mostrar un logro como desbloqueado basándose únicamente en el nombre del juego, en una lista de metadatos o en una ventana de proceso detectada. No pedir ni guardar contraseñas de tiendas; usar únicamente métodos de autenticación documentados y guardar las claves API de usuario fuera del repositorio.
