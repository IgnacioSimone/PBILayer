# PBI Layer

**Documentación de modelos de Power BI.** (Antes llamada "PBI Context".)

Aplicación de escritorio para Windows (también se registra como herramienta externa de Power BI Desktop) que documenta un modelo (medidas DAX, tablas,
columnas, relaciones, consultas Power Query) y su reporte (qué visual usa qué campo, en
qué página), con exportación a Word, Excel, HTML, CSV, JSON y Markdown para IA.

**Este proyecto no es un producto de Microsoft ni está afiliado, patrocinado o
respaldado por Microsoft Corporation.** "Power BI" es una marca registrada de
Microsoft. Se usa aquí únicamente para describir la compatibilidad de esta
herramienta, tal como lo hacen otras herramientas externas de la comunidad
(DAX Studio, Tabular Editor, ALM Toolkit).

## Qué hace

- **Modelo:** medidas DAX, tablas, columnas, relaciones y consultas de Power Query (M), con el código completo y resaltado de sintaxis.
- **Reporte:** páginas, visuales, qué campo usa cada visual y los filtros aplicados.
- **Mapa de relaciones:** las tablas como cajas conectadas, con cardinalidad, dirección del filtro y relaciones inactivas.
- **Uso y limpieza:** detecta tablas, columnas, medidas y consultas que nada usa (candidatas a eliminar).
- **Rendimiento y comparación:** alertas de buenas prácticas del modelo y comparación entre dos versiones.
- **Exportación:** Word, Excel, HTML, CSV, JSON y un Markdown de "contexto para IA" para pegar en un asistente de IA.
- Interfaz en **español e inglés**, con tema claro y oscuro.

## Requisitos

- **Windows 10 o 11 de 64 bits.** No hace falta instalar .NET: la app lo trae incluido.
- **Power BI Desktop instalado** (versión de Microsoft Store o instalador clásico). Es lo que permite leer el modelo de un tablero.
- Permisos de administrador **solo para instalar**.
- Unos 50 MB de descarga y unos 160 MB de espacio una vez instalada.

## Instalación

### Instalador (recomendado)

1. Bajá `PBILayer-Setup-<versión>.exe` de la página de [Releases](https://github.com/IgnacioSimone/PBILayer/releases)
   y ejecutalo. Pide permisos de administrador porque registra la herramienta externa en una carpeta de
   sistema que lee Power BI Desktop. Instala en `Program Files` y crea el acceso directo del menú Inicio. Si tenías la app con su
   nombre anterior ("PBI Context"), la desinstala sola y trae tus preferencias.
2. Abrí **PBI Layer** desde el menú Inicio (busca solo el tablero abierto en Power BI Desktop) o desde
   Power BI Desktop → pestaña **Herramientas externas** → **PBI Layer**.

Instalación silenciosa: `PBILayer-Setup-<versión>.exe /VERYSILENT /NORESTART`.
Los archivos publicados traen un `.sha256`; podés verificar la descarga con `Get-FileHash`.
El instalador **no está firmado digitalmente**, así que Windows SmartScreen puede mostrar un aviso la primera vez.

### Desde el código fuente

1. Instalá el SDK de .NET 8 e [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).
2. `./build-installer.ps1` corre los tests, publica la app y arma `installer_output\PBILayer-Setup-<versión>.exe`
   junto con su `.sha256`. (`install.ps1` queda como alternativa sin instalador: copia la publicación a tu perfil.)
3. Para publicar una versión: subí `<Version>` en `PBIExplorer.csproj`, agregá sus notas en `CHANGELOG.md`, y
   empujá la etiqueta `vX.Y.Z`: el workflow `release.yml` arma el instalador y crea la release en GitHub.

## Actualizaciones

En **Acerca de** (menú ⚙ de la barra lateral) se ve la versión, se busca una versión nueva y se activa o
no la búsqueda automática. Si hay una versión nueva con instalador y hash publicados, **Descargar e instalar**
la baja, verifica el SHA-256 y lanza el instalador. Ojo: esto solo funciona si las releases son accesibles
públicamente; con un repositorio privado la app informa que no hay versiones para consultar.

## Qué lee y qué no lee (privacidad)

- Se conecta al modelo que Power BI Desktop ya tiene abierto en memoria: o bien lanzada desde la pestaña
  [Herramientas externas](https://learn.microsoft.com/power-bi/transform-model/desktop-external-tools),
  o bien sola, detectando el tablero abierto (por el proceso de Power BI Desktop y los archivos de su
  carpeta de trabajo). Si abrís un `.pbix` que no está abierto en Power BI Desktop, lo abre ahí para poder leer su modelo.
- Solo consulta las vistas de metadata del motor (`$SYSTEM.TMSCHEMA_*`): nombres de
  tablas/columnas/medidas, fórmulas DAX, código M de Power Query, relaciones. **Nunca
  ejecuta una consulta `EVALUATE` ni de ningún otro tipo sobre las tablas de datos** —
  no hay forma de que esta herramienta lea una fila real de tu tablero.
- Para leer visuales y qué campo usa cada uno, abre el archivo `.pbix` como un zip y lee
  únicamente la definición del reporte (`Report/definition` o `Report/Layout`), nunca los
  datos del modelo empaquetados adentro.
- Para encontrar automáticamente el `.pbix` abierto, busca por nombre de archivo en el
  Escritorio, Documentos, OneDrive y el resto de tus discos locales. No sube nada a
  ningún servidor: toda la búsqueda y lectura ocurre en tu máquina.
- El código DAX/M que se exporta pasa por un enmascarado automático de patrones
  sensibles (emails, DNI, CUIT/CUIL, contraseñas/tokens en cadenas de conexión, rutas
  locales que revelan tu usuario de Windows) antes de escribirse en cualquier exportación (Word, Excel, HTML, CSV, Markdown).
  Aun así, revisá el archivo exportado antes de compartirlo — ninguna heurística
  automática reemplaza una revisión humana si tu modelo tiene texto libre con datos
  personales embebidos a mano en una fórmula.
- **No hay telemetría.** La única conexión de red posible es **buscar actualizaciones**: está apagada por
  defecto, solo se activa si lo elegís (la app te pregunta una vez, y se cambia en *Acerca de*), hace una
  consulta por día a la API pública de GitHub (`releases/latest`) sin enviar nada tuyo, del modelo ni del
  tablero, y baja el instalador solo desde GitHub y verificando su SHA-256 antes de ejecutarlo.

## Comentarios y problemas

Desde la app: menú ⚙ → **Enviar comentarios o reportar un problema**, o directamente en
[Issues](https://github.com/IgnacioSimone/PBILayer/issues/new/choose). No pegues datos sensibles: la app solo lee
metadata, pero lo que escribas en un issue puede ser público. Las vulnerabilidades se reportan en privado (ver [SECURITY.md](SECURITY.md)).

## Licencia y dependencias de terceros

Este proyecto se distribuye bajo licencia MIT (ver `LICENSE`) — sin garantía, "tal cual".

Usa estos componentes de terceros, redistribuidos dentro de la publicación self-contained:

- **DocumentFormat.OpenXml** (Microsoft, licencia MIT) — generación de los documentos Word y Excel.
- **CommunityToolkit.Mvvm** (.NET Foundation, licencia MIT) — estructura MVVM de la interfaz.
- **System.Management** (Microsoft, licencia MIT) — para detectar el motor local de Power BI Desktop.
- **Microsoft.AnalysisServices.AdomdClient** (Microsoft, EULA de redistribución del SQL
  Server Feature Pack) — cliente para conectarse al motor analítico local que expone
  Power BI Desktop. Es el mismo componente que redistribuyen otras herramientas
  públicas de la comunidad (DAX Studio, Tabular Editor) por el mismo motivo.

## Alcance

Uso previsto: analistas y desarrolladores de Power BI documentando **sus propios**
modelos. No está pensado para inspeccionar tableros ajenos sin autorización, ni para
extraer datos personales — por diseño, no puede hacer esto último.
